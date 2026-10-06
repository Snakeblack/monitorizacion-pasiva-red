using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Monitoring.Persistence.Operations;

// Lag and WAL pressure of one replication slot. ConfirmLagBytes is what the consumer (Debezium) has not yet confirmed and is null for
// slots that do not report a confirmed position; RetainedBytes is the WAL PostgreSQL must keep for the slot.
public sealed record ReplicationSlotStatus(string Name, bool Active, long RetainedBytes, long? ConfirmLagBytes);

public sealed record PipelineSnapshot(double? OldestPendingSeconds, IReadOnlyList<ReplicationSlotStatus> Slots);

// Reads, from the authority itself, the facts the pipeline alerts depend on. Only counts, ages and slot names are returned.
public sealed class PipelineSnapshotReader(MonitoringDbContext dbContext)
{
    public async Task<PipelineSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            double? oldest;
            await using (var command = new NpgsqlCommand("""
                SELECT EXTRACT(EPOCH FROM clock_timestamp() - min(accepted_at))::float8
                FROM monitoring.ingestion_inbox WHERE processed_at IS NULL AND quarantined_at IS NULL
                """, connection))
            {
                oldest = await command.ExecuteScalarAsync(cancellationToken) is double age ? Math.Max(age, 0) : null;
            }
            var slots = new List<ReplicationSlotStatus>();
            await using var slotCommand = new NpgsqlCommand("""
                SELECT slot_name::text, active,
                       COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn), 0)::bigint,
                       pg_wal_lsn_diff(pg_current_wal_lsn(), confirmed_flush_lsn)::bigint
                FROM pg_replication_slots ORDER BY slot_name
                """, connection);
            await using var reader = await slotCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                slots.Add(new ReplicationSlotStatus(reader.GetString(0), reader.GetBoolean(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
            return new PipelineSnapshot(oldest, slots);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions.Search;
using Npgsql;

namespace Monitoring.Persistence.Search;

public sealed class SnapshotLeaseOptions
{
    public int MaxPerSubject { get; set; } = 2;
    public int MaxGlobal { get; set; } = 20;
}

public sealed class PostgresSnapshotLeases(MonitoringDbContext dbContext, SnapshotLeaseOptions options) : ISnapshotLeases
{
    // Serializes admission so concurrent acquisitions cannot jointly exceed a limit.
    private const long AdmissionLock = 7182041002;

    public async Task<Guid> AcquireAsync(string subject, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@lock)", connection, transaction))
            {
                gate.Parameters.AddWithValue("lock", AdmissionLock);
                await gate.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var purge = new NpgsqlCommand("DELETE FROM monitoring.search_snapshot_lease WHERE expires_at<=@now", connection, transaction))
            {
                purge.Parameters.AddWithValue("now", now);
                await purge.ExecuteNonQueryAsync(cancellationToken);
            }
            long global, own;
            await using (var count = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (WHERE subject=@subject) FROM monitoring.search_snapshot_lease
                """, connection, transaction))
            {
                count.Parameters.AddWithValue("subject", subject);
                await using var reader = await count.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                (global, own) = (reader.GetInt64(0), reader.GetInt64(1));
            }
            if (global >= options.MaxGlobal || own >= options.MaxPerSubject)
                throw new SessionSearchException(SessionSearchFailure.Saturated);
            var lease = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO monitoring.search_snapshot_lease(lease_id,subject,expires_at) VALUES (@id,@subject,@expires)
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("id", lease);
                insert.Parameters.AddWithValue("subject", subject);
                insert.Parameters.AddWithValue("expires", expiresAt);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return lease;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public async Task<bool> RecordPitAsync(Guid leaseId, string pitId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE monitoring.search_snapshot_lease
                SET pit_changes = pit_changes + CASE WHEN pit_hash IS NOT NULL AND pit_hash<>@hash THEN 1 ELSE 0 END, pit_hash=@hash
                WHERE lease_id=@id AND expires_at>@now
                """, connection);
            command.Parameters.AddWithValue("id", leaseId);
            command.Parameters.AddWithValue("hash", SHA256.HashData(Encoding.UTF8.GetBytes(pitId)));
            command.Parameters.AddWithValue("now", now);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public async Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("DELETE FROM monitoring.search_snapshot_lease WHERE lease_id=@id", connection);
            command.Parameters.AddWithValue("id", leaseId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }
}

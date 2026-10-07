using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions;
using Npgsql;

namespace Monitoring.Persistence.Sessions;

public enum SuppressionResult { Suppressed, AlreadySuppressed, NotFound }

// Authority-side suppression: one transaction locks the identity, advances its revision, removes the traffic data and
// confirms the permanent delete barrier in the outbox. A replayed older upsert can then never resurrect the session.
public sealed class SessionSuppressor(MonitoringDbContext dbContext)
{
    public async Task<SuppressionResult> SuppressAsync(SessionIdentity identity, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            // Shared publication lock first, then the row lock, matching every other writer so a rebuild's exclusive lock cannot deadlock.
            await OutboxStore.AcquirePublicationLockAsync(connection, transaction, cancellationToken);
            string? state;
            await using (var select = Command("""
                SELECT state FROM monitoring.session_identity
                WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event FOR UPDATE
                """, connection, transaction, identity))
                state = (string?)await select.ExecuteScalarAsync(cancellationToken);
            if (state is null) return SuppressionResult.NotFound;
            if (state == "deleted") return SuppressionResult.AlreadySuppressed;
            await using (var update = Command("""
                UPDATE monitoring.session_identity SET state='deleted',revision=revision+1,deleted_at=clock_timestamp()
                WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
                """, connection, transaction, identity))
                await update.ExecuteNonQueryAsync(cancellationToken);
            // Metadata cascades from the projection row; the accepted inbox event stays for its own retention.
            await using (var delete = Command("""
                DELETE FROM monitoring.session_projection WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
                """, connection, transaction, identity))
                await delete.ExecuteNonQueryAsync(cancellationToken);
            await OutboxStore.WriteDeleteBarrierAsync(connection, transaction, identity, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SuppressionResult.Suppressed;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction, SessionIdentity identity)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site", identity.SiteId);
        command.Parameters.AddWithValue("sensor", identity.SensorId);
        command.Parameters.AddWithValue("event", identity.EventId);
        return command;
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions;
using Npgsql;

namespace Monitoring.Persistence.Sessions;

public sealed class CanonicalSessionInitializer(MonitoringDbContext dbContext)
{
    private const int PageSize = 100;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await PublishSessionsAsync(connection, cancellationToken);
            await PublishDeleteBarriersAsync(connection, cancellationToken);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    // Covers never-canonicalized history and identities whose current revision has not reached the current topic yet,
    // so the upgrade republishes legacy identities exactly once; WriteSessionAsync is idempotent per revision and topic.
    private static async Task PublishSessionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        SessionIdentity? cursor = null;
        while (true)
        {
            var page = new List<(SessionIdentity Identity, string Json, DateTimeOffset AcceptedAt)>();
            await using (var select = new NpgsqlCommand("""
                SELECT s.site_id,s.sensor_id,s.event_id,s.data::text,i.accepted_at
                FROM monitoring.session_projection s JOIN monitoring.ingestion_inbox i USING(site_id,sensor_id,event_id)
                LEFT JOIN monitoring.session_identity d USING(site_id,sensor_id,event_id)
                WHERE (d.event_id IS NULL OR (d.state='active' AND NOT EXISTS (
                        SELECT 1 FROM monitoring.projection_outbox o
                        WHERE o.aggregateid=d.document_key AND o.revision=d.revision AND o.target_topic=(SELECT target_topic FROM monitoring.search_generation WHERE state='active'))))
                  AND (@first OR (s.site_id,s.sensor_id,s.event_id)>(@site,@sensor,@event))
                ORDER BY s.site_id,s.sensor_id,s.event_id LIMIT @limit
                """, connection))
            {
                AddCursor(select, cursor);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    page.Add((new SessionIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2)),
                        reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
            }
            if (page.Count == 0) return;
            foreach (var item in page)
            {
                using var data = JsonDocument.Parse(item.Json);
                if (!CanonicalSession.TryParse(data.RootElement, out var canonical)) continue;
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await OutboxStore.WriteSessionAsync(connection, transaction, item.Identity, canonical!, item.AcceptedAt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            // Invalid historical records are deliberately never reinterpreted; still advance past them.
            cursor = page[^1].Identity;
        }
    }

    // A suppression barrier must exist on the current topic too, so a replayed older upsert cannot resurrect the identity.
    private static async Task PublishDeleteBarriersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        SessionIdentity? cursor = null;
        while (true)
        {
            var page = new List<SessionIdentity>();
            await using (var select = new NpgsqlCommand("""
                SELECT d.site_id,d.sensor_id,d.event_id FROM monitoring.session_identity d
                WHERE d.state='deleted' AND NOT EXISTS (
                        SELECT 1 FROM monitoring.projection_outbox o
                        WHERE o.aggregateid=d.document_key AND o.revision=d.revision AND o.target_topic=(SELECT target_topic FROM monitoring.search_generation WHERE state='active'))
                  AND (@first OR (d.site_id,d.sensor_id,d.event_id)>(@site,@sensor,@event))
                ORDER BY d.site_id,d.sensor_id,d.event_id LIMIT @limit
                """, connection))
            {
                AddCursor(select, cursor);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    page.Add(new SessionIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
            if (page.Count == 0) return;
            foreach (var identity in page)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await OutboxStore.WriteDeleteBarrierAsync(connection, transaction, identity, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            cursor = page[^1];
        }
    }

    private static void AddCursor(NpgsqlCommand command, SessionIdentity? cursor)
    {
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("first", cursor is null);
        command.Parameters.AddWithValue("site", cursor?.SiteId ?? "");
        command.Parameters.AddWithValue("sensor", cursor?.SensorId ?? "");
        command.Parameters.AddWithValue("event", cursor?.EventId ?? "");
    }
}

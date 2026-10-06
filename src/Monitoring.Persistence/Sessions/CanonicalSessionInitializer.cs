using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions;
using Npgsql;

namespace Monitoring.Persistence.Sessions;

public sealed class CanonicalSessionInitializer(MonitoringDbContext dbContext)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            SessionIdentity? cursor = null;
            while (true)
            {
                var page = new List<(SessionIdentity Identity, string Json, DateTimeOffset AcceptedAt)>();
                await using (var select = new NpgsqlCommand("""
                    SELECT s.site_id,s.sensor_id,s.event_id,s.data::text,i.accepted_at
                    FROM monitoring.session_projection s JOIN monitoring.ingestion_inbox i USING(site_id,sensor_id,event_id)
                    LEFT JOIN monitoring.session_metadata m USING(site_id,sensor_id,event_id)
                    WHERE m.event_id IS NULL AND (@first OR (s.site_id,s.sensor_id,s.event_id)>(@site,@sensor,@event))
                    ORDER BY s.site_id,s.sensor_id,s.event_id LIMIT 100
                    """, connection))
                {
                    select.Parameters.AddWithValue("first", cursor is null);
                    select.Parameters.AddWithValue("site", cursor?.SiteId ?? "");
                    select.Parameters.AddWithValue("sensor", cursor?.SensorId ?? "");
                    select.Parameters.AddWithValue("event", cursor?.EventId ?? "");
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
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }
}

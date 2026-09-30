using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Domain.Sessions;
using Npgsql;

namespace Monitoring.Persistence.Sessions;

public sealed class SessionProjector(MonitoringDbContext dbContext)
{
    public async Task RunPassAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            EventKey? upper;
            await using (var command = new NpgsqlCommand("""
                SELECT accepted_at, site_id, sensor_id, event_id FROM monitoring.ingestion_inbox
                WHERE processed_at IS NULL ORDER BY accepted_at DESC,site_id DESC,sensor_id DESC,event_id DESC LIMIT 1
                """, Connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                upper = await reader.ReadAsync(cancellationToken) ? ReadKey(reader) : null;
            }
            if (upper is null)
            {
                return;
            }

            EventKey? cursor = null;
            while (true)
            {
                var keys = await ReadPageAsync(upper, cursor, cancellationToken);
                if (keys.Count == 0)
                {
                    return;
                }
                foreach (var key in keys)
                {
                    await ProjectAsync(key, cancellationToken);
                }
                // Advance over every selected key, including invalid and temporarily locked rows.
                // PostgreSQL performs both ordering and tuple comparisons with the same collation.
                cursor = keys[^1];
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    private async Task<List<EventKey>> ReadPageAsync(EventKey upper, EventKey? cursor, CancellationToken cancellationToken)
    {
        var afterCursor = cursor is null ? string.Empty
            : "AND (accepted_at,site_id,sensor_id,event_id) > (@cursor_at,@cursor_site,@cursor_sensor,@cursor_event)";
        await using var command = new NpgsqlCommand($"""
            SELECT accepted_at,site_id,sensor_id,event_id FROM monitoring.ingestion_inbox
            WHERE processed_at IS NULL
            AND (accepted_at,site_id,sensor_id,event_id) <= (@upper_at,@upper_site,@upper_sensor,@upper_event)
            {afterCursor}
            ORDER BY accepted_at,site_id,sensor_id,event_id LIMIT 100
            """, Connection);
        AddCursor(command, "upper", upper);
        if (cursor is not null)
        {
            AddCursor(command, "cursor", cursor);
        }
        var keys = new List<EventKey>(100);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(ReadKey(reader));
        }
        return keys;
    }

    private static EventKey ReadKey(NpgsqlDataReader reader) =>
        new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));

    private static void AddCursor(NpgsqlCommand command, string prefix, EventKey key)
    {
        command.Parameters.AddWithValue(prefix + "_at", key.AcceptedAt);
        command.Parameters.AddWithValue(prefix + "_site", key.SiteId);
        command.Parameters.AddWithValue(prefix + "_sensor", key.SensorId);
        command.Parameters.AddWithValue(prefix + "_event", key.EventId);
    }

    private async Task ProjectAsync(EventKey key, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var postgresTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        string occurredAt;
        string json;
        await using (var select = Command("""
            SELECT occurred_at_text, data::text FROM monitoring.ingestion_inbox
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event AND processed_at IS NULL
            FOR UPDATE SKIP LOCKED
            """, key, postgresTransaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }
            occurredAt = reader.GetString(0);
            json = reader.GetString(1);
        }

        using var document = JsonDocument.Parse(json);
        if (!SyntheticSessionContract.TryParse(document.RootElement, out _))
        {
            return;
        }

        await using (var insert = Command("""
            INSERT INTO monitoring.session_projection(site_id,sensor_id,event_id,occurred_at_text,data)
            VALUES (@site,@sensor,@event,@occurred,CAST(@data AS jsonb)) ON CONFLICT DO NOTHING
            """, key, postgresTransaction))
        {
            insert.Parameters.AddWithValue("occurred", occurredAt);
            insert.Parameters.AddWithValue("data", json);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var compare = Command("""
            SELECT occurred_at_text=@occurred AND data=CAST(@data AS jsonb) FROM monitoring.session_projection
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
            """, key, postgresTransaction))
        {
            compare.Parameters.AddWithValue("occurred", occurredAt);
            compare.Parameters.AddWithValue("data", json);
            if (await compare.ExecuteScalarAsync(cancellationToken) is not true)
            {
                throw new InvalidOperationException("Session projection conflicts with its accepted event.");
            }
        }
        await using (var mark = Command("""
            UPDATE monitoring.ingestion_inbox SET processed_at=clock_timestamp()
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
            """, key, postgresTransaction))
        {
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private NpgsqlCommand Command(string sql, EventKey key, NpgsqlTransaction? transaction = null)
    {
        var command = new NpgsqlCommand(sql, Connection, transaction);
        command.Parameters.AddWithValue("site", key.SiteId);
        command.Parameters.AddWithValue("sensor", key.SensorId);
        command.Parameters.AddWithValue("event", key.EventId);
        return command;
    }

    private sealed record EventKey(DateTimeOffset AcceptedAt, string SiteId, string SensorId, string EventId);
}

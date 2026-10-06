using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Domain.Inventory;
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
                WHERE processed_at IS NULL AND quarantined_at IS NULL ORDER BY accepted_at DESC,site_id DESC,sensor_id DESC,event_id DESC LIMIT 1
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
            WHERE processed_at IS NULL AND quarantined_at IS NULL
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

    private enum Outcome { Projected, NotPending, InvalidContract }

    private async Task ProjectAsync(EventKey key, CancellationToken cancellationToken)
    {
        string? permanentCause;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var postgresTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            try
            {
                switch (await TryProjectAsync(key, postgresTransaction, cancellationToken))
                {
                    case Outcome.NotPending:
                        return;
                    case Outcome.InvalidContract:
                        await QuarantineAsync(key, "contract-invalid", postgresTransaction, cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return;
                    default:
                        await transaction.CommitAsync(cancellationToken);
                        return;
                }
            }
            catch (PermanentProjectionException exception)
            {
                // Nothing the attempt wrote survives; the event is quarantined in a transaction of its own.
                permanentCause = exception.Cause;
                await transaction.RollbackAsync(cancellationToken);
            }
        }
        await using var quarantine = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await QuarantineAsync(key, permanentCause, (NpgsqlTransaction)quarantine.GetDbTransaction(), cancellationToken);
        await quarantine.CommitAsync(cancellationToken);
    }

    private async Task<Outcome> TryProjectAsync(EventKey key, NpgsqlTransaction postgresTransaction, CancellationToken cancellationToken)
    {
        string occurredAt;
        string json;
        await using (var select = Command("""
            SELECT occurred_at_text, data::text FROM monitoring.ingestion_inbox
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event AND processed_at IS NULL AND quarantined_at IS NULL
            FOR UPDATE SKIP LOCKED
            """, key, postgresTransaction))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Outcome.NotPending;
            }
            occurredAt = reader.GetString(0);
            json = reader.GetString(1);
        }

        using var document = JsonDocument.Parse(json);
        var outcome = IsDeviceObservation(document.RootElement)
            ? await ProjectDeviceObservationAsync(key, document.RootElement, postgresTransaction, cancellationToken)
            : await ProjectSessionAsync(key, occurredAt, json, document.RootElement, postgresTransaction, cancellationToken);
        if (outcome != Outcome.Projected)
        {
            return outcome;
        }
        await using (var mark = Command("""
            UPDATE monitoring.ingestion_inbox SET processed_at=clock_timestamp()
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
            """, key, postgresTransaction))
        {
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        return Outcome.Projected;
    }

    private static bool IsDeviceObservation(JsonElement data) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == DeviceObservationContract.Kind;

    private async Task<Outcome> ProjectSessionAsync(EventKey key, string occurredAt, string json, JsonElement data, NpgsqlTransaction postgresTransaction,
        CancellationToken cancellationToken)
    {
        if (!CanonicalSession.TryParse(data, out var session))
        {
            return Outcome.InvalidContract;
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
                throw new PermanentProjectionException("projection-conflict");
            }
        }
        await OutboxStore.WriteSessionAsync(Connection, postgresTransaction,
            new SessionIdentity(key.SiteId, key.SensorId, key.EventId), session!, key.AcceptedAt, cancellationToken);
        return Outcome.Projected;
    }

    // Observations never create sessions or outbox records: they add facts and, when a MAC is present, a candidate with its
    // temporary IP association. Every statement is idempotent, so a replay of the same event changes nothing.
    private async Task<Outcome> ProjectDeviceObservationAsync(EventKey key, JsonElement data, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (!DeviceObservationContract.TryParse(data, out var observation))
        {
            return Outcome.InvalidContract;
        }
        await using (var insert = Command("""
            INSERT INTO monitoring.device_observation(site_id,sensor_id,event_id,observed_at,ip,mac,vlan_id)
            VALUES (@site,@sensor,@event,@observed,@ip,CAST(@mac AS macaddr),@vlan) ON CONFLICT DO NOTHING
            """, key, transaction))
        {
            AddObservation(insert, observation!);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        if (observation!.Mac is null)
        {
            return Outcome.Projected;
        }
        Guid candidate;
        await using (var upsert = Command("""
            INSERT INTO monitoring.device_candidate(site_id,sensor_id,mac,vlan_id,first_seen,last_seen)
            VALUES (@site,@sensor,CAST(@mac AS macaddr),@vlan,@observed,@observed)
            ON CONFLICT (site_id,sensor_id,mac,(COALESCE(vlan_id,-1))) DO UPDATE
              SET first_seen=LEAST(monitoring.device_candidate.first_seen,EXCLUDED.first_seen),
                  last_seen=GREATEST(monitoring.device_candidate.last_seen,EXCLUDED.last_seen)
            RETURNING candidate_id
            """, key, transaction))
        {
            AddObservation(upsert, observation);
            candidate = (Guid)(await upsert.ExecuteScalarAsync(cancellationToken))!;
        }
        await using (var association = Command("""
            INSERT INTO monitoring.device_ip_association(candidate_id,ip,first_seen,last_seen) VALUES (@candidate,@ip,@observed,@observed)
            ON CONFLICT (candidate_id,ip) DO UPDATE
              SET first_seen=LEAST(monitoring.device_ip_association.first_seen,EXCLUDED.first_seen),
                  last_seen=GREATEST(monitoring.device_ip_association.last_seen,EXCLUDED.last_seen)
            """, key, transaction))
        {
            association.Parameters.AddWithValue("candidate", candidate);
            association.Parameters.AddWithValue("ip", NpgsqlTypes.NpgsqlDbType.Inet, observation.Ip);
            association.Parameters.AddWithValue("observed", observation.ObservedAt);
            await association.ExecuteNonQueryAsync(cancellationToken);
        }
        return Outcome.Projected;
    }

    private static void AddObservation(NpgsqlCommand command, DeviceObservation observation)
    {
        command.Parameters.AddWithValue("observed", observation.ObservedAt);
        command.Parameters.AddWithValue("ip", NpgsqlTypes.NpgsqlDbType.Inet, observation.Ip);
        command.Parameters.AddWithValue("mac", NpgsqlTypes.NpgsqlDbType.Text, (object?)observation.Mac ?? DBNull.Value);
        command.Parameters.AddWithValue("vlan", NpgsqlTypes.NpgsqlDbType.Integer, (object?)observation.VlanId ?? DBNull.Value);
    }

    // Moves a pending event to quarantine and records that single state change; a no-op when another worker already handled it.
    private async Task QuarantineAsync(EventKey key, string cause, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var pending = Command("""
            SELECT 1 FROM monitoring.ingestion_inbox
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event AND processed_at IS NULL AND quarantined_at IS NULL
            FOR UPDATE SKIP LOCKED
            """, key, transaction))
        {
            if (await pending.ExecuteScalarAsync(cancellationToken) is null)
            {
                return;
            }
        }
        int inserted;
        await using (var insert = Command("""
            INSERT INTO monitoring.ingestion_quarantine(site_id,sensor_id,event_id,cause) VALUES (@site,@sensor,@event,@cause)
            ON CONFLICT DO NOTHING
            """, key, transaction))
        {
            insert.Parameters.AddWithValue("cause", cause);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        if (inserted == 1)
        {
            await using var audit = Command("""
                INSERT INTO monitoring.ingestion_quarantine_audit(site_id,sensor_id,event_id,action,cause,actor)
                VALUES (@site,@sensor,@event,'quarantined',@cause,'system:projector')
                """, key, transaction);
            audit.Parameters.AddWithValue("cause", cause);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var mark = Command("""
            UPDATE monitoring.ingestion_inbox SET quarantined_at=clock_timestamp()
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
            """, key, transaction);
        await mark.ExecuteNonQueryAsync(cancellationToken);
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

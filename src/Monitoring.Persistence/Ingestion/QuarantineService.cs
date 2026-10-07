using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Monitoring.Persistence.Ingestion;

public enum ResolutionResult { Resolved, AlreadyResolved, NotFound, InvalidReplacement }

// Identity, cause and state only: the quarantined payload is never read, copied or returned.
public sealed record QuarantineEntry(string SiteId, string SensorId, string EventId, string Cause, string State, int Attempts,
    DateTimeOffset QuarantinedAt, DateTimeOffset? ResolvedAt, string? ResolvedBy, string? ReplacedByEventId);

public sealed record QuarantineSummary(long Accepted, long Processed, long Quarantined, long Pending,
    IReadOnlyDictionary<string, long> UnresolvedByCause, long Replaced, long Discarded, double? OldestUnresolvedAgeSeconds);

// Operational access to the quarantine. Every resolution carries an actor and a reason and is recorded in the audit trail in the
// same transaction; the original accepted event is never edited, and a replacement is a separate, already processed event.
public sealed class QuarantineService(MonitoringDbContext dbContext)
{
    private static readonly string[] States = ["unresolved", "replaced", "discarded"];
    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    public async Task<IReadOnlyList<QuarantineEntry>> ListAsync(string? state, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        if (state is not null && !States.Contains(state, StringComparer.Ordinal)) throw new ArgumentException("Unknown quarantine state.", nameof(state));
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT site_id,sensor_id,event_id,cause,state,attempts,quarantined_at,resolved_at,resolved_by,replaced_by_event_id
                FROM monitoring.ingestion_quarantine WHERE (@state IS NULL OR state=@state)
                ORDER BY quarantined_at,site_id,sensor_id,event_id LIMIT @limit
                """, Connection);
            command.Parameters.AddWithValue("state", NpgsqlTypes.NpgsqlDbType.Text, (object?)state ?? DBNull.Value);
            command.Parameters.AddWithValue("limit", limit);
            var list = new List<QuarantineEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(new QuarantineEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            return list;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public Task<ResolutionResult> DiscardAsync(string siteId, string sensorId, string eventId, string actor, string reason, CancellationToken cancellationToken) =>
        ResolveAsync(siteId, sensorId, eventId, "discarded", null, actor, reason, cancellationToken);

    public Task<ResolutionResult> ReplaceAsync(string siteId, string sensorId, string eventId, string replacementEventId, string actor, string reason,
        CancellationToken cancellationToken) =>
        ResolveAsync(siteId, sensorId, eventId, "replaced", replacementEventId, actor, reason, cancellationToken);

    public async Task<QuarantineSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            long accepted, processed, quarantined, replaced, discarded;
            double? oldest;
            await using (var totals = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM monitoring.ingestion_inbox),
                       (SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL),
                       (SELECT count(*) FROM monitoring.ingestion_inbox WHERE quarantined_at IS NOT NULL),
                       (SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='replaced'),
                       (SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='discarded'),
                       (SELECT EXTRACT(EPOCH FROM (clock_timestamp()-min(quarantined_at)))::float8 FROM monitoring.ingestion_quarantine WHERE state='unresolved')
                """, Connection))
            await using (var reader = await totals.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                (accepted, processed, quarantined, replaced, discarded) = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
                oldest = reader.IsDBNull(5) ? null : Math.Max(0, reader.GetDouble(5));
            }
            var byCause = new Dictionary<string, long>(StringComparer.Ordinal);
            await using var causes = new NpgsqlCommand("SELECT cause,count(*) FROM monitoring.ingestion_quarantine WHERE state='unresolved' GROUP BY cause", Connection);
            await using var causeReader = await causes.ExecuteReaderAsync(cancellationToken);
            while (await causeReader.ReadAsync(cancellationToken)) byCause[causeReader.GetString(0)] = causeReader.GetInt64(1);
            return new QuarantineSummary(accepted, processed, quarantined, accepted - processed - quarantined, byCause, replaced, discarded, oldest);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task<ResolutionResult> ResolveAsync(string siteId, string sensorId, string eventId, string newState, string? replacementEventId,
        string actor, string reason, CancellationToken cancellationToken)
    {
        Require(actor, nameof(actor), 128);
        Require(reason, nameof(reason), 256);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            string? cause, state;
            await using (var select = new NpgsqlCommand("""
                SELECT cause,state FROM monitoring.ingestion_quarantine WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event FOR UPDATE
                """, Connection, transaction))
            {
                select.Parameters.AddWithValue("site", siteId);
                select.Parameters.AddWithValue("sensor", sensorId);
                select.Parameters.AddWithValue("event", eventId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return ResolutionResult.NotFound;
                (cause, state) = (reader.GetString(0), reader.GetString(1));
            }
            if (state != "unresolved") return ResolutionResult.AlreadyResolved;
            if (replacementEventId is not null && !await IsProcessedReplacementAsync(siteId, sensorId, eventId, replacementEventId, transaction, cancellationToken))
                return ResolutionResult.InvalidReplacement;

            await using (var update = new NpgsqlCommand("""
                UPDATE monitoring.ingestion_quarantine SET state=@state,resolved_at=clock_timestamp(),resolved_by=@actor,
                  resolution_reason=@reason,replaced_by_event_id=@replacement
                WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event
                """, Connection, transaction))
            {
                update.Parameters.AddWithValue("state", newState);
                update.Parameters.AddWithValue("actor", actor);
                update.Parameters.AddWithValue("reason", reason);
                update.Parameters.AddWithValue("replacement", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)replacementEventId ?? DBNull.Value);
                update.Parameters.AddWithValue("site", siteId);
                update.Parameters.AddWithValue("sensor", sensorId);
                update.Parameters.AddWithValue("event", eventId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var audit = new NpgsqlCommand("""
                INSERT INTO monitoring.ingestion_quarantine_audit(site_id,sensor_id,event_id,action,cause,actor,reason,replaced_by_event_id)
                VALUES (@site,@sensor,@event,@action,@cause,@actor,@reason,@replacement)
                """, Connection, transaction))
            {
                audit.Parameters.AddWithValue("site", siteId);
                audit.Parameters.AddWithValue("sensor", sensorId);
                audit.Parameters.AddWithValue("event", eventId);
                audit.Parameters.AddWithValue("action", newState);
                audit.Parameters.AddWithValue("cause", cause);
                audit.Parameters.AddWithValue("actor", actor);
                audit.Parameters.AddWithValue("reason", reason);
                audit.Parameters.AddWithValue("replacement", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)replacementEventId ?? DBNull.Value);
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return ResolutionResult.Resolved;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    // The correction is a different event of the same origin that was accepted and has been projected successfully.
    private async Task<bool> IsProcessedReplacementAsync(string siteId, string sensorId, string eventId, string replacementEventId,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (string.Equals(eventId, replacementEventId, StringComparison.Ordinal)) return false;
        await using var command = new NpgsqlCommand("""
            SELECT 1 FROM monitoring.ingestion_inbox WHERE site_id=@site AND sensor_id=@sensor AND event_id=@replacement AND processed_at IS NOT NULL
            """, Connection, transaction);
        command.Parameters.AddWithValue("site", siteId);
        command.Parameters.AddWithValue("sensor", sensorId);
        command.Parameters.AddWithValue("replacement", replacementEventId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void Require(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength) throw new ArgumentException($"A non-empty {name} of at most {maximumLength} characters is required.", name);
    }
}

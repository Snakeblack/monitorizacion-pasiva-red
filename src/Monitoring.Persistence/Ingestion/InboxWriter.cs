using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Monitoring.Domain.Ingestion;
using Npgsql;

namespace Monitoring.Persistence.Ingestion;

public enum InboxWriteResult
{
    Accepted,
    Conflict,
    RateLimited
}

public sealed class InboxWriter(MonitoringDbContext dbContext)
{
    public async Task<InboxWriteResult> WriteAsync(
        string siteId,
        string sensorId,
        IngestionBatch batch,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await EnsureOriginLockedAsync(siteId, sensorId, transaction, cancellationToken);
        var acceptedAt = await ReadDatabaseTimeAsync(transaction, cancellationToken);
        var uniqueEvents = new Dictionary<string, IngestionEvent>(StringComparer.Ordinal);

        foreach (var ingestionEvent in batch.Events)
        {
            if (uniqueEvents.TryGetValue(ingestionEvent.EventId, out var duplicate))
            {
                if (!SameTimestamp(duplicate, ingestionEvent)
                    || !await JsonbValuesEqualAsync(duplicate.Data.GetRawText(), ingestionEvent.Data.GetRawText(), transaction, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return InboxWriteResult.Conflict;
                }

                continue;
            }

            uniqueEvents.Add(ingestionEvent.EventId, ingestionEvent);
        }

        var newEvents = new List<IngestionInboxEntity>(uniqueEvents.Count);
        foreach (var ingestionEvent in uniqueEvents.Values)
        {
            var existing = await FindExistingAsync(siteId, sensorId, ingestionEvent, transaction, cancellationToken);
            if (existing.Exists)
            {
                if (!existing.ContentMatches)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return InboxWriteResult.Conflict;
                }

                continue;
            }

            newEvents.Add(new IngestionInboxEntity
            {
                SiteId = siteId,
                SensorId = sensorId,
                EventId = ingestionEvent.EventId,
                BatchId = batch.BatchId,
                SchemaVersion = (short)batch.SchemaVersion,
                OccurredAt = ParseTimestamp(ingestionEvent),
                OccurredAtText = ingestionEvent.OccurredAt,
                Data = ingestionEvent.Data.GetRawText(),
                AcceptedAt = acceptedAt
            });
        }

        var acceptedInWindow = await CountAcceptedInWindowAsync(siteId, sensorId, acceptedAt, transaction, cancellationToken);
        if (acceptedInWindow + newEvents.Count > 500)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InboxWriteResult.RateLimited;
        }

        dbContext.IngestionInbox.AddRange(newEvents);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InboxWriteResult.Accepted;
    }

    private async Task EnsureOriginLockedAsync(
        string siteId,
        string sensorId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var insert = Command(
            "INSERT INTO monitoring.ingestion_origin (site_id, sensor_id) VALUES (@site_id, @sensor_id) ON CONFLICT (site_id, sensor_id) DO NOTHING",
            transaction))
        {
            insert.Parameters.AddWithValue("site_id", siteId);
            insert.Parameters.AddWithValue("sensor_id", sensorId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var lockOrigin = Command(
            "SELECT site_id FROM monitoring.ingestion_origin WHERE site_id = @site_id AND sensor_id = @sensor_id FOR UPDATE",
            transaction);
        lockOrigin.Parameters.AddWithValue("site_id", siteId);
        lockOrigin.Parameters.AddWithValue("sensor_id", sensorId);
        await lockOrigin.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<DateTimeOffset> ReadDatabaseTimeAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("SELECT clock_timestamp()", transaction);
        var databaseTime = (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new DateTimeOffset(DateTime.SpecifyKind(databaseTime, DateTimeKind.Utc));
    }

    private async Task<(bool Exists, bool ContentMatches)> FindExistingAsync(
        string siteId,
        string sensorId,
        IngestionEvent ingestionEvent,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            "SELECT occurred_at_text, data = CAST(@data AS jsonb) FROM monitoring.ingestion_inbox WHERE site_id = @site_id AND sensor_id = @sensor_id AND event_id = @event_id",
            transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("event_id", ingestionEvent.EventId);
        command.Parameters.AddWithValue("data", ingestionEvent.Data.GetRawText());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (false, false);
        }

        var sameTimestamp = reader.GetString(0) == ingestionEvent.OccurredAt;
        return (true, sameTimestamp && reader.GetBoolean(1));
    }

    private async Task<bool> JsonbValuesEqualAsync(
        string left,
        string right,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("SELECT CAST(@left AS jsonb) = CAST(@right AS jsonb)", transaction);
        command.Parameters.AddWithValue("left", left);
        command.Parameters.AddWithValue("right", right);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<long> CountAcceptedInWindowAsync(
        string siteId,
        string sensorId,
        DateTimeOffset acceptedAt,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            "SELECT count(*) FROM monitoring.ingestion_inbox WHERE site_id = @site_id AND sensor_id = @sensor_id AND accepted_at > @window_start",
            transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("window_start", acceptedAt.AddSeconds(-60));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private NpgsqlCommand Command(string sql, IDbContextTransaction transaction) =>
        new(sql, (NpgsqlConnection)dbContext.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());

    private static bool SameTimestamp(IngestionEvent left, IngestionEvent right) =>
        string.Equals(left.OccurredAt, right.OccurredAt, StringComparison.Ordinal);

    private static DateTimeOffset ParseTimestamp(IngestionEvent ingestionEvent) =>
        DateTimeOffset.Parse(ingestionEvent.OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

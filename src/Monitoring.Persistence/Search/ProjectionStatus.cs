using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions.Search;
using Npgsql;

namespace Monitoring.Persistence.Search;

public sealed class ProjectionOptions
{
    // Outbox rows younger than this are still legitimately in flight and are not examined yet.
    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(30);
    public int BlockSize { get; set; } = 100;
    // A verification older than this no longer proves anything about the present.
    public TimeSpan MaxCheckAge { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed record ProjectionCheckResult(Guid CheckId, long Examined, long Missing, long Stale, bool Complete, long? MaxLagSeconds);

// Verifies the projection against the authority: the latest outbox record of every identity older than the grace window must
// be visible in the index (after a refresh) at that revision or a newer one. A connector in RUNNING proves nothing by itself.
public sealed class ProjectionReconciler(MonitoringDbContext dbContext, IProjectionIndex index, ProjectionOptions options)
{
    public async Task<ProjectionCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var checkId = Guid.NewGuid();
        long examined = 0, missing = 0, stale = 0;
        long? maxLag = null;
        var complete = false;
        try
        {
            DateTimeOffset now, upperBound;
            await using (var start = new NpgsqlCommand("""
                INSERT INTO monitoring.search_projection_check(check_id,started_at,upper_bound)
                VALUES (@id,clock_timestamp(),clock_timestamp()-@grace) RETURNING started_at,upper_bound
                """, connection))
            {
                start.Parameters.AddWithValue("id", checkId);
                start.Parameters.AddWithValue("grace", options.Grace);
                await using var reader = await start.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                (now, upperBound) = (reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1));
            }
            await index.RefreshAsync(cancellationToken);
            string? after = null;
            while (true)
            {
                var expected = await ReadBlockAsync(connection, upperBound, now, after, cancellationToken);
                if (expected.Count == 0) break;
                var visible = await index.GetAsync(expected.Select(item => item.Id).ToArray(), cancellationToken);
                foreach (var item in expected)
                {
                    examined++;
                    if (!visible.TryGetValue(item.Id, out var indexed))
                    {
                        missing++;
                        maxLag = Math.Max(maxLag ?? 0, Math.Max(1, item.AgeSeconds));
                    }
                    else if (indexed.Revision < item.Revision || (indexed.Revision == item.Revision && indexed.Operation != item.Operation))
                    {
                        stale++;
                        maxLag = Math.Max(maxLag ?? 0, Math.Max(1, item.AgeSeconds));
                    }
                }
                after = expected[^1].AggregateId;
                if (expected.Count < options.BlockSize) break;
            }
            complete = true;
        }
        catch (Exception exception) when (exception is SessionSearchException or HttpRequestException)
        {
            // The sweep did not finish: record it as incomplete so it can never count as a verification.
        }
        finally
        {
            await using var finish = new NpgsqlCommand("""
                UPDATE monitoring.search_projection_check SET finished_at=clock_timestamp(),examined=@examined,missing=@missing,
                  stale=@stale,max_lag_seconds=@lag,complete=@complete WHERE check_id=@id
                """, connection);
            finish.Parameters.AddWithValue("id", checkId);
            finish.Parameters.AddWithValue("examined", examined);
            finish.Parameters.AddWithValue("missing", missing);
            finish.Parameters.AddWithValue("stale", stale);
            finish.Parameters.AddWithValue("lag", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)maxLag ?? DBNull.Value);
            finish.Parameters.AddWithValue("complete", complete);
            await finish.ExecuteNonQueryAsync(CancellationToken.None);
            await dbContext.Database.CloseConnectionAsync();
        }
        return new ProjectionCheckResult(checkId, examined, missing, stale, complete, maxLag);
    }

    private sealed record Expected(string AggregateId, Guid Id, long Revision, string Operation, long AgeSeconds);

    private async Task<List<Expected>> ReadBlockAsync(NpgsqlConnection connection, DateTimeOffset upperBound, DateTimeOffset now,
        string? after, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT ON (o.aggregateid) o.aggregateid,(o.payload->>'searchDocumentId')::uuid,o.revision,o.payload->>'operation',
              (EXTRACT(EPOCH FROM (@now - COALESCE((o.payload->>'acceptedAt')::timestamptz,o.created_at))))::bigint
            FROM monitoring.projection_outbox o
            WHERE o.target_topic=@topic AND o.created_at<=@upper AND (@first OR o.aggregateid>@after)
            ORDER BY o.aggregateid,o.revision DESC LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("topic", Sessions.OutboxStore.SessionTopic);
        command.Parameters.AddWithValue("upper", upperBound);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("first", after is null);
        command.Parameters.AddWithValue("after", after ?? "");
        command.Parameters.AddWithValue("limit", options.BlockSize);
        var list = new List<Expected>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new Expected(reader.GetString(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3), reader.GetInt64(4)));
        return list;
    }
}

// Freshness derived only from a recorded, complete, recent verification; anything else is recovering with unknown lag.
public sealed class PostgresProjectionStatus(MonitoringDbContext dbContext, ProjectionOptions options) : IProjectionStatus
{
    public async Task<SearchFreshness> CurrentAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // The database clock decides both the age of the verification and the measurement time.
            long missing, stale, age;
            long? lag;
            DateTimeOffset measuredAt;
            await using (var command = new NpgsqlCommand("""
                SELECT missing,stale,max_lag_seconds,(EXTRACT(EPOCH FROM (clock_timestamp()-finished_at)))::bigint,clock_timestamp()
                FROM monitoring.search_projection_check WHERE complete ORDER BY finished_at DESC LIMIT 1
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await reader.CloseAsync();
                    return new SearchFreshness(FreshnessState.Recovering, await NowAsync(connection, cancellationToken), null);
                }
                (missing, stale, lag, age, measuredAt) = (reader.GetInt64(0), reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetInt64(3), reader.GetFieldValue<DateTimeOffset>(4));
            }
            if (age > options.MaxCheckAge.TotalSeconds) return new SearchFreshness(FreshnessState.Recovering, measuredAt, null);
            // The lag of a verified gap keeps growing until the next sweep proves it closed.
            return missing + stale > 0
                ? new SearchFreshness(FreshnessState.Lagging, measuredAt, Math.Max(1, (lag ?? 1) + age))
                : new SearchFreshness(FreshnessState.Current, measuredAt, null);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private static async Task<DateTimeOffset> NowAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetFieldValue<DateTimeOffset>(0);
    }
}

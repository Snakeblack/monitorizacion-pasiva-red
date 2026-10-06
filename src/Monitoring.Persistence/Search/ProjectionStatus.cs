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

// Outcome of verifying a candidate generation; it is never recorded as a freshness check of the serving index.
public sealed record GenerationVerification(long Examined, long Missing, long Stale, long IndexCount, bool Complete)
{
    public bool Converged => Complete && Missing == 0 && Stale == 0 && IndexCount == Examined;
}

// Verifies a search index against the authority: the latest outbox record of every identity older than the grace window must
// be visible in the index (after a refresh) at that revision or a newer one. A connector in RUNNING proves nothing by itself.
public sealed class ProjectionReconciler(MonitoringDbContext dbContext, IProjectionIndex index, ProjectionOptions options)
{
    private sealed record Sweep(long Examined, long Missing, long Stale, long? MaxLagSeconds);

    // Verifies the serving index (the read alias) against the active topic and records the outcome.
    public async Task<ProjectionCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var checkId = Guid.NewGuid();
        Sweep? sweep = null;
        var partial = new Sweep(0, 0, 0, null);
        try
        {
            await using (var start = new NpgsqlCommand("""
                INSERT INTO monitoring.search_projection_check(check_id,started_at,upper_bound)
                VALUES (@id,clock_timestamp(),clock_timestamp()-@grace)
                """, connection))
            {
                start.Parameters.AddWithValue("id", checkId);
                start.Parameters.AddWithValue("grace", options.Grace);
                await start.ExecuteNonQueryAsync(cancellationToken);
            }
            sweep = await SweepAsync(connection, null, null, options.Grace, cancellationToken);
        }
        catch (Exception exception) when (exception is SessionSearchException or HttpRequestException)
        {
            // The sweep did not finish: record it as incomplete so it can never count as a verification.
        }
        finally
        {
            var result = sweep ?? partial;
            await using var finish = new NpgsqlCommand("""
                UPDATE monitoring.search_projection_check SET finished_at=clock_timestamp(),examined=@examined,missing=@missing,
                  stale=@stale,max_lag_seconds=@lag,complete=@complete WHERE check_id=@id
                """, connection);
            finish.Parameters.AddWithValue("id", checkId);
            finish.Parameters.AddWithValue("examined", result.Examined);
            finish.Parameters.AddWithValue("missing", result.Missing);
            finish.Parameters.AddWithValue("stale", result.Stale);
            finish.Parameters.AddWithValue("lag", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)result.MaxLagSeconds ?? DBNull.Value);
            finish.Parameters.AddWithValue("complete", sweep is not null);
            await finish.ExecuteNonQueryAsync(CancellationToken.None);
            await dbContext.Database.CloseConnectionAsync();
        }
        var final = sweep ?? partial;
        return new ProjectionCheckResult(checkId, final.Examined, final.Missing, final.Stale, sweep is not null, final.MaxLagSeconds);
    }

    // Verifies a candidate generation (its own topic and index) with no grace: everything copied must be visible.
    public async Task<GenerationVerification> VerifyGenerationAsync(string topic, string indexName, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var sweep = await SweepAsync(connection, topic, indexName, TimeSpan.Zero, cancellationToken);
            var count = await index.CountAsync(indexName, cancellationToken);
            return new GenerationVerification(sweep.Examined, sweep.Missing, sweep.Stale, count, true);
        }
        catch (Exception exception) when (exception is SessionSearchException or HttpRequestException)
        {
            return new GenerationVerification(0, 0, 0, 0, false);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task<Sweep> SweepAsync(NpgsqlConnection connection, string? topic, string? indexName, TimeSpan grace, CancellationToken cancellationToken)
    {
        DateTimeOffset now, upperBound;
        await using (var clock = new NpgsqlCommand("SELECT clock_timestamp(),clock_timestamp()-@grace", connection))
        {
            clock.Parameters.AddWithValue("grace", grace);
            await using var reader = await clock.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            (now, upperBound) = (reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1));
        }
        await index.RefreshAsync(indexName, cancellationToken);
        long examined = 0, missing = 0, stale = 0;
        long? maxLag = null;
        string? after = null;
        while (true)
        {
            var expected = await ReadBlockAsync(connection, topic, upperBound, now, after, cancellationToken);
            if (expected.Count == 0) break;
            var visible = await index.GetAsync(indexName, expected.Select(item => item.Id).ToArray(), cancellationToken);
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
        return new Sweep(examined, missing, stale, maxLag);
    }

    private sealed record Expected(string AggregateId, Guid Id, long Revision, string Operation, long AgeSeconds);

    private async Task<List<Expected>> ReadBlockAsync(NpgsqlConnection connection, string? topic, DateTimeOffset upperBound, DateTimeOffset now,
        string? after, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT ON (o.aggregateid) o.aggregateid,(o.payload->>'searchDocumentId')::uuid,o.revision,o.payload->>'operation',
              (EXTRACT(EPOCH FROM (@now - COALESCE((o.payload->>'acceptedAt')::timestamptz,o.created_at))))::bigint
            FROM monitoring.projection_outbox o
            WHERE o.target_topic=COALESCE(@topic,(SELECT target_topic FROM monitoring.search_generation WHERE state='active'))
              AND o.created_at<=@upper AND (@first OR o.aggregateid>@after)
            ORDER BY o.aggregateid,o.revision DESC LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("topic", NpgsqlTypes.NpgsqlDbType.Text, (object?)topic ?? DBNull.Value);
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

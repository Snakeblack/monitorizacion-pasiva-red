using System.Net;
using Monitoring.Domain.Sessions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence.Search;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class ProjectionReconciliationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record Rig(string Connection, FakeElasticsearch Elastic, List<(Guid Id, string Event)> Identities);

    private static async Task<List<(Guid, string)>> IdentitiesAsync(string connection)
    {
        await using var npgsql = new NpgsqlConnection(connection);
        await npgsql.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT search_document_id,event_id FROM monitoring.session_identity ORDER BY event_id", npgsql);
        var list = new List<(Guid, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add((reader.GetGuid(0), reader.GetString(1)));
        return list;
    }

    private async Task<Rig> ProjectedAsync(int count, bool indexed = true)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var n in Enumerable.Range(1, count)) await SessionTestDatabase.AcceptAsync(connection, $"event-{n:D2}");
        await SessionTestDatabase.ProjectAsync(connection);
        var identities = await IdentitiesAsync(connection);
        var elastic = new FakeElasticsearch();
        if (indexed) foreach (var (id, eventId) in identities) elastic.Add(IndexedUpsert(id, eventId, 1));
        return new Rig(connection, elastic, identities);
    }

    private static FakeElasticsearch.Doc IndexedUpsert(Guid id, string eventId, long revision, string operation = "upsert")
    {
        var doc = FakeElasticsearch.MakeDoc(id, "site", "sensor", eventId, DateTimeOffset.UtcNow, operation: operation);
        doc.Source["revision"] = revision;
        return doc;
    }

    private static ProjectionReconciler Reconciler(Rig rig, MonitoringDbContextHolder db, ProjectionOptions? options = null) =>
        new(db.Context, new ElasticsearchProjectionIndex(new HttpClient(rig.Elastic) { BaseAddress = new Uri("http://elasticsearch.test:9200") }, new ElasticsearchOptions()),
            options ?? new ProjectionOptions { Grace = TimeSpan.Zero, BlockSize = 100 });

    private sealed class MonitoringDbContextHolder(string connection) : IAsyncDisposable
    {
        public Monitoring.Persistence.MonitoringDbContext Context { get; } = SessionTestDatabase.Context(connection);
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static async Task<SearchFreshness> StatusAsync(string connection, ProjectionOptions? options = null)
    {
        await using var holder = new MonitoringDbContextHolder(connection);
        return await new PostgresProjectionStatus(holder.Context, options ?? new ProjectionOptions()).CurrentAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFullyVisibleProjectionVerifiesCleanlyAndBecomesCurrentAfterARefresh()
    {
        var rig = await ProjectedAsync(5);
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((5L, 0L, 0L, true), (result.Examined, result.Missing, result.Stale, result.Complete));
        Assert.Equal("POST /sessions-read/_refresh", rig.Elastic.Calls[0]);
        var status = await StatusAsync(rig.Connection);
        Assert.Equal(FreshnessState.Current, status.State);
    }

    [Fact]
    public async Task AMissingDocumentMakesTheProjectionLaggingWithAMeasuredLagNeverZero()
    {
        var rig = await ProjectedAsync(3);
        rig.Elastic.Remove(rig.Identities[1].Id);
        await SessionTestDatabase.ExecuteAsync(rig.Connection, "UPDATE monitoring.projection_outbox SET payload=jsonb_set(payload,'{acceptedAt}',to_jsonb(to_char((clock_timestamp()-interval '90 seconds') AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.MS\"Z\"')))");
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((3L, 1L, 0L, true), (result.Examined, result.Missing, result.Stale, result.Complete));
        var status = await StatusAsync(rig.Connection);
        Assert.Equal(FreshnessState.Lagging, status.State);
        Assert.True(status.LagSeconds >= 90);
    }

    [Fact]
    public async Task AnUnappliedSuppressionBarrierIsAGapEvenThoughTheOldDocumentExists()
    {
        var rig = await ProjectedAsync(2);
        await using (var db = SessionTestDatabase.Context(rig.Connection))
            Assert.Equal(SuppressionResult.Suppressed, await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "event-01"), CancellationToken.None));
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var stale = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((2L, 0L, 1L), (stale.Examined, stale.Missing, stale.Stale));
        Assert.Equal(FreshnessState.Lagging, (await StatusAsync(rig.Connection)).State);
        // Once the barrier reaches the index the projection converges.
        rig.Elastic.Replace(IndexedUpsert(rig.Identities[0].Id, "event-01", 2, "delete"));
        var converged = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((0L, 0L, true), (converged.Missing, converged.Stale, converged.Complete));
        Assert.Equal(FreshnessState.Current, (await StatusAsync(rig.Connection)).State);
    }

    [Fact]
    public async Task ANewerIndexedRevisionIsNeverAGap()
    {
        var rig = await ProjectedAsync(1);
        rig.Elastic.Replace(IndexedUpsert(rig.Identities[0].Id, "event-01", 9));
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((0L, 0L), (result.Missing, result.Stale));
    }

    [Fact]
    public async Task RowsYoungerThanTheGraceWindowAreNotExaminedYet()
    {
        var rig = await ProjectedAsync(3, indexed: false);
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder, new ProjectionOptions { Grace = TimeSpan.FromHours(1), BlockSize = 100 }).RunAsync(CancellationToken.None);
        Assert.Equal((0L, 0L, true), (result.Examined, result.Missing, result.Complete));
    }

    [Fact]
    public async Task TheSweepWalksEveryIdentityInKeysetBlocks()
    {
        var rig = await ProjectedAsync(7);
        rig.Elastic.Remove(rig.Identities[6].Id);
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder, new ProjectionOptions { Grace = TimeSpan.Zero, BlockSize = 3 }).RunAsync(CancellationToken.None);
        Assert.Equal((7L, 1L), (result.Examined, result.Missing));
        Assert.Equal([3, 3, 1], rig.Elastic.MgetSizes);
    }

    [Fact]
    public async Task OnlyTheCurrentRevisionOfAnIdentityIsExaminedNotItsHistory()
    {
        var rig = await ProjectedAsync(1);
        await using (var db = SessionTestDatabase.Context(rig.Connection))
            await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "event-01"), CancellationToken.None);
        rig.Elastic.Replace(IndexedUpsert(rig.Identities[0].Id, "event-01", 2, "delete"));
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        var result = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.Equal((1L, 0L, 0L), (result.Examined, result.Missing, result.Stale));
    }

    [Fact]
    public async Task AnInterruptedSweepIsRecordedIncompleteAndNeverCountsAsCurrent()
    {
        var rig = await ProjectedAsync(3);
        await using var holder = new MonitoringDbContextHolder(rig.Connection);
        Assert.True((await Reconciler(rig, holder).RunAsync(CancellationToken.None)).Complete);
        rig.Elastic.FailMget = true;
        var failed = await Reconciler(rig, holder).RunAsync(CancellationToken.None);
        Assert.False(failed.Complete);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(rig.Connection, "SELECT count(*) FROM monitoring.search_projection_check WHERE NOT complete"));
        // The earlier complete verification still stands until it ages out.
        Assert.Equal(FreshnessState.Current, (await StatusAsync(rig.Connection)).State);
        await SessionTestDatabase.ExecuteAsync(rig.Connection, "UPDATE monitoring.search_projection_check SET finished_at=finished_at-interval '1 hour'");
        var aged = await StatusAsync(rig.Connection);
        Assert.Equal((FreshnessState.Recovering, (long?)null), (aged.State, aged.LagSeconds));
    }

    [Fact]
    public async Task WithoutAnyVerificationTheStateIsRecoveringWithUnknownLag()
    {
        var rig = await ProjectedAsync(1);
        var status = await StatusAsync(rig.Connection);
        Assert.Equal((FreshnessState.Recovering, (long?)null), (status.State, status.LagSeconds));
    }
}

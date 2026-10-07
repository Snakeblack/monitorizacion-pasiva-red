using Monitoring.Domain.Sessions;
using Monitoring.Persistence.Search;
using Monitoring.Persistence.Sessions;

namespace Monitoring.Tests;

public sealed class SearchGenerationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<string> ProjectedAsync(params string[] events)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var eventId in events) await SessionTestDatabase.AcceptAsync(connection, eventId);
        await SessionTestDatabase.ProjectAsync(connection);
        return connection;
    }

    private static async Task<T> WithStoreAsync<T>(string connection, Func<SearchGenerationStore, Task<T>> action, GenerationOptions? options = null)
    {
        await using var db = SessionTestDatabase.Context(connection);
        return await action(new SearchGenerationStore(db, options ?? new GenerationOptions()));
    }

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task BeginAllocatesTheNextGenerationAndOnlyOneRebuildMayBeOpen()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var generation = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None));
        Assert.Equal((2, "sessions-v2-000002", "monitoring.sessions.v2.g2"), (generation.Generation, generation.IndexName, generation.Topic));
        var busy = await Assert.ThrowsAsync<SearchGenerationException>(() => WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None)));
        Assert.Equal("rebuild-in-progress", busy.Code);
        await WithStoreAsync(connection, async store => { await store.AbortAsync(2, CancellationToken.None); return 0; });
        var next = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None));
        Assert.Equal(3, next.Generation);
    }

    [Fact]
    public async Task TheFullPassCopiesOnlyTheLatestRecordOfEachIdentityPreservingKeyRevisionAndPayload()
    {
        var connection = await ProjectedAsync("a", "b", "c", "d", "e");
        await using (var db = SessionTestDatabase.Context(connection))
            await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "b"), CancellationToken.None);
        var options = new GenerationOptions { BlockSize = 2 };
        var generation = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None), options);
        Assert.Equal(5L, await WithStoreAsync(connection, store => store.CopyAllAsync(generation, CancellationToken.None), options));
        // b contributes its revision-2 barrier only; the superseded revision-1 upsert is not copied.
        Assert.Equal(5L, await Count(connection, $"SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='{generation.Topic}'"));
        Assert.Equal(1L, await Count(connection, $"""
            SELECT count(*) FROM monitoring.projection_outbox g JOIN monitoring.projection_outbox o ON o.id=g.source_outbox_id
            WHERE g.target_topic='{generation.Topic}' AND g.revision=2 AND g.payload->>'operation'='delete'
              AND g.aggregateid=o.aggregateid AND g.payload=o.payload AND o.target_topic='monitoring.sessions.v2'
            """));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.search_generation WHERE generation=2 AND copied=5"));
        // A repeated pass finds nothing left to copy.
        Assert.Equal(0L, await WithStoreAsync(connection, store => store.CopyAllAsync(generation, CancellationToken.None), options));
        Assert.Equal(6L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2'"));
    }

    [Fact]
    public async Task CatchUpCopiesConcurrentChangesExactlyOnce()
    {
        var connection = await ProjectedAsync("a", "b");
        var generation = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None));
        await WithStoreAsync(connection, store => store.CopyAllAsync(generation, CancellationToken.None));
        // Changes while the generation is building: a new session and a suppression of an already copied one.
        await SessionTestDatabase.AcceptAsync(connection, "late");
        await SessionTestDatabase.ProjectAsync(connection);
        await using (var db = SessionTestDatabase.Context(connection))
            await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "a"), CancellationToken.None);
        Assert.Equal(2L, await WithStoreAsync(connection, store => store.CatchUpAsync(generation, CancellationToken.None)));
        Assert.Equal(0L, await WithStoreAsync(connection, store => store.CatchUpAsync(generation, CancellationToken.None)));
        Assert.Equal(3L, await Count(connection, $"SELECT count(DISTINCT aggregateid) FROM monitoring.projection_outbox WHERE target_topic='{generation.Topic}'"));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='{generation.Topic}' AND revision=2 AND payload->>'operation'='delete'"));
        // The copy of an identity keeps both its old and new revision rows only in the history of the generation topic.
        Assert.Equal(4L, await Count(connection, $"SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='{generation.Topic}'"));
    }

    [Fact]
    public async Task ThePassFailsOnceTheSnapshotDeadlineHasPassed()
    {
        var connection = await ProjectedAsync("a");
        var generation = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None), new GenerationOptions { SnapshotWindow = TimeSpan.FromMilliseconds(-1) });
        var failure = await Assert.ThrowsAsync<SearchGenerationException>(() => WithStoreAsync(connection, store => store.CopyAllAsync(generation, CancellationToken.None)));
        Assert.Equal("snapshot-deadline", failure.Code);
    }

    [Fact]
    public async Task ActivationRetiresThePreviousGenerationAndRequiresAReadyOne()
    {
        var connection = await ProjectedAsync("a");
        var generation = await WithStoreAsync(connection, store => store.BeginAsync(CancellationToken.None));
        var early = await Assert.ThrowsAsync<SearchGenerationException>(() => WithStoreAsync(connection, async store => { await store.ActivateAsync(2, CancellationToken.None); return 0; }));
        Assert.Equal("generation-not-ready", early.Code);
        // The refused activation must not have retired the serving generation.
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=1 AND state='active'"));
        await WithStoreAsync(connection, async store => { await store.MarkReadyAsync(2, CancellationToken.None); await store.ActivateAsync(2, CancellationToken.None); return 0; });
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=1 AND state='retired'"));
        Assert.Equal(generation.Topic, (await WithStoreAsync(connection, store => store.ActiveAsync(CancellationToken.None)))!.Topic);
    }

    [Fact]
    public async Task TheFirstGenerationIsSeededActiveOnTheOriginalTopicAndIndex()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.search_generation
            WHERE generation=1 AND state='active' AND target_topic='monitoring.sessions.v2' AND index_name='sessions-v2-000001'
            """));
    }

    [Fact]
    public async Task WritersFollowTheActiveGenerationTopicAfterASwitch()
    {
        var connection = await ProjectedAsync("before");
        await SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.search_generation(generation,index_name,target_topic,state,snapshot_deadline)
              VALUES (2,'sessions-v2-000002','monitoring.sessions.v2.g2','building',now()+interval '1 hour');
            UPDATE monitoring.search_generation SET state='retired' WHERE generation=1;
            UPDATE monitoring.search_generation SET state='active' WHERE generation=2;
            """);
        await SessionTestDatabase.AcceptAsync(connection, "after");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2.g2' AND payload->>'eventId'='after'"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2' AND payload->>'eventId'='before'"));
    }

    [Fact]
    public async Task OnlyOneGenerationCanBeActive()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.search_generation(generation,index_name,target_topic,state,snapshot_deadline)
              VALUES (2,'sessions-v2-000002','monitoring.sessions.v2.g2','active',now()+interval '1 hour')
            """));
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }
}

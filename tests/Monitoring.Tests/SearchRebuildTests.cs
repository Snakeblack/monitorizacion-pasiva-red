using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json.Nodes;
using Monitoring.Domain.Sessions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence;
using Monitoring.Persistence.Search;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class SearchRebuildTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // Stand-in for Debezium + Kafka + the Elasticsearch sink: applies the generation topic's outbox rows to its index.
    private sealed class PumpingSink(string connection, FakeElasticsearch elastic) : IGenerationSink
    {
        private CancellationTokenSource? _stop;
        public volatile bool Paused;
        public volatile bool SkipOne;
        public List<string> Log { get; } = [];

        public Task StartAsync(GenerationInfo generation, CancellationToken cancellationToken)
        {
            Log.Add($"start g{generation.Generation}");
            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (!Paused) await ApplyAsync(generation);
                    try { await Task.Delay(30, token); } catch (OperationCanceledException) { return; }
                }
            }, token);
            return Task.CompletedTask;
        }

        public Task StopAsync(GenerationInfo generation, CancellationToken cancellationToken)
        {
            Log.Add($"stop g{generation.Generation}");
            _stop?.Cancel();
            return Task.CompletedTask;
        }

        public async Task ApplyAsync(GenerationInfo generation)
        {
            await using var npgsql = new NpgsqlConnection(connection);
            await npgsql.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT payload::text FROM monitoring.projection_outbox WHERE target_topic=@topic ORDER BY revision", npgsql);
            command.Parameters.AddWithValue("topic", generation.Topic);
            var skipped = false;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var source = JsonNode.Parse(reader.GetString(0))!.AsObject();
                if (SkipOne && !skipped) { skipped = true; continue; }
                elastic.Put(generation.IndexName, new FakeElasticsearch.Doc(source["searchDocumentId"]!.GetValue<string>(), source));
            }
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required string Connection { get; init; }
        public required FakeElasticsearch Elastic { get; init; }
        public required PumpingSink Sink { get; init; }
        public required SearchRebuildCoordinator Coordinator { get; init; }
        public required SearchGenerationStore Store { get; init; }
        public required List<MonitoringDbContext> Contexts { get; init; }
        public async ValueTask DisposeAsync() { foreach (var context in Contexts) await context.DisposeAsync(); }
    }

    private async Task<Rig> CreateAsync(int sessions, GenerationOptions? generation = null, RebuildOptions? rebuild = null)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var n in Enumerable.Range(1, sessions)) await SessionTestDatabase.AcceptAsync(connection, $"event-{n}");
        await SessionTestDatabase.ProjectAsync(connection);
        var elastic = new FakeElasticsearch();
        // The serving generation already holds what the pipeline indexed.
        await using (var npgsql = new NpgsqlConnection(connection))
        {
            await npgsql.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT payload::text FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2'", npgsql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var source = JsonNode.Parse(reader.GetString(0))!.AsObject();
                elastic.Put(FakeElasticsearch.DefaultIndex, new FakeElasticsearch.Doc(source["searchDocumentId"]!.GetValue<string>(), source));
            }
        }
        var http = new HttpClient(elastic) { BaseAddress = new Uri("http://elasticsearch.test:9200") };
        var contexts = new List<MonitoringDbContext>();
        MonitoringDbContext Context() { var context = SessionTestDatabase.Context(connection); contexts.Add(context); return context; }
        var sink = new PumpingSink(connection, elastic);
        var store = new SearchGenerationStore(Context(), generation ?? new GenerationOptions());
        var reconciler = new ProjectionReconciler(Context(), new ElasticsearchProjectionIndex(http, new ElasticsearchOptions()),
            new ProjectionOptions { BlockSize = 100, Grace = TimeSpan.Zero });
        var coordinator = new SearchRebuildCoordinator(Context(), store, new ElasticsearchIndexAdmin(http, "{\"mappings\":{\"dynamic\":\"strict\"}}"), sink, reconciler,
            rebuild ?? new RebuildOptions { FinalPhaseBudget = TimeSpan.FromSeconds(20), PollInterval = TimeSpan.FromMilliseconds(40) });
        return new Rig { Connection = connection, Elastic = elastic, Sink = sink, Coordinator = coordinator, Store = store, Contexts = contexts };
    }

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task ARebuildCopiesEverythingSwitchesTheAliasAndMovesWritersToTheNewTopic()
    {
        await using var rig = await CreateAsync(3);
        await using (var db = SessionTestDatabase.Context(rig.Connection))
            await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "event-2"), CancellationToken.None);
        var result = await rig.Coordinator.RunAsync(CancellationToken.None);
        Assert.Equal((RebuildOutcome.Switched, 2, (string?)null), (result.Outcome, result.Generation, result.Reason));
        Assert.Equal(["sessions-v2-000002"], rig.Elastic.AliasTargets("sessions-read"));
        Assert.Equal(3, rig.Elastic.Store("sessions-v2-000002").Count);
        Assert.Equal(1, rig.Elastic.Store("sessions-v2-000002").Count(doc => doc.Source["operation"]!.GetValue<string>() == "delete"));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=1 AND state='retired'"));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=2 AND state='active'"));
        // Writers now publish to the new generation's topic.
        await SessionTestDatabase.AcceptAsync(rig.Connection, "after-switch");
        await SessionTestDatabase.ProjectAsync(rig.Connection);
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2.g2' AND payload->>'eventId'='after-switch'"));
        Assert.Equal(0L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2' AND payload->>'eventId'='after-switch'"));
    }

    [Fact]
    public async Task WritersAreHeldDuringTheFinalPhaseAndPublishToTheNewGenerationOnceItEnds()
    {
        await using var rig = await CreateAsync(2);
        await SessionTestDatabase.AcceptAsync(rig.Connection, "waiting");
        rig.Sink.Paused = true;
        var rebuild = rig.Coordinator.RunAsync(CancellationToken.None);
        const string held = "SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND granted AND mode='ExclusiveLock' AND ((classid::bigint<<32)|objid::bigint)=7182041001";
        for (var attempt = 0; attempt < 200 && await Count(rig.Connection, held) == 0; attempt++) await Task.Delay(25);
        Assert.Equal(1L, await Count(rig.Connection, held));
        var writer = SessionTestDatabase.ProjectAsync(rig.Connection);
        await Task.Delay(600);
        Assert.False(writer.IsCompleted, "a session writer must wait for the exclusive publication lock");
        rig.Sink.Paused = false;
        Assert.Equal(RebuildOutcome.Switched, (await rebuild).Outcome);
        await writer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2.g2' AND payload->>'eventId'='waiting'"));
        Assert.Equal(0L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2' AND payload->>'eventId'='waiting'"));
    }

    [Fact]
    public async Task AFinalPhaseThatCannotCatchUpTimesOutLeavesTheOldIndexServingAndCanBeRetried()
    {
        await using var rig = await CreateAsync(2, rebuild: new RebuildOptions { FinalPhaseBudget = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(40) });
        rig.Sink.Paused = true;
        var first = await rig.Coordinator.RunAsync(CancellationToken.None);
        Assert.Equal((RebuildOutcome.TimedOut, "sink-not-caught-up"), (first.Outcome, first.Reason));
        Assert.Equal([FakeElasticsearch.DefaultIndex], rig.Elastic.AliasTargets("sessions-read"));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=2 AND state='ready'"));
        // Writers are released and still publish to the serving generation.
        await SessionTestDatabase.AcceptAsync(rig.Connection, "released");
        await SessionTestDatabase.ProjectAsync(rig.Connection).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v2' AND payload->>'eventId'='released'"));
        // The retry in a later window copies what changed meanwhile and switches.
        rig.Sink.Paused = false;
        var generation = (await rig.Store.FindAsync(2, CancellationToken.None))!;
        var retry = await rig.Coordinator.SwitchAsync(generation, CancellationToken.None);
        Assert.Equal(RebuildOutcome.Switched, retry.Outcome);
        Assert.Equal(3, rig.Elastic.Store("sessions-v2-000002").Count);
    }

    [Fact]
    public async Task AnIncompleteOrPollutedGenerationIsNeverSwitchedTo()
    {
        await using var missing = await CreateAsync(3, rebuild: new RebuildOptions { FinalPhaseBudget = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(40) });
        missing.Sink.SkipOne = true;
        Assert.Equal("sink-not-caught-up", (await missing.Coordinator.RunAsync(CancellationToken.None)).Reason);
        Assert.Equal([FakeElasticsearch.DefaultIndex], missing.Elastic.AliasTargets("sessions-read"));

        await using var extra = await CreateAsync(2, rebuild: new RebuildOptions { FinalPhaseBudget = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(40) });
        // A document the authority never published (count mismatch) blocks the switch even though every expected one is present.
        extra.Elastic.Put("sessions-v2-000002", new FakeElasticsearch.Doc(Guid.NewGuid().ToString(), new JsonObject { ["revision"] = 1, ["operation"] = "upsert" }));
        Assert.Equal("sink-not-caught-up", (await extra.Coordinator.RunAsync(CancellationToken.None)).Reason);
        Assert.Equal([FakeElasticsearch.DefaultIndex], extra.Elastic.AliasTargets("sessions-read"));
    }

    [Fact]
    public async Task ASnapshotThatOutlivesItsDeadlineIsAbortedAndItsInfrastructureRemoved()
    {
        await using var rig = await CreateAsync(2, new GenerationOptions { SnapshotWindow = TimeSpan.FromMilliseconds(-1) });
        var result = await rig.Coordinator.RunAsync(CancellationToken.None);
        Assert.Equal((RebuildOutcome.Aborted, "snapshot-deadline"), (result.Outcome, result.Reason));
        Assert.False(rig.Elastic.IndexExists("sessions-v2-000002"));
        Assert.Contains("stop g2", rig.Sink.Log);
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=2 AND state='aborted'"));
        Assert.Equal([FakeElasticsearch.DefaultIndex], rig.Elastic.AliasTargets("sessions-read"));
        // The slot is free again.
        await using var db = SessionTestDatabase.Context(rig.Connection);
        Assert.Equal(3, (await new SearchGenerationStore(db, new GenerationOptions()).BeginAsync(CancellationToken.None)).Generation);
    }

    [Fact]
    public async Task AFailedAliasSwitchLeavesTheServingGenerationAndReleasesTheWriters()
    {
        await using var rig = await CreateAsync(2);
        rig.Elastic.FailAliasSwitch = true;
        await Assert.ThrowsAsync<SessionSearchException>(() => rig.Coordinator.RunAsync(CancellationToken.None));
        Assert.Equal([FakeElasticsearch.DefaultIndex], rig.Elastic.AliasTargets("sessions-read"));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=1 AND state='active'"));
        await SessionTestDatabase.AcceptAsync(rig.Connection, "still-working");
        await SessionTestDatabase.ProjectAsync(rig.Connection).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task IfTheAuthoritySwitchFailsAfterTheAliasMovedTheAliasIsRestored()
    {
        await using var rig = await CreateAsync(2);
        await SessionTestDatabase.ExecuteAsync(rig.Connection, """
            CREATE FUNCTION monitoring.refuse_activation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'refused' USING ERRCODE='P0001'; END $$;
            CREATE TRIGGER refuse_activation BEFORE UPDATE ON monitoring.search_generation
            FOR EACH ROW WHEN (NEW.state='active' AND OLD.state='ready') EXECUTE FUNCTION monitoring.refuse_activation();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => rig.Coordinator.RunAsync(CancellationToken.None));
        Assert.Equal([FakeElasticsearch.DefaultIndex], rig.Elastic.AliasTargets("sessions-read"));
        Assert.Equal(1L, await Count(rig.Connection, "SELECT count(*) FROM monitoring.search_generation WHERE generation=1 AND state='active'"));
    }
}

public sealed class SearchAdminHttpTests
{
    private sealed class Recording : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, JsonNode? Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task EachGenerationGetsItsOwnConnectorWiredToItsTopicIndexAndDeadLetterTopic()
    {
        var handler = new Recording();
        var sink = new ConnectGenerationSink(new HttpClient(handler) { BaseAddress = new Uri("http://connect.test:8083") }, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sink.json")));
        var generation = new GenerationInfo(3, "sessions-v2-000003", "monitoring.sessions.v2.g3", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await sink.StartAsync(generation, CancellationToken.None);
        var (method, path, body) = Assert.Single(handler.Calls);
        Assert.Equal((HttpMethod.Put, "/connectors/monitoring-sink-g3/config"), (method, path));
        Assert.Equal("monitoring.sessions.v2.g3", body!["topics"]!.GetValue<string>());
        Assert.Equal("monitoring\\.sessions\\.v2\\.g3", body["transforms.index.regex"]!.GetValue<string>());
        Assert.Equal("sessions-v2-000003", body["transforms.index.replacement"]!.GetValue<string>());
        Assert.Equal("monitoring.sessions.dlq.g3", body["errors.deadletterqueue.topic.name"]!.GetValue<string>());
        // Everything else is inherited from the reviewed sink: authority revision as external version, INSERT, strict contract guard.
        Assert.Equal("revision", body["external.version.header"]!.GetValue<string>());
        Assert.Equal("INSERT", body["write.method"]!.GetValue<string>());
        Assert.Equal("validate,index", body["transforms"]!.GetValue<string>());
        await sink.StopAsync(generation, CancellationToken.None);
        Assert.Equal((HttpMethod.Delete, "/connectors/monitoring-sink-g3"), (handler.Calls[1].Method, handler.Calls[1].Path));
        Assert.Equal("monitoring-sink", ConnectGenerationSink.ConnectorName(1));
    }
}

public sealed class SearchRebuildCommandTests
{
    [Fact]
    public async Task MissingConfigurationFailsWithAGenericMessageAndExitCode1()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Monitoring"] = "Host=secret-host;Password=secret-password"
        }).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await Monitoring.Host.Sessions.SearchRebuildCommand.RunAsync(configuration, output, error, CancellationToken.None));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Contains("required", error.ToString());
    }

    [Fact]
    public async Task AnUnreachableDependencyReportsOnlyTheFailureTypeAndExitCode1()
    {
        var mapping = Path.GetTempFileName();
        var sink = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(mapping, "{}");
            await File.WriteAllTextAsync(sink, "{}");
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Monitoring"] = "Host=127.0.0.1;Port=1;Username=secret-user;Password=secret-password;Timeout=1",
                ["Search:Elasticsearch:Url"] = "http://secret-elastic.invalid:9200",
                ["Search:Connect:Url"] = "http://secret-connect.invalid:8083",
                ["Search:Rebuild:MappingPath"] = mapping,
                ["Search:Rebuild:SinkTemplatePath"] = sink
            }).Build();
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, await Monitoring.Host.Sessions.SearchRebuildCommand.RunAsync(configuration, output, error, CancellationToken.None));
            Assert.DoesNotContain("secret", error.ToString());
            Assert.Contains("previous index keeps serving", error.ToString());
        }
        finally { File.Delete(mapping); File.Delete(sink); }
    }
}

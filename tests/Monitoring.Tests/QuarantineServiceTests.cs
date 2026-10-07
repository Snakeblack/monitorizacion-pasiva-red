using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Monitoring.Host.Sessions;
using Monitoring.Persistence.Ingestion;
using Npgsql;

namespace Monitoring.Tests;

public sealed class QuarantineServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Incompatible = "{\"arbitrary\":true,\"secret\":\"payload-must-not-leak\"}";

    // Two quarantined events (bad-1, bad-2) and one valid event (good) already projected.
    private async Task<string> SeedAsync()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "bad-1", json: Incompatible);
        await SessionTestDatabase.AcceptAsync(connection, "good");
        await SessionTestDatabase.AcceptAsync(connection, "bad-2", json: Incompatible);
        await SessionTestDatabase.ProjectAsync(connection);
        return connection;
    }

    private static async Task<T> WithServiceAsync<T>(string connection, Func<QuarantineService, Task<T>> action)
    {
        await using var db = SessionTestDatabase.Context(connection);
        return await action(new QuarantineService(db));
    }

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task ListShowsIdentityCauseAndStateWithoutAnyPayloadAndHonoursFilterAndLimit()
    {
        var connection = await SeedAsync();
        var all = await WithServiceAsync(connection, service => service.ListAsync(null, 100, CancellationToken.None));
        Assert.Equal(["bad-1", "bad-2"], all.Select(entry => entry.EventId).Order());
        Assert.All(all, entry => Assert.Equal(("site", "sensor", "contract-invalid", "unresolved", 1), (entry.SiteId, entry.SensorId, entry.Cause, entry.State, entry.Attempts)));
        Assert.DoesNotContain("payload-must-not-leak", System.Text.Json.JsonSerializer.Serialize(all));
        Assert.Single(await WithServiceAsync(connection, service => service.ListAsync("unresolved", 1, CancellationToken.None)));
        Assert.Empty(await WithServiceAsync(connection, service => service.ListAsync("discarded", 100, CancellationToken.None)));
        foreach (var invalid in new[] { 0, -1, 501 })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WithServiceAsync(connection, service => service.ListAsync(null, invalid, CancellationToken.None)));
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(connection, service => service.ListAsync("deleted", 10, CancellationToken.None)));
    }

    [Fact]
    public async Task DiscardIsAuditedOnceAndNeverTouchesTheOriginalEvent()
    {
        var connection = await SeedAsync();
        var before = await Count(connection, "SELECT hashtext(data::text || occurred_at_text)::bigint FROM monitoring.ingestion_inbox WHERE event_id='bad-1'");
        var result = await WithServiceAsync(connection, service => service.DiscardAsync("site", "sensor", "bad-1", "ops-alice", "sender bug, replaced upstream", CancellationToken.None));
        Assert.Equal(ResolutionResult.Resolved, result);
        Assert.Equal(1L, await Count(connection, """
            SELECT count(*) FROM monitoring.ingestion_quarantine WHERE event_id='bad-1' AND state='discarded' AND resolved_by='ops-alice'
              AND resolution_reason='sender bug, replaced upstream' AND resolved_at IS NOT NULL
            """));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='bad-1' AND action='discarded' AND actor='ops-alice'"));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='bad-1'")); // quarantined + discarded
        Assert.Equal(before, await Count(connection, "SELECT hashtext(data::text || occurred_at_text)::bigint FROM monitoring.ingestion_inbox WHERE event_id='bad-1'"));
        // Repeating changes nothing and is not audited twice; an unknown identity is reported, not invented.
        Assert.Equal(ResolutionResult.AlreadyResolved, await WithServiceAsync(connection, service => service.DiscardAsync("site", "sensor", "bad-1", "ops-bob", "again", CancellationToken.None)));
        Assert.Equal(ResolutionResult.NotFound, await WithServiceAsync(connection, service => service.DiscardAsync("site", "sensor", "good", "ops-bob", "not quarantined", CancellationToken.None)));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='bad-1'"));
        // An unresolved event is never purged: the inbox row of bad-2 cannot be deleted.
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, "DELETE FROM monitoring.ingestion_inbox WHERE event_id='bad-2'"));
    }

    [Fact]
    public async Task ReplaceLinksTheCorrectedEventOnlyWhenItWasAcceptedAndProcessedForTheSameOrigin()
    {
        var connection = await SeedAsync();
        Assert.Equal(ResolutionResult.InvalidReplacement, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "does-not-exist", "ops", "fix", CancellationToken.None)));
        Assert.Equal(ResolutionResult.InvalidReplacement, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "bad-1", "ops", "fix", CancellationToken.None)));
        Assert.Equal(ResolutionResult.InvalidReplacement, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "bad-2", "ops", "fix", CancellationToken.None)));
        await SessionTestDatabase.AcceptAsync(connection, "good-other-origin", "other-site", "sensor");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(ResolutionResult.InvalidReplacement, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "good-other-origin", "ops", "fix", CancellationToken.None)));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='replaced'"));

        Assert.Equal(ResolutionResult.Resolved, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "good", "ops-alice", "corrected event re-sent", CancellationToken.None)));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE event_id='bad-1' AND state='replaced' AND replaced_by_event_id='good'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='bad-1' AND action='replaced' AND replaced_by_event_id='good' AND actor='ops-alice'"));
        Assert.Equal(ResolutionResult.AlreadyResolved, await WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "good", "ops", "again", CancellationToken.None)));
    }

    [Theory]
    [InlineData("", "reason")]
    [InlineData("   ", "reason")]
    [InlineData("actor", "")]
    [InlineData("actor", "  ")]
    public async Task AnActorAndAReasonAreMandatoryForEveryResolution(string actor, string reason)
    {
        var connection = await SeedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(connection, s => s.DiscardAsync("site", "sensor", "bad-1", actor, reason, CancellationToken.None)));
        await Assert.ThrowsAsync<ArgumentException>(() => WithServiceAsync(connection, s => s.ReplaceAsync("site", "sensor", "bad-1", "good", actor, reason, CancellationToken.None)));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='unresolved'"));
    }

    [Fact]
    public async Task ARejectedAuditRecordRollsTheResolutionBack()
    {
        var connection = await SeedAsync();
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE FUNCTION monitoring.refuse_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE='P0001'; END $$;
            CREATE TRIGGER refuse_audit BEFORE INSERT ON monitoring.ingestion_quarantine_audit
            FOR EACH ROW WHEN (NEW.action='discarded') EXECUTE FUNCTION monitoring.refuse_audit();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => WithServiceAsync(connection, s => s.DiscardAsync("site", "sensor", "bad-1", "ops", "reason", CancellationToken.None)));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE event_id='bad-1' AND state='unresolved' AND resolved_at IS NULL"));
    }

    [Fact]
    public async Task ConcurrentResolutionsOfTheSameEventResolveItExactlyOnce()
    {
        var connection = await SeedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(n =>
            WithServiceAsync(connection, s => s.DiscardAsync("site", "sensor", "bad-1", $"ops-{n}", "race", CancellationToken.None))));
        Assert.Equal(1, results.Count(r => r == ResolutionResult.Resolved));
        Assert.Equal(4, results.Count(r => r == ResolutionResult.AlreadyResolved));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='bad-1' AND action='discarded'"));
    }

    [Fact]
    public async Task TheSummaryReconcilesTheInboxBalanceByState()
    {
        var connection = await SeedAsync();
        await WithServiceAsync(connection, s => s.DiscardAsync("site", "sensor", "bad-1", "ops", "reason", CancellationToken.None));
        var summary = await WithServiceAsync(connection, s => s.SummaryAsync(CancellationToken.None));
        Assert.Equal((3L, 1L, 2L), (summary.Accepted, summary.Processed, summary.Quarantined));
        Assert.Equal(0L, summary.Pending);
        Assert.Equal(1L, summary.UnresolvedByCause["contract-invalid"]);
        Assert.Equal(1L, summary.Discarded);
        Assert.Equal(0L, summary.Replaced);
        Assert.NotNull(summary.OldestUnresolvedAgeSeconds);
        // Accepted = processed + quarantined + pending: the balance an operator can reconcile.
        Assert.Equal(summary.Accepted, summary.Processed + summary.Quarantined + summary.Pending);
    }
}

public sealed class QuarantineCommandTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static IConfiguration Configuration(string connection) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Monitoring"] = connection }).Build();

    private static async Task<(int Code, string Output, string Error)> RunAsync(string connection, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await QuarantineCommand.RunAsync(args, Configuration(connection), output, error, CancellationToken.None);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task OperatorsListSummarizeAndResolveThroughTheAuditedCommandWithoutSeeingPayloads()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "bad", json: "{\"x\":\"payload-must-not-leak\"}");
        await SessionTestDatabase.AcceptAsync(connection, "good");
        await SessionTestDatabase.ProjectAsync(connection);

        var list = await RunAsync(connection, "list");
        Assert.Equal(0, list.Code);
        Assert.Contains("site/sensor/bad", list.Output);
        Assert.Contains("contract-invalid", list.Output);
        Assert.DoesNotContain("payload-must-not-leak", list.Output + list.Error);
        var summary = await RunAsync(connection, "summary");
        Assert.Equal(0, summary.Code);
        Assert.Contains("accepted=2", summary.Output);
        Assert.Contains("quarantined=1", summary.Output);

        // Resolution needs an actor and a reason; without them nothing changes.
        Assert.Equal(1, (await RunAsync(connection, "discard", "site", "sensor", "bad")).Code);
        Assert.Equal(1, (await RunAsync(connection, "discard", "site", "sensor", "bad", "--actor", "ops")).Code);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='unresolved'"));

        var discard = await RunAsync(connection, "discard", "site", "sensor", "bad", "--actor", "ops-alice", "--reason", "sender bug");
        Assert.Equal((0, true), (discard.Code, discard.Output.Contains("resolved", StringComparison.Ordinal)));
        Assert.Equal(2, (await RunAsync(connection, "discard", "site", "sensor", "bad", "--actor", "ops-alice", "--reason", "again")).Code);
        Assert.Equal(1, (await RunAsync(connection, "unknown-action")).Code);
    }

    [Fact]
    public async Task ReplaceThroughTheCommandLinksTheCorrectedEvent()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "bad", json: "{\"x\":1}");
        await SessionTestDatabase.AcceptAsync(connection, "corrected");
        await SessionTestDatabase.ProjectAsync(connection);
        var replace = await RunAsync(connection, "replace", "site", "sensor", "bad", "corrected", "--actor", "ops", "--reason", "re-sent");
        Assert.Equal(0, replace.Code);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE state='replaced' AND replaced_by_event_id='corrected'"));
        Assert.Equal(2, (await RunAsync(connection, "replace", "site", "sensor", "bad", "missing", "--actor", "ops", "--reason", "x")).Code);
    }

    [Fact]
    public async Task MissingConfigurationFailsWithAGenericMessage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await QuarantineCommand.RunAsync(["list"], new ConfigurationBuilder().Build(), output, error, CancellationToken.None);
        Assert.Equal(1, code);
        Assert.Contains("ConnectionStrings:Monitoring", error.ToString());
    }
}

public sealed class QuarantineMetricsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class Scopes(string connection) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(connection);
        private sealed class Scope(string connection) : IServiceScope
        {
            private readonly Monitoring.Persistence.MonitoringDbContext db = SessionTestDatabase.Context(connection);
            public IServiceProvider ServiceProvider { get; } = new Provider(SessionTestDatabase.Context(connection));
            public void Dispose() => db.Dispose();
        }
        private sealed class Provider(Monitoring.Persistence.MonitoringDbContext db) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(Monitoring.Persistence.MonitoringDbContext) ? db : null;
        }
    }

    [Fact]
    public async Task TheBalanceAndTheUnresolvedQuarantineArePublishedFromTheDatabaseSummary()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "bad", json: "{\"x\":1}");
        await SessionTestDatabase.AcceptAsync(connection, "good");
        await SessionTestDatabase.ProjectAsync(connection);
        using var metrics = new QuarantineMetrics(new Scopes(connection), Microsoft.Extensions.Logging.Abstractions.NullLogger<QuarantineMetrics>.Instance);
        var values = new Dictionary<string, double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            // Match the exact meter instance: other hosts in the same process publish meters with the same name.
            if (ReferenceEquals(instrument.Meter, metrics.Meter)) meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            values[instrument.Name + string.Concat(tags.ToArray().Select(tag => $"[{tag.Key}={tag.Value}]"))] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => values[instrument.Name] = value);
        listener.Start();

        await metrics.RefreshAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        Assert.Equal((2, 1, 1, 0), ((long)values["monitoring.ingestion.accepted_events"], (long)values["monitoring.ingestion.processed_events"],
            (long)values["monitoring.ingestion.quarantined_events"], (long)values["monitoring.ingestion.pending_events"]));
        Assert.Equal(1, values["monitoring.ingestion.quarantine_unresolved[cause=contract-invalid]"]);
        Assert.True(values["monitoring.ingestion.quarantine_oldest_unresolved_seconds"] >= 0);

        // Resolving the event changes the published value on the next refresh, once.
        await using (var db = SessionTestDatabase.Context(connection))
            await new QuarantineService(db).DiscardAsync("site", "sensor", "bad", "ops", "reason", CancellationToken.None);
        values.Clear();
        await metrics.RefreshAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        Assert.DoesNotContain(values.Keys, key => key.StartsWith("monitoring.ingestion.quarantine_unresolved", StringComparison.Ordinal));
        Assert.DoesNotContain("monitoring.ingestion.quarantine_oldest_unresolved_seconds", values.Keys);
    }
}

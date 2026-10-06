using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Domain.Ingestion;
using Monitoring.Persistence.Ingestion;
using Monitoring.Persistence.Retention;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

// The fixture sessions end on 2026-09-29, so a three-day window always has them expired and a 100000-day window never does,
// whatever day the suite runs.
public sealed class RetentionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Expired = TimeSpan.FromDays(3);
    private static readonly TimeSpan Forever = TimeSpan.FromDays(100000);

    private static RetentionOptions Options(TimeSpan? sessions = null, TimeSpan? outbox = null, TimeSpan? margin = null, int batch = 500) => new()
    {
        SessionRetention = sessions ?? Expired, OutboxRetention = outbox ?? Forever, TombstoneMargin = margin ?? Forever, BatchSize = batch
    };

    private static async Task<RetentionReport> RunAsync(string connection, RetentionOptions options, CancellationToken cancellationToken = default)
    {
        await using var db = SessionTestDatabase.Context(connection);
        return await new RetentionService(db, options).RunAsync(cancellationToken);
    }

    private async Task<string> ProjectedAsync(int sessions = 1)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        for (var index = 0; index < sessions; index++) await SessionTestDatabase.AcceptAsync(connection, index == 0 ? "event" : $"event-{index}");
        await SessionTestDatabase.ProjectAsync(connection);
        return connection;
    }

    [Fact]
    public async Task AnExpiredSessionBecomesATombstoneWithTheSameMinimalBarrierAsASuppressionAndNoTrafficData()
    {
        var connection = await ProjectedAsync(2);
        await using (var db = SessionTestDatabase.Context(connection))
            await new SessionSuppressor(db).SuppressAsync(new Monitoring.Domain.Sessions.SessionIdentity("site", "sensor", "event-1"), CancellationToken.None);
        var report = await RunAsync(connection, Options());
        Assert.Equal(1, report.ExpiredSessions); // event-1 was already suppressed by hand
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted' AND revision=2 AND deleted_at IS NOT NULL"));
        // Retention and a manual suppression publish identical barrier shapes (apart from identity values).
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(DISTINCT (SELECT array_agg(k ORDER BY k) FROM jsonb_object_keys(payload) k)) FROM monitoring.projection_outbox
            WHERE payload->>'operation'='delete'
            """));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.projection_outbox o JOIN monitoring.session_identity i ON i.document_key=o.aggregateid
            WHERE o.revision=2 AND o.schema_version=2 AND o.target_topic='monitoring.sessions.v2' AND o.payload->>'operation'='delete'
              AND o.payload->>'searchDocumentId'=i.search_document_id::text AND (o.payload->>'revision')::bigint=2
              AND NOT o.payload ? 'sourceIp' AND NOT o.payload ? 'startedAt' AND NOT o.payload ? 'acceptedAt'
            """));
    }

    [Fact]
    public async Task SessionsInsideTheWindowAreUntouchedAndARerunChangesNothing()
    {
        var connection = await ProjectedAsync();
        var untouched = await RunAsync(connection, Options(sessions: Forever));
        Assert.Equal(0, untouched.ExpiredSessions);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1, (await RunAsync(connection, Options())).ExpiredSessions);
        var again = await RunAsync(connection, Options());
        Assert.Equal((0, 0, 0, 0, 0), (again.ExpiredSessions, again.DeletedInbox, again.DeletedObservations, again.DeletedOutbox, again.PurgedTombstones));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task ManySessionsAreExpiredInBoundedBatches()
    {
        var connection = await ProjectedAsync(7);
        var report = await RunAsync(connection, Options(batch: 3));
        Assert.Equal(7, report.ExpiredSessions);
        Assert.Equal(7L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted'"));
        Assert.Equal(7L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'operation'='delete'"));
    }

    [Fact]
    public async Task PendingEventsAreNeverDeletedButAnExpiredOneIsProcessedWithoutCreatingASession()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "pending");
        var report = await RunAsync(connection, Options());
        Assert.Equal(0, report.DeletedInbox);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
        await using (var db = SessionTestDatabase.Context(connection))
            await new SessionProjector(db, Options()).RunPassAsync(CancellationToken.None);
        // Processed (so it is not retried forever) but it leaves no session, identity or publication behind.
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task AReplayOfAnExpiredEventIsNeverResurrectedAtTheDoorNorWhileItsTombstoneLives()
    {
        var connection = await ProjectedAsync();
        await RunAsync(connection, Options()); // expires the session, keeps the tombstone
        // 1. Tombstone alive: the replay (same identity) reaches the worker and is quarantined, never projected.
        await SessionTestDatabase.ExecuteAsync(connection, "DELETE FROM monitoring.ingestion_inbox");
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE cause='identity-suppressed'"));
        // 2. At the door: an event older than the window is acknowledged and dropped, so nothing stores its traffic data again.
        var fresh = await SessionTestDatabase.CreateAsync(postgres);
        await using var db = SessionTestDatabase.Context(fresh);
        using var document = System.Text.Json.JsonDocument.Parse(SyntheticSessionContractTests.ValidData);
        var batch = new IngestionBatch(1, "b", "site", "sensor", [new IngestionEvent("old", "2026-09-29T12:00:00.100Z", document.RootElement.Clone())]);
        var result = await new InboxWriter(db, new IngestionOptions { EventRetention = Expired }).WriteAsync("site", "sensor", batch, CancellationToken.None);
        Assert.Equal(InboxWriteResult.Accepted, result);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(fresh, "SELECT count(*) FROM monitoring.ingestion_inbox"));
        // Without a window nothing is dropped.
        var kept = await new InboxWriter(db, new IngestionOptions()).WriteAsync("site", "sensor", batch, CancellationToken.None);
        Assert.Equal(InboxWriteResult.Accepted, kept);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(fresh, "SELECT count(*) FROM monitoring.ingestion_inbox"));
    }

    [Fact]
    public async Task ProcessedInboxRowsExpireButUnresolvedAndAuditedOnesStay()
    {
        var connection = await ProjectedAsync();
        await SessionTestDatabase.AcceptAsync(connection, "bad", json: "{\"x\":1}");
        await SessionTestDatabase.AcceptAsync(connection, "later");
        await SessionTestDatabase.ProjectAsync(connection); // event and later projected, bad quarantined
        await SessionTestDatabase.AcceptAsync(connection, "pending", json: SyntheticSessionContractTests.ValidData.Replace("2026-09-29T12:00:00.123Z", "2026-09-29T12:00:01Z", StringComparison.Ordinal));
        var report = await RunAsync(connection, Options());
        Assert.Equal(2, report.DeletedInbox); // event and later
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE event_id='bad' AND quarantined_at IS NOT NULL"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE event_id='pending' AND processed_at IS NULL"));
    }

    [Fact]
    public async Task RawObservationsAndTemporaryIpAssociationsExpireButTheCandidateAndItsDecisionStay()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "obs", json: DeviceObservationContractTests.Observation("2026-09-29T10:00:00Z", "192.0.2.10"));
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal((1L, 1L, 1L), (await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_observation"),
            await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_candidate"),
            await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_ip_association")));
        var report = await RunAsync(connection, Options());
        Assert.Equal(1, report.DeletedObservations);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_observation"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_ip_association"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_candidate"));
        // The observation's accepted event can now be expired too.
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox"));
    }

    [Fact]
    public async Task OnlySupersededOutboxRowsPastTheirWindowAreRemovedAndTheLatestOfEveryIdentityStays()
    {
        var connection = await ProjectedAsync();
        await RunAsync(connection, Options()); // adds the revision-2 barrier; the revision-1 upsert is superseded
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        // Inside the outbox window nothing is removed.
        Assert.Equal(0, (await RunAsync(connection, Options(outbox: Forever))).DeletedOutbox);
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.projection_outbox SET created_at = created_at - interval '3 days'");
        var report = await RunAsync(connection, Options(outbox: TimeSpan.FromDays(2)));
        Assert.Equal(1, report.DeletedOutbox);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE revision=2 AND payload->>'operation'='delete'"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload ? 'sourceIp'"));
    }

    [Fact]
    public async Task AnUpsertThatIsStillTheLatestRowOfAnActiveSessionIsNeverRemoved()
    {
        var connection = await ProjectedAsync();
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.projection_outbox SET created_at = created_at - interval '30 days'");
        Assert.Equal(0, (await RunAsync(connection, Options(sessions: Forever, outbox: TimeSpan.FromHours(1)))).DeletedOutbox);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task TombstonesAndTheirPublicationRowsArePurgedOnlyAfterTheWindowPlusTheMargin()
    {
        var connection = await ProjectedAsync();
        await RunAsync(connection, Options());
        Assert.Equal(0, (await RunAsync(connection, Options(margin: TimeSpan.FromDays(1)))).PurgedTombstones);
        // Past the window but inside the margin (3.5 d of 3 d + 1 d): a late replay could still be accepted, so it stays.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.session_identity SET deleted_at = clock_timestamp() - interval '84 hours'");
        Assert.Equal(0, (await RunAsync(connection, Options(margin: TimeSpan.FromDays(1)))).PurgedTombstones);
        // The tombstone is old enough only when deleted_at is beyond retention + margin.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.session_identity SET deleted_at = clock_timestamp() - interval '5 days'");
        var report = await RunAsync(connection, Options(margin: TimeSpan.FromDays(1)));
        Assert.Equal(1, report.PurgedTombstones);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task AnInactiveLogicalSlotKeepsTheOutboxBecauseTheConsumerMayStillNeedIt()
    {
        var connection = await ProjectedAsync();
        await RunAsync(connection, Options());
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.projection_outbox SET created_at = created_at - interval '3 days'");
        var report = await RunAsync(connection, Options(outbox: TimeSpan.FromDays(2)), new CancellationToken());
        // Without any logical slot (this server is not running with logical replication) nothing blocks it; the guard itself is
        // exercised through the reader contract below.
        Assert.False(report.OutboxSkipped);
        Assert.True(RetentionService.ConsumerLagBlocksPurge([new Monitoring.Persistence.Operations.ReplicationSlotStatus("monitoring_outbox", false, 0, 0)]));
        Assert.False(RetentionService.ConsumerLagBlocksPurge([new Monitoring.Persistence.Operations.ReplicationSlotStatus("monitoring_outbox", true, 0, 0)]));
        Assert.False(RetentionService.ConsumerLagBlocksPurge([]));
    }

    [Fact]
    public async Task RetentionWaitsForARebuildThatHoldsThePublicationLockExclusively()
    {
        var connection = await ProjectedAsync();
        await using var holder = new NpgsqlConnection(connection);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var exclusive = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({OutboxStore.PublicationLock})", holder, transaction))
            await exclusive.ExecuteNonQueryAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(connection, Options(), timeout.Token));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        await transaction.RollbackAsync();
        Assert.Equal(1, (await RunAsync(connection, Options())).ExpiredSessions);
    }

    [Theory]
    [InlineData(0, 2, 1, 500)]
    [InlineData(30, 0, 1, 500)]
    [InlineData(30, 2, -1, 500)]
    [InlineData(30, 2, 1, 0)]
    [InlineData(30, 2, 1, 100000)]
    public void InvalidOptionsAreRejected(int sessionDays, int outboxDays, int marginDays, int batch)
    {
        var options = new RetentionOptions { SessionRetention = TimeSpan.FromDays(sessionDays), OutboxRetention = TimeSpan.FromDays(outboxDays), TombstoneMargin = TimeSpan.FromDays(marginDays), BatchSize = batch };
        Assert.Throws<InvalidOperationException>(options.Validate);
        new RetentionOptions().Validate();
    }
}

public sealed class RetentionHostTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void ProductionRefusesToStartWithoutRetentionConfiguredEvenWithEverythingElseValid()
    {
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Monitoring", "Host=unused;Database=unused");
            builder.UseSetting("Identity:Mode", "Oidc");
            builder.UseSetting("Identity:Authority", "https://idp.test/realms/monitoring");
            builder.UseSetting("Identity:Audience", "monitoring-api");
            ProbeTestTrust.For(builder, "Production", withRetention: false);
        });
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Retention", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConfiguredHostRunsTheRetentionPassAndPublishesItsAgeAndWhatItRemoved()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        using var factory = SessionHostTests.CreateHost(connection, null).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Retention:SessionRetention", "3.00:00:00");
            builder.UseSetting("Retention:RunInterval", "00:00:01");
        });
        using var client = factory.CreateClient();
        var worker = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Monitoring.Host.Operations.RetentionWorker>().Single();
        await SessionWorkerTests.WaitAsync(async () => await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted'") == 1);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        var values = new Dictionary<string, double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (ReferenceEquals(instrument.Meter, worker.Meter)) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            values[instrument.Name + string.Concat(tags.ToArray().Select(tag => $"[{tag.Key}={tag.Value}]"))] = values.GetValueOrDefault(instrument.Name + string.Concat(tags.ToArray().Select(tag => $"[{tag.Key}={tag.Value}]"))) + value);
        listener.Start();
        // The gauge only exists once a whole pass completed, which happens after the first (visible) step.
        await SessionWorkerTests.WaitAsync(() =>
        {
            listener.RecordObservableInstruments();
            return Task.FromResult(values.ContainsKey("monitoring.retention.seconds_since_last_run"));
        });
        Assert.InRange(values["monitoring.retention.seconds_since_last_run"], 0, 60);
    }

    private sealed class Scopes(string connection) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(connection);
        private sealed class Scope(string connection) : IServiceScope
        {
            private readonly Monitoring.Persistence.MonitoringDbContext _db = SessionTestDatabase.Context(connection);
            public IServiceProvider ServiceProvider => field ??= new Provider(_db);
            public void Dispose() => _db.Dispose();
        }
        private sealed class Provider(Monitoring.Persistence.MonitoringDbContext db) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(Monitoring.Persistence.MonitoringDbContext) ? db : null;
        }
    }

    [Fact]
    public async Task APassCountsWhatItRemovedByKindAndAFailedOneDoesNotLookHealthy()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        var options = new RetentionOptions { SessionRetention = TimeSpan.FromDays(3), OutboxRetention = TimeSpan.FromDays(100000), TombstoneMargin = TimeSpan.FromDays(100000) };
        using var worker = new Monitoring.Host.Operations.RetentionWorker(new Scopes(connection), options, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Monitoring.Host.Operations.RetentionWorker>.Instance);
        var values = new Dictionary<string, double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (ReferenceEquals(instrument.Meter, worker.Meter)) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var key = instrument.Name + string.Concat(tags.ToArray().Select(tag => $"[{tag.Key}={tag.Value}]"));
            values[key] = values.GetValueOrDefault(key) + value;
        });
        listener.Start();
        // Before any pass the age is absent, never zero.
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => { listener.RecordObservableInstruments(); await Task.Yield(); _ = values["monitoring.retention.seconds_since_last_run"]; });
        var report = await worker.RunOnceAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        Assert.Equal(1, report.ExpiredSessions);
        Assert.Equal(1, values["monitoring.retention.removed[kind=sessions]"]);
        Assert.Equal(1, values["monitoring.retention.removed[kind=inbox]"]);
        Assert.InRange(values["monitoring.retention.seconds_since_last_run"], 0, 60);
    }

    [Fact]
    public async Task DevelopmentAndTestingDoNotRunRetentionUnlessItIsConfigured()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var factory = SessionHostTests.CreateHost(connection, null);
        using var client = factory.CreateClient();
        Assert.Empty(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Monitoring.Host.Operations.RetentionWorker>());
    }
}

public sealed partial class RetentionInvariantTests
{
    [System.Text.RegularExpressions.GeneratedRegex(@"KAFKA_LOG_RETENTION_HOURS:\s*(\d+)")]
    private static partial System.Text.RegularExpressions.Regex KafkaRetention();

    // A replayed topic record must never outlive the tombstone that blocks it: Kafka keeps publication records for less than
    // SessionRetention + TombstoneMargin, and the outbox keeps superseded rows for less than that too.
    [Fact]
    public void BrokerRetentionAndOutboxRetentionStayBelowTheLifeOfATombstone()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yaml"))) directory = directory.Parent;
        var match = KafkaRetention().Match(File.ReadAllText(Path.Combine(directory!.FullName, "compose.yaml")));
        Assert.True(match.Success, "compose.yaml must set KAFKA_LOG_RETENTION_HOURS explicitly");
        var defaults = new RetentionOptions();
        var tombstoneLife = defaults.SessionRetention + defaults.TombstoneMargin;
        Assert.True(TimeSpan.FromHours(int.Parse(match.Groups[1].Value)) < tombstoneLife);
        Assert.True(defaults.OutboxRetention < tombstoneLife);
    }
}

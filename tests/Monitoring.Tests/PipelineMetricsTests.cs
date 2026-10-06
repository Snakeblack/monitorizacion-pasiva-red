using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Operations;
using Monitoring.Persistence;
using Monitoring.Persistence.Operations;

namespace Monitoring.Tests;

public sealed class PipelineMetricsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class Status(SearchFreshness freshness) : IProjectionStatus
    {
        public Task<SearchFreshness> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(freshness);
    }

    private sealed class Scopes(string connection, IProjectionStatus? status = null) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(connection, status);

        private sealed class Scope(string connection, IProjectionStatus? status) : IServiceScope
        {
            private readonly MonitoringDbContext _db = SessionTestDatabase.Context(connection);
            public IServiceProvider ServiceProvider => field ??= new Provider(_db, status);
            public void Dispose() => _db.Dispose();
        }

        private sealed class Provider(MonitoringDbContext db, IProjectionStatus? status) : IServiceProvider
        {
            public object? GetService(Type serviceType) =>
                serviceType == typeof(MonitoringDbContext) ? db : serviceType == typeof(IProjectionStatus) ? status : null;
        }
    }

    private static async Task<Dictionary<string, double>> ObserveAsync(PipelineMetrics metrics)
    {
        var values = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter)) meterListener.EnableMeasurementEvents(instrument);
        };
        void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct, IConvertible =>
            values[instrument.Name + string.Concat(tags.ToArray().Select(tag => $"[{tag.Key}={tag.Value}]"))] = value.ToDouble(null);
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
        await metrics.RefreshAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        return values;
    }

    [Fact]
    public async Task TheAgeOfTheOldestUnprojectedEventIsPublishedAndDisappearsWhenNothingWaits()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var metrics = new PipelineMetrics(new Scopes(connection), NullLogger<PipelineMetrics>.Instance);
        Assert.DoesNotContain("monitoring.pipeline.projection_oldest_pending_seconds", (await ObserveAsync(metrics)).Keys);

        await SessionTestDatabase.AcceptAsync(connection, "waiting");
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET accepted_at = now() - interval '10 minutes'");
        var waiting = await ObserveAsync(metrics);
        Assert.InRange(waiting["monitoring.pipeline.projection_oldest_pending_seconds"], 590, 700);

        // A quarantined event is not "waiting": it is accounted for by the quarantine metrics, not by projection lag.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET quarantined_at = now()");
        Assert.DoesNotContain("monitoring.pipeline.projection_oldest_pending_seconds", (await ObserveAsync(metrics)).Keys);
    }

    [Fact]
    public async Task ReplicationSlotsPublishRetainedWalAndActivityAndDisappearWhenDropped()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.ExecuteAsync(connection, "SELECT pg_create_physical_replication_slot('pipeline_probe_slot', true)");
        using var metrics = new PipelineMetrics(new Scopes(connection), NullLogger<PipelineMetrics>.Instance);
        var values = await ObserveAsync(metrics);
        Assert.True(values["monitoring.pipeline.wal_slot_retained_bytes[slot=pipeline_probe_slot]"] >= 0);
        Assert.Equal(0, values["monitoring.pipeline.wal_slot_active[slot=pipeline_probe_slot]"]);

        await SessionTestDatabase.ExecuteAsync(connection, "SELECT pg_drop_replication_slot('pipeline_probe_slot')");
        Assert.DoesNotContain(await ObserveAsync(metrics), pair => pair.Key.Contains("pipeline_probe_slot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchFreshnessIsPublishedWhenAProjectionStatusExistsAndUnknownLagIsNeverZero()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var lagging = new PipelineMetrics(new Scopes(connection, new Status(new SearchFreshness(FreshnessState.Lagging, DateTimeOffset.UtcNow, 420))), NullLogger<PipelineMetrics>.Instance);
        var values = await ObserveAsync(lagging);
        Assert.Equal(420, values["monitoring.search.freshness_lag_seconds"]);
        Assert.Equal(1, values["monitoring.search.freshness_state"]);

        using var unknown = new PipelineMetrics(new Scopes(connection, new Status(new SearchFreshness(FreshnessState.Recovering, DateTimeOffset.UtcNow, null))), NullLogger<PipelineMetrics>.Instance);
        var unknownValues = await ObserveAsync(unknown);
        Assert.DoesNotContain("monitoring.search.freshness_lag_seconds", unknownValues.Keys);
        Assert.Equal(2, unknownValues["monitoring.search.freshness_state"]);

        using var none = new PipelineMetrics(new Scopes(connection), NullLogger<PipelineMetrics>.Instance);
        Assert.DoesNotContain("monitoring.search.freshness_state", (await ObserveAsync(none)).Keys);
    }

    [Fact]
    public async Task AFailedRefreshKeepsThePreviousValuesAndNeverThrowsOutOfTheWorker()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "waiting");
        using var metrics = new PipelineMetrics(new Scopes(connection), NullLogger<PipelineMetrics>.Instance, TimeSpan.FromMilliseconds(50));
        Assert.Contains("monitoring.pipeline.projection_oldest_pending_seconds", (await ObserveAsync(metrics)).Keys);
        await SessionTestDatabase.ExecuteAsync(connection, "ALTER TABLE monitoring.ingestion_inbox RENAME TO ingestion_inbox_gone");
        await metrics.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await metrics.StopAsync(CancellationToken.None);
        var after = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (ReferenceEquals(instrument.Meter, metrics.Meter)) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => after[instrument.Name] = value);
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => after[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        Assert.Contains("monitoring.pipeline.projection_oldest_pending_seconds", after.Keys);
    }
}

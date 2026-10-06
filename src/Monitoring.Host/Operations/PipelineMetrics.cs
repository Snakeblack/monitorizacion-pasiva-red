using System.Diagnostics.Metrics;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence;
using Monitoring.Persistence.Operations;

namespace Monitoring.Host.Operations;

// Publishes where the pipeline is stuck: unprojected events waiting in the authority, WAL retained by (and not yet confirmed to)
// each replication slot, and the freshness the search side reports. Absent values mean "not observable right now", never zero, so an
// alert on lag cannot be silenced by a failed read. A failed refresh keeps the last values and logs only the failure type.
public sealed class PipelineMetrics(IServiceScopeFactory scopeFactory, ILogger<PipelineMetrics> logger, TimeSpan? interval = null) : BackgroundService
{
    public const string MeterName = "Monitoring.Pipeline";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private volatile PipelineSnapshot? _snapshot;
    private volatile SearchFreshness? _freshness;
    private int _registered;

    // The instance, so a listener can tell this service's instruments from another host's in the same process.
    public Meter Meter => _meter;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        _snapshot = await new PipelineSnapshotReader(scope.ServiceProvider.GetRequiredService<MonitoringDbContext>()).ReadAsync(cancellationToken);
        if (scope.ServiceProvider.GetService<IProjectionStatus>() is { } status)
        {
            try { _freshness = await status.CurrentAsync(cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _freshness = null; // unknown, not "current"
                logger.LogWarning("Search freshness could not be read ({FailureType}).", exception.GetType().Name);
            }
        }
        Register();
    }

    private void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        _meter.CreateObservableGauge<double>("monitoring.pipeline.projection_oldest_pending_seconds", () =>
            _snapshot?.OldestPendingSeconds is { } age ? [new Measurement<double>(age)] : []);
        Measurement<long>[] PerSlot(Func<ReplicationSlotStatus, long?> value) => (_snapshot?.Slots ?? [])
            .Select(slot => (slot, measurement: value(slot)))
            .Where(pair => pair.measurement.HasValue)
            .Select(pair => new Measurement<long>(pair.measurement!.Value, new KeyValuePair<string, object?>("slot", pair.slot.Name)))
            .ToArray();
        _meter.CreateObservableGauge("monitoring.pipeline.wal_slot_retained_bytes", () => PerSlot(slot => slot.RetainedBytes));
        _meter.CreateObservableGauge("monitoring.pipeline.wal_slot_confirm_lag_bytes", () => PerSlot(slot => slot.ConfirmLagBytes));
        _meter.CreateObservableGauge("monitoring.pipeline.wal_slot_active", () => PerSlot(slot => slot.Active ? 1 : 0));
        _meter.CreateObservableGauge<long>("monitoring.search.freshness_lag_seconds", () =>
            _freshness?.LagSeconds is { } lag ? [new Measurement<long>(lag)] : []);
        _meter.CreateObservableGauge<long>("monitoring.search.freshness_state", () =>
            _freshness is { } freshness ? [new Measurement<long>((long)freshness.State)] : []);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try { await RefreshAsync(stoppingToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning("Pipeline metrics refresh failed ({FailureType}).", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}

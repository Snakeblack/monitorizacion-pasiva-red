using System.Diagnostics.Metrics;
using Monitoring.Persistence.Ingestion;

namespace Monitoring.Host.Sessions;

// Publishes the reconcilable ingestion balance and the unresolved quarantine so an operator (and S14 alerting) can act on it.
// Values come from the database summary, so a state change is counted once no matter how often it is observed.
public sealed class QuarantineMetrics(IServiceScopeFactory scopeFactory, ILogger<QuarantineMetrics> logger, TimeSpan? interval = null)
    : BackgroundService, IDisposable
{
    public const string MeterName = "Monitoring.Ingestion";
    private readonly Meter meter = new(MeterName, "1.0.0");
    // The instance, so a listener can tell this service's instruments from those of another host running in the same process.
    public Meter Meter => meter;
    private volatile QuarantineSummary? latest;
    private bool registered;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        latest = await new QuarantineService(scope.ServiceProvider.GetRequiredService<Monitoring.Persistence.MonitoringDbContext>()).SummaryAsync(cancellationToken);
        Register();
    }

    private void Register()
    {
        if (registered) return;
        registered = true;
        meter.CreateObservableGauge("monitoring.ingestion.accepted_events", () => latest?.Accepted ?? 0);
        meter.CreateObservableGauge("monitoring.ingestion.processed_events", () => latest?.Processed ?? 0);
        meter.CreateObservableGauge("monitoring.ingestion.pending_events", () => latest?.Pending ?? 0);
        meter.CreateObservableGauge("monitoring.ingestion.quarantined_events", () => latest?.Quarantined ?? 0);
        meter.CreateObservableGauge<long>("monitoring.ingestion.quarantine_unresolved", () =>
            (latest?.UnresolvedByCause ?? new Dictionary<string, long>()).Select(pair => new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("cause", pair.Key))));
        meter.CreateObservableGauge<double>("monitoring.ingestion.quarantine_oldest_unresolved_seconds", () =>
            latest?.OldestUnresolvedAgeSeconds is { } age ? [new Measurement<double>(age)] : []);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try { await RefreshAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    // The previous values stay published; never log the exception itself.
                    logger.LogWarning("Ingestion balance refresh failed ({FailureType}).", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override void Dispose()
    {
        meter.Dispose();
        base.Dispose();
    }
}

using System.Diagnostics.Metrics;
using Monitoring.Persistence;
using Monitoring.Persistence.Retention;

namespace Monitoring.Host.Operations;

// Runs the retention pass on a schedule and publishes what it did. A failed pass changes nothing it did not finish (every batch is its
// own transaction), is logged by type only and is simply retried at the next interval; the age of the last success is what alerts.
public sealed class RetentionWorker(IServiceScopeFactory scopeFactory, RetentionOptions options, TimeProvider clock, ILogger<RetentionWorker> logger) : BackgroundService
{
    public const string MeterName = "Monitoring.Retention";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private Counter<long>? _removed;
    private long _lastSuccess = -1;

    public Meter Meter => _meter;

    public async Task<RetentionReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        EnsureInstruments();
        await using var scope = scopeFactory.CreateAsyncScope();
        var report = await new RetentionService(scope.ServiceProvider.GetRequiredService<MonitoringDbContext>(), options).RunAsync(cancellationToken);
        Add("sessions", report.ExpiredSessions);
        Add("observations", report.DeletedObservations);
        Add("inbox", report.DeletedInbox);
        Add("outbox", report.DeletedOutbox);
        Add("tombstones", report.PurgedTombstones);
        Interlocked.Exchange(ref _lastSuccess, clock.GetUtcNow().ToUnixTimeSeconds());
        return report;
    }

    private void Add(string kind, int count)
    {
        if (count > 0) _removed!.Add(count, new KeyValuePair<string, object?>("kind", kind));
    }

    private int _created;

    private void EnsureInstruments()
    {
        if (Interlocked.Exchange(ref _created, 1) == 1) return;
        _removed = _meter.CreateCounter<long>("monitoring.retention.removed", "{record}");
        // Seconds since the last complete pass, or absent before the first one (never 0, so a stuck job cannot look healthy).
        _meter.CreateObservableGauge<long>("monitoring.retention.seconds_since_last_run", () =>
            Interlocked.Read(ref _lastSuccess) is var last and >= 0 ? [new Measurement<long>(Math.Max(0, clock.GetUtcNow().ToUnixTimeSeconds() - last))] : []);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureInstruments();
        using var timer = new PeriodicTimer(options.RunInterval);
        try
        {
            do
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError("Retention pass failed ({FailureType}); it is retried at the next interval.", exception.GetType().Name);
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

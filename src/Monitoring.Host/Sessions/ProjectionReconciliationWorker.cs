using Monitoring.Persistence.Search;

namespace Monitoring.Host.Sessions;

// Periodically verifies the search projection against the authority. A failed sweep never stops the host: it is recorded as
// incomplete by the reconciler, so the reported freshness degrades to recovering instead of claiming currency.
public sealed class ProjectionReconciliationWorker(IServiceScopeFactory scopeFactory, ProjectionOptions options,
    ILogger<ProjectionReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<ProjectionReconciler>().RunAsync(stoppingToken);
                    if (!result.Complete || result.Missing + result.Stale > 0)
                        logger.LogWarning("Search projection check {Complete}: examined {Examined}, missing {Missing}, stale {Stale}.",
                            result.Complete, result.Examined, result.Missing, result.Stale);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Never log the exception itself: dependency messages may carry hosts or identifiers.
                    logger.LogError("Search projection check failed ({FailureType}).", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}

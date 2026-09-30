using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Host.Sessions;

public sealed class SessionProjectionWorker(IServiceScopeFactory scopeFactory, ILogger<SessionProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retrySeconds = 1;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var waitSeconds = 1;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SessionProjector>().RunPassAsync(stoppingToken);
                    retrySeconds = 1;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (NpgsqlException exception) when (exception.IsTransient)
                {
                    // Never pass the exception to logging: PostgreSQL details may contain accepted data.
                    logger.LogWarning("Session projection transient failure ({FailureType}); retrying.", exception.GetType().Name);
                    waitSeconds = retrySeconds;
                    retrySeconds = Math.Min(30, retrySeconds * 2);
                }
                catch (Exception exception)
                {
                    logger.LogCritical("Session projection non-transient failure ({FailureType}); stopping host.", exception.GetType().Name);
                    // BackgroundServiceExceptionBehavior.StopHost receives only a sanitized exception.
                    throw new InvalidOperationException($"Session projection stopped after a non-transient failure ({exception.GetType().Name}).");
                }
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation during the wait is normal host shutdown.
        }
    }
}

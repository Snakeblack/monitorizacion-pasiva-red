namespace Monitoring.Host.Sessions;

public sealed class TrustedSessionReadScope
{
    public string? SiteId { get; init; }
    public string? SensorId { get; init; }
}

public sealed class TrustedSessionReadScopeMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public Task Invoke(HttpContext context)
    {
        var scope = configuration.GetSection("TrustedSessionRead").Get<TrustedSessionReadScope>();
        if (scope is not null
            && !string.IsNullOrWhiteSpace(scope.SiteId)
            && !string.IsNullOrWhiteSpace(scope.SensorId))
        {
            context.Features.Set<ITrustedSessionReadContextFeature>(
                new ConfiguredFeature(new TrustedSessionReadContext(scope.SiteId, scope.SensorId)));
        }

        return next(context);
    }

    private sealed record ConfiguredFeature(TrustedSessionReadContext Context) : ITrustedSessionReadContextFeature;
}

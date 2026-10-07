using Monitoring.Domain.Sessions;

namespace Monitoring.Host.Sessions;

// Subject identifies the caller a cursor is bound to; the Development/Testing context has a single fixed one until real identity lands.
public sealed record TrustedSessionReadContext(string SiteId, string SensorId, string Subject = "trusted-server-context");

public interface ITrustedSessionReadContextFeature
{
    TrustedSessionReadContext Context { get; }
}

public interface ITrustedSessionReadContextProvider
{
    TrustedSessionReadContext? Resolve(HttpContext context);
}

public sealed class HostContextTrustedSessionReadContextProvider : ITrustedSessionReadContextProvider
{
    public TrustedSessionReadContext? Resolve(HttpContext context) =>
        context.Features.Get<ITrustedSessionReadContextFeature>()?.Context;
}

public interface ISessionReader
{
    Task<SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken);
}

public sealed class UnconfiguredSessionReader : ISessionReader
{
    public Task<SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken) =>
        Task.FromResult<SessionDetail?>(null);
}

using Monitoring.Domain.Sessions;
using Monitoring.Persistence.Sessions;

namespace Monitoring.Host.Sessions;

public static class SessionEndpoint
{
    public static void MapSessionEndpoint(this WebApplication app)
    {
        app.MapGet("/api/v1/sessions/{eventId}", async (
            string eventId, HttpContext httpContext, IHostEnvironment environment,
            ITrustedSessionReadContextProvider contextProvider, ISessionReader reader, CancellationToken cancellationToken) =>
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                return Results.Unauthorized();
            }
            var context = contextProvider.Resolve(httpContext);
            if (context is null)
            {
                return Results.Unauthorized();
            }
            var detail = await reader.FindAsync(context.SiteId, context.SensorId, eventId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}

internal sealed class PersistentSessionReader(SessionReader reader) : ISessionReader
{
    public Task<SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken) =>
        reader.FindAsync(siteId, sensorId, eventId, cancellationToken);
}

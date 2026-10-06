using Monitoring.Domain.Security;
using Monitoring.Domain.Sessions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Security;
using Monitoring.Persistence.Sessions;

namespace Monitoring.Host.Sessions;

public static class SessionEndpoint
{
    public static void MapSessionEndpoint(this WebApplication app)
    {
        app.MapGet("/api/v1/sessions/{eventId}", async (
            string eventId, HttpContext httpContext, IAccessProvider accessProvider, ISessionReader reader, CancellationToken cancellationToken) =>
        {
            var access = await accessProvider.AuthorizeAsync(httpContext, Operation.ReadSessions, cancellationToken);
            if (access.Outcome == AccessOutcome.Unauthenticated) return Results.Unauthorized();
            if (access.Outcome == AccessOutcome.Forbidden) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var scopes = access.Scopes!;
            AuthorizedPair selected;
            if (access.ClientSelectorsAreAdvisory)
            {
                // The development context is a single server-side pair; anything the client sends is ignored.
                selected = scopes.Single();
            }
            else
            {
                // Real identities may hold several scopes: the detail route names the one it addresses, and it must be one of theirs.
                var site = httpContext.Request.Query["siteId"].ToString();
                var sensor = httpContext.Request.Query["sensorId"].ToString();
                if (site.Length == 0 && sensor.Length == 0 && scopes.Count == 1) selected = scopes.Single();
                else if (site.Length == 0 || sensor.Length == 0) return Results.StatusCode(StatusCodes.Status400BadRequest);
                else if (!scopes.Contains(new AuthorizedPair(site, sensor))) return Results.StatusCode(StatusCodes.Status403Forbidden);
                else selected = new AuthorizedPair(site, sensor);
            }
            var detail = await reader.FindAsync(selected.SiteId, selected.SensorId, eventId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}

internal sealed class PersistentSessionReader(SessionReader reader) : ISessionReader
{
    public Task<SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken) =>
        reader.FindAsync(siteId, sensorId, eventId, cancellationToken);
}

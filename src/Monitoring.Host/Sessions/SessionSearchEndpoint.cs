using System.Globalization;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Host.Sessions;

public sealed class SessionSearchOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxConcurrentSearches { get; set; } = 20;
}

// Bounds in-flight searches for the whole process; saturation is rejected immediately instead of queueing.
internal sealed class SessionSearchGate(SessionSearchOptions options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.MaxConcurrentSearches, options.MaxConcurrentSearches);
    public bool TryEnter() => _slots.Wait(0);
    public void Exit() => _slots.Release();
    public void Dispose() => _slots.Dispose();
}

internal sealed class UnconfiguredSessionSearch : ISessionSearch
{
    public Task<SessionSearchPage> SearchAsync(SessionSearchRequest request, CancellationToken cancellationToken) =>
        throw new SessionSearchException(SessionSearchFailure.Unavailable);
}

public static class SessionSearchEndpoint
{
    public static void MapSessionSearchEndpoint(this WebApplication app)
    {
        app.MapGet("/api/v1/sessions", async (
            HttpContext httpContext, IHostEnvironment environment, ITrustedSessionReadContextProvider contextProvider,
            ISessionSearch search, SessionSearchGate gate, SessionSearchOptions options, SessionCursor cursors, TimeProvider time) =>
        {
            // Until the identity slice lands, only the Development/Testing server context may authorize a read.
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return Results.Unauthorized();
            var context = contextProvider.Resolve(httpContext);
            if (context is null) return Results.Unauthorized();
            var now = time.GetUtcNow();
            if (!SessionSearchQuery.TryParse(httpContext.Request.Query, now, out var query, out _))
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            // Client selectors may only narrow the authorized set; the narrowed set is applied inside the search itself.
            var scope = new List<AuthorizedPair> { new(context.SiteId, context.SensorId) }
                .Where(pair => (query!.SiteId is null || pair.SiteId == query.SiteId) && (query.SensorId is null || pair.SensorId == query.SensorId))
                .ToList();
            if (scope.Count == 0) return Results.StatusCode(StatusCodes.Status403Forbidden);
            // The cursor binds the full authorized set (current permissions), not just the narrowed one.
            var authorized = new List<AuthorizedPair> { new(context.SiteId, context.SensorId) };
            SessionSearchPosition? after = null;
            var expires = now + SessionCursor.Lifetime;
            if (query!.Cursor is not null
                && !cursors.TryOpen(query.Cursor, context.Subject, authorized, query.Fingerprint(), query.PageSize, now, out after, out expires, out var failure))
                return Results.StatusCode(failure switch
                {
                    CursorFailure.Expired => StatusCodes.Status410Gone,
                    CursorFailure.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status400BadRequest
                });

            if (!gate.TryEnter()) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
                timeout.CancelAfter(options.Timeout);
                var request = new SessionSearchRequest(query!.From, query.To, scope, query.SourceIp, query.DestinationIp,
                    query.Protocol, query.SourcePort, query.DestinationPort, query.PageSize, after);
                try
                {
                    var page = await search.SearchAsync(request, timeout.Token);
                    return Results.Ok(Present(page, page.Next is null ? null
                        : cursors.Issue(context.Subject, authorized, query.Fingerprint(), query.PageSize, page.Next, expires)));
                }
                catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
                }
                catch (SessionSearchException exception)
                {
                    return Results.StatusCode(exception.Failure switch
                    {
                        SessionSearchFailure.Saturated => StatusCodes.Status429TooManyRequests,
                        SessionSearchFailure.CursorExpired => StatusCodes.Status410Gone,
                        SessionSearchFailure.CursorInvalid => StatusCodes.Status400BadRequest,
                        SessionSearchFailure.ScopeChanged => StatusCodes.Status403Forbidden,
                        _ => StatusCodes.Status503ServiceUnavailable
                    });
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            }
            finally { gate.Exit(); }
        });
    }

    private static object Present(SessionSearchPage page, string? nextCursor) => new
    {
        items = page.Items,
        nextCursor,
        freshness = new
        {
            state = page.Freshness.State.ToString().ToLowerInvariant(),
            measuredAt = page.Freshness.MeasuredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            lagSeconds = page.Freshness.LagSeconds
        }
    };
}

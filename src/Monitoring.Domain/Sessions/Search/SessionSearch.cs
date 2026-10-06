namespace Monitoring.Domain.Sessions.Search;

// Application port for listing sessions. Implementations apply the scope inside the query, never after it.
public interface ISessionSearch
{
    Task<SessionSearchPage> SearchAsync(SessionSearchRequest request, CancellationToken cancellationToken);
}

public sealed record AuthorizedPair(string SiteId, string SensorId);

// Every filter is already validated and normalized (UTC instants, canonical IP text, upper-case protocol); the
// time range is [From,To) over the session start, and Scope is the authorized set already narrowed by client selectors.
public sealed record SessionSearchRequest(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<AuthorizedPair> Scope,
    string? SourceIp, string? DestinationIp, string? Protocol, int? SourcePort, int? DestinationPort, int PageSize, string? Cursor);

public sealed record SessionSearchItem(string EventId, string SiteId, string SensorId, string SourceIp, string DestinationIp,
    int SourcePort, int DestinationPort, string Protocol, string StartedAt, string EndedAt, string Provenance, bool? Inferred, bool? Partial);

public enum FreshnessState { Current, Lagging, Recovering }

// Unknown lag is null, never zero.
public sealed record SearchFreshness(FreshnessState State, DateTimeOffset MeasuredAt, long? LagSeconds);

public sealed record SessionSearchPage(IReadOnlyList<SessionSearchItem> Items, string? NextCursor, SearchFreshness Freshness);

public enum SessionSearchFailure { Unavailable, Saturated, CursorExpired, CursorInvalid, ScopeChanged }

public sealed class SessionSearchException(SessionSearchFailure failure, string? message = null) : Exception(message)
{
    public SessionSearchFailure Failure { get; } = failure;
}

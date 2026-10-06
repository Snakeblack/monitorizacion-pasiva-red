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
    string? SourceIp, string? DestinationIp, string? Protocol, int? SourcePort, int? DestinationPort, int PageSize,
    SessionSearchPosition? After, string Subject, DateTimeOffset SnapshotExpiresAt);

// Resume point inside one snapshot, in the total order (startedAt, document key). The host seals it into an opaque
// cursor; implementations only see this typed value and never client-supplied text.
public sealed record SessionSearchPosition(string SnapshotId, string StartedAt, string DocumentKey);

public sealed record SessionSearchItem(string EventId, string SiteId, string SensorId, string SourceIp, string DestinationIp,
    int SourcePort, int DestinationPort, string Protocol, string StartedAt, string EndedAt, string Provenance, bool? Inferred, bool? Partial);

public enum FreshnessState { Current, Lagging, Recovering }

// Unknown lag is null, never zero.
public sealed record SearchFreshness(FreshnessState State, DateTimeOffset MeasuredAt, long? LagSeconds);

public sealed record SessionSearchPage(IReadOnlyList<SessionSearchItem> Items, SessionSearchPosition? Next, SearchFreshness Freshness);

public enum SessionSearchFailure { Unavailable, Saturated, CursorExpired, CursorInvalid, ScopeChanged }

public sealed class SessionSearchException(SessionSearchFailure failure, string? message = null) : Exception(message)
{
    public SessionSearchFailure Failure { get; } = failure;
}

// Authority check applied to every page: only identities that are still active in PostgreSQL may be shown, so a lagging
// index or an open snapshot can never serve a suppressed or expired session. Keyed by the compact search identity.
public interface ISessionVisibility
{
    Task<IReadOnlySet<Guid>> VisibleAsync(IReadOnlyCollection<Guid> searchDocumentIds, CancellationToken cancellationToken);
}

// Bounds the open search snapshots per subject and globally. A lease lives until released or until the cursor deadline.
public interface ISnapshotLeases
{
    // Throws SessionSearchException(Saturated) when a limit would be exceeded.
    Task<Guid> AcquireAsync(string subject, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken);
    // Records the current PIT id (digest only) and any change of it; false when the lease was released or has expired.
    Task<bool> RecordPitAsync(Guid leaseId, string pitId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken);
}

// Freshness of the search projection for the response; unknown lag is null, never zero.
public interface IProjectionStatus
{
    Task<SearchFreshness> CurrentAsync(CancellationToken cancellationToken);
}

public sealed record IndexedDocument(long Revision, string Operation);

// Read side of a search index used to verify it against the authority; a null index is the read alias. Values are what is
// actually visible after a refresh.
public interface IProjectionIndex
{
    Task RefreshAsync(string? index, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IndexedDocument>> GetAsync(string? index, IReadOnlyCollection<Guid> searchDocumentIds, CancellationToken cancellationToken);
    Task<long> CountAsync(string? index, CancellationToken cancellationToken);
}

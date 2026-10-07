using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Persistence.Search;

// Until a verifiable freshness checkpoint exists the honest answer is recovering with unknown lag, never "current".
public sealed class UnknownProjectionStatus(TimeProvider time) : IProjectionStatus
{
    public Task<SearchFreshness> CurrentAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SearchFreshness(FreshnessState.Recovering, time.GetUtcNow(), null));
}

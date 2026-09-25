using Monitoring.Domain.Ingestion;

namespace Monitoring.Host.Ingestion;

public sealed record TrustedSensorIdentity(string SiteId, string SensorId);

public interface ITrustedSensorIdentityFeature
{
    TrustedSensorIdentity Identity { get; }
}

public interface ITrustedSensorIdentityProvider
{
    TrustedSensorIdentity? Resolve(HttpContext context);
}

public sealed class HostContextTrustedSensorIdentityProvider : ITrustedSensorIdentityProvider
{
    public TrustedSensorIdentity? Resolve(HttpContext context) =>
        context.Features.Get<ITrustedSensorIdentityFeature>()?.Identity;
}

public enum IngestionWriteResult
{
    Accepted,
    Conflict,
    RateLimited,
    Failed
}

public interface IIngestionBatchWriter
{
    Task<IngestionWriteResult> WriteAsync(
        TrustedSensorIdentity identity,
        IngestionBatch batch,
        CancellationToken cancellationToken);
}

public sealed class UnconfiguredIngestionBatchWriter : IIngestionBatchWriter
{
    public Task<IngestionWriteResult> WriteAsync(
        TrustedSensorIdentity identity,
        IngestionBatch batch,
        CancellationToken cancellationToken) =>
        Task.FromResult(IngestionWriteResult.Failed);
}

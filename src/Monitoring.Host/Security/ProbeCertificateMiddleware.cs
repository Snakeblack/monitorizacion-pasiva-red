using System.Diagnostics.Metrics;
using Monitoring.Host.Ingestion;

namespace Monitoring.Host.Security;

public sealed class ProbeAuthMetrics : IDisposable
{
    public const string MeterName = "Monitoring.Probes";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _rejections;

    public ProbeAuthMetrics() => _rejections = _meter.CreateCounter<long>("monitoring.probe.auth_rejections", "{request}");

    public Meter Meter => _meter;

    public void RecordRejection(string cause) => _rejections.Add(1, new KeyValuePair<string, object?>("cause", cause));

    public void Dispose() => _meter.Dispose();
}

// Authenticates the ingestion route with the client certificate of the TLS connection. The sensor identity it sets comes only from
// the registry binding of a validated certificate; whatever identity was on the request before is discarded first, and a request
// that is not on TLS, has no certificate or fails validation is answered 401 here and never reaches the endpoint.
public sealed class ProbeCertificateMiddleware(RequestDelegate next, ProbeAuthMetrics metrics, ILogger<ProbeCertificateMiddleware> logger)
{
    private sealed class Feature(TrustedSensorIdentity identity) : ITrustedSensorIdentityFeature
    {
        public TrustedSensorIdentity Identity { get; } = identity;
    }

    public async Task InvokeAsync(HttpContext context, ProbeCertificateValidator validator)
    {
        if (!context.Request.Path.Equals(BatchEndpoint.Route, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }
        context.Features.Set<ITrustedSensorIdentityFeature>(null);
        var result = context.Request.IsHttps
            ? await validator.ValidateAsync(await context.Connection.GetClientCertificateAsync(context.RequestAborted), context.RequestAborted)
            : ProbeIdentityResult.Rejected("plaintext");
        if (result.Identity is null)
        {
            metrics.RecordRejection(result.Cause!);
            // Cause only: no subject, serial, address or certificate content is logged.
            logger.LogWarning("Probe authentication refused: {Cause}.", result.Cause);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        context.Features.Set<ITrustedSensorIdentityFeature>(new Feature(result.Identity));
        await next(context);
    }
}

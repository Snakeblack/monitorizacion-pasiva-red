using System.Diagnostics.Metrics;
using Monitoring.Domain.Ingestion;

namespace Monitoring.Host.Ingestion;

public static class BatchEndpoint
{
    public const string Route = "/api/v1/ingestion/batches";

    public static RouteHandlerBuilder MapBatchEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Route, HandleAsync);

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ITrustedSensorIdentityProvider identityProvider,
        IIngestionBatchWriter writer,
        IngestionRejectionMetrics rejectionMetrics)
    {
        var identity = identityProvider.Resolve(context);
        if (identity is null)
        {
            rejectionMetrics.RecordAnonymousRejection();
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var requestBody = await ReadBoundedBodyAsync(context.Request, context.RequestAborted);
        if (requestBody is null || !BatchContract.TryParse(requestBody, out var batch))
        {
            rejectionMetrics.RecordTrustedRejection(identity);
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(identity.SiteId, batch!.SiteId, StringComparison.Ordinal)
            || !string.Equals(identity.SensorId, batch.SensorId, StringComparison.Ordinal))
        {
            rejectionMetrics.RecordTrustedRejection(identity);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await writer.WriteAsync(identity, batch, context.RequestAborted);
        if (result is IngestionWriteResult.Conflict or IngestionWriteResult.RateLimited)
        {
            rejectionMetrics.RecordTrustedRejection(identity);
        }

        return result switch
        {
            IngestionWriteResult.Accepted => Results.StatusCode(StatusCodes.Status200OK),
            IngestionWriteResult.Conflict => Results.StatusCode(StatusCodes.Status409Conflict),
            IngestionWriteResult.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            IngestionWriteResult.Failed => Results.StatusCode(StatusCodes.Status500InternalServerError),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > BatchContract.MaximumBodyBytes)
        {
            return null;
        }

        using var body = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await request.Body.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                return body.ToArray();
            }

            if (body.Length + bytesRead > BatchContract.MaximumBodyBytes)
            {
                return null;
            }

            await body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }
}

public sealed class IngestionRejectionMetrics : IDisposable
{
    private readonly Meter meter = new("Monitoring.Host.Ingestion", "1.0.0");
    private readonly Counter<long> rejections;

    public IngestionRejectionMetrics()
    {
        rejections = meter.CreateCounter<long>("monitoring.ingestion.rejections", "{request}");
    }

    public Meter Meter => meter;

    public void RecordTrustedRejection(TrustedSensorIdentity identity) =>
        rejections.Add(1,
            new KeyValuePair<string, object?>("site.id", identity.SiteId),
            new KeyValuePair<string, object?>("sensor.id", identity.SensorId));

    public void RecordAnonymousRejection() => rejections.Add(1);

    public void Dispose() => meter.Dispose();
}

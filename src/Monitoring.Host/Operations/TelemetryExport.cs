using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Monitoring.Host.Operations;

// The product's meters (Monitoring.*) leave the process over OTLP/HTTP, the vendor-neutral protocol of ADR-011/ADR-021, and only when an
// endpoint is configured: with none nothing is exported and no connection is attempted. An endpoint that is not an http(s) URL stops the
// host at start-up, because a typo would otherwise silently disable every alert that depends on these metrics.
public static class TelemetryExport
{
    public const string EndpointSetting = "Telemetry:Otlp:Endpoint";

    public static IServiceCollection AddMonitoringTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration[EndpointSetting];
        if (string.IsNullOrWhiteSpace(configured)) return services;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{EndpointSetting} must be an absolute http(s) URL.");
        var interval = configuration.GetValue("Telemetry:Otlp:ExportIntervalSeconds", 15);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("monitoring-api"))
            .WithMetrics(metrics => metrics
                .AddMeter("Monitoring.*")
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = interval * 1000;
                }));
        return services;
    }
}

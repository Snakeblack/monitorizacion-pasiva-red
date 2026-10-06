namespace Monitoring.Probe;
public sealed record ProbeConfiguration(ProbeScope Scope, CaptureOptions Capture, CorrelationOptions Correlation,
    SpoolOptions Spool, string SpoolPath, string? Endpoint = null, decimal? SustainedEventsPerSecond = null,
    decimal? MeasuredBytesPerEventIncludingOverhead = null, decimal Margin = .25m, string? ClientCertificatePem = null,
    string? ClientKeyPem = null)
{
    public void Validate()
    {
        ProbeJson.ValidateScope(Scope);
        if (string.IsNullOrWhiteSpace(SpoolPath) || Margin < .25m) throw new ArgumentException("Invalid spool configuration.");
        if (Capture.FixturePcap is null && (SustainedEventsPerSecond is null || MeasuredBytesPerEventIncludingOverhead is null || Endpoint is null))
            throw new ArgumentException("Live capture requires an endpoint and measured sizing inputs.");
        if (Endpoint is not null && (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new ArgumentException("Probe endpoint must use HTTPS.");
        if ((ClientCertificatePem is null) != (ClientKeyPem is null)) throw new ArgumentException("Certificate and key must be configured together.");
        if (SustainedEventsPerSecond is { } rate && MeasuredBytesPerEventIncludingOverhead is { } size)
        {
            var required = SpoolSizing.RequiredBytes(rate, size, Margin);
            if (Spool.MaximumDiskBytes < required || Spool.MaximumPayloadBytes < decimal.Ceiling((14400m * rate + 3600m * rate) * size * (1 + Margin))
                || Spool.MaximumEvents < decimal.Ceiling(18000m * rate)) throw new ArgumentException("Spool quota cannot cover configured outage and burst.");
        }
    }
}

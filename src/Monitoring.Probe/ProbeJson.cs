using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Monitoring.Probe;

public sealed record ProbeBatch(int SchemaVersion, string BatchId, string SiteId, string SensorId, IReadOnlyList<ProbeEvent> Events);
public static class ProbeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static void ValidateScope(ProbeScope scope)
    {
        if (string.IsNullOrEmpty(scope.SiteId) || scope.SiteId.Length > 128 || string.IsNullOrEmpty(scope.SensorId) || scope.SensorId.Length > 128)
            throw new ArgumentException("Invalid probe scope.");
    }
    public static byte[] Batch(ProbeScope scope, string batchId, IReadOnlyList<ProbeEvent> events) =>
        JsonSerializer.SerializeToUtf8Bytes(new ProbeBatch(1, batchId, scope.SiteId, scope.SensorId, events), Options);
    public static void Validate(ProbeEvent value)
    {
        var d = value.Data;
        if (string.IsNullOrEmpty(value.EventId) || value.EventId.Length > 128 || d.Kind != "captured-session" || d.Version != 1
            || d.CorrelationVersion != 1 || !d.Inferred || !IPAddress.TryParse(d.SourceIp, out _) || !IPAddress.TryParse(d.DestinationIp, out _)
            || d.SourcePort is < 0 or > 65535 || d.DestinationPort is < 0 or > 65535 || d.Protocol is not ("TCP" or "UDP")
            || d.VlanId is < 0 or > 4094 || d.PacketCount < 0 || d.ByteCount < 0
            || d.CloseReason is not ("inactivity" or "max-duration" or "shutdown" or "restart")
            || !ParseTime(d.StartedAt, out var start) || !ParseTime(d.EndedAt, out var end) || end < start || !ParseTime(value.OccurredAt, out _))
            throw new ArgumentException("Invalid captured session v1.");
    }
    private static bool ParseTime(string value, out DateTimeOffset time) => DateTimeOffset.TryParseExact(value,
        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.f'Z'", "yyyy-MM-dd'T'HH:mm:ss.ff'Z'", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"],
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);
}

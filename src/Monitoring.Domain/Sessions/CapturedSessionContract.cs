using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Monitoring.Domain.Sessions;

public sealed record CapturedSessionData(JsonElement Data);

public static class CapturedSessionContract
{
    private static readonly HashSet<string> Fields = ["kind", "version", "sourceIp", "destinationIp", "sourcePort",
        "destinationPort", "protocol", "startedAt", "endedAt", "vlanId", "correlationVersion", "inferred", "partial",
        "closeReason", "packetCount", "byteCount"];
    private static readonly string[] TimestampFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.f'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ff'Z'", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"];

    public static bool TryParse(JsonElement data, out CapturedSessionData? session)
    {
        session = null;
        if (data.ValueKind != JsonValueKind.Object) return false;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
            if (!Fields.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value)) return false;
        if (fields.Count != Fields.Count || Text(fields["kind"]) != "captured-session"
            || !Integer(fields["version"], 1, 1) || !Integer(fields["correlationVersion"], 1, 1)
            || !IPAddress.TryParse(Text(fields["sourceIp"]), out _) || !IPAddress.TryParse(Text(fields["destinationIp"]), out _)
            || !Integer(fields["sourcePort"], 0, 65535) || !Integer(fields["destinationPort"], 0, 65535)
            || Text(fields["protocol"]) is not ("TCP" or "UDP")
            || !Timestamp(fields["startedAt"], out var start) || !Timestamp(fields["endedAt"], out var end) || end < start
            || !(fields["vlanId"].ValueKind == JsonValueKind.Null || Integer(fields["vlanId"], 0, 4094))
            || fields["inferred"].ValueKind != JsonValueKind.True
            || fields["partial"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Text(fields["closeReason"]) is not ("inactivity" or "max-duration" or "shutdown" or "restart")
            || !Integer(fields["packetCount"], 0, long.MaxValue) || !Integer(fields["byteCount"], 0, long.MaxValue)) return false;
        session = new CapturedSessionData(data.Clone());
        return true;
    }

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Integer(JsonElement value, long min, long max) => value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number) && number >= min && number <= max;
    private static bool Timestamp(JsonElement value, out DateTimeOffset timestamp) => DateTimeOffset.TryParseExact(
        Text(value), TimestampFormats, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
}

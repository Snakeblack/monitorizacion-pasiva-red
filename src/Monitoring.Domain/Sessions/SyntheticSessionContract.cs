using System.Text.Json;
using System.Globalization;
using System.Net;

namespace Monitoring.Domain.Sessions;

public sealed record SyntheticSessionData(JsonElement Data);

public static class SyntheticSessionContract
{
    private static readonly HashSet<string> Fields =
        ["kind", "version", "sourceIp", "destinationIp", "sourcePort", "destinationPort", "protocol", "startedAt", "endedAt"];
    private static readonly string[] TimestampFormats =
        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.f'Z'", "yyyy-MM-dd'T'HH:mm:ss.ff'Z'", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"];

    public static bool TryParse(JsonElement data, out SyntheticSessionData? session)
    {
        session = null;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (!Fields.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value))
            {
                return false;
            }
        }

        if (fields.Count != Fields.Count
            || Text(fields["kind"]) != "synthetic-session"
            || !Integer(fields["version"], 1, 1)
            || !IPAddress.TryParse(Text(fields["sourceIp"]), out _)
            || !IPAddress.TryParse(Text(fields["destinationIp"]), out _)
            || !Integer(fields["sourcePort"], 0, 65535)
            || !Integer(fields["destinationPort"], 0, 65535)
            || Text(fields["protocol"]) is not ("TCP" or "UDP")
            || !Timestamp(fields["startedAt"], out var startedAt)
            || !Timestamp(fields["endedAt"], out var endedAt)
            || endedAt < startedAt)
        {
            return false;
        }

        session = new SyntheticSessionData(data.Clone());
        return true;
    }

    private static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Integer(JsonElement value, int minimum, int maximum) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
        && number >= minimum && number <= maximum;

    private static bool Timestamp(JsonElement value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(Text(value), TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
}

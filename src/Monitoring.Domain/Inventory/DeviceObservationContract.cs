using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Monitoring.Domain.Inventory;

// Mac is normalized lower-case colon-separated, or null when the observation carries none; VlanId null means untagged.
public sealed record DeviceObservation(DateTimeOffset ObservedAt, IPAddress Ip, string? Mac, int? VlanId);

// `contrato-ingestion-v1` data of kind `device-observation` version 1: exactly kind, version, observedAt, ip, mac and vlanId.
public static partial class DeviceObservationContract
{
    public const string Kind = "device-observation";
    private static readonly HashSet<string> Fields = ["kind", "version", "observedAt", "ip", "mac", "vlanId"];
    private static readonly string[] TimestampFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.f'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ff'Z'", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"];

    [GeneratedRegex(@"^[0-9A-Fa-f]{2}([:-])[0-9A-Fa-f]{2}(\1[0-9A-Fa-f]{2}){4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacPattern();

    [GeneratedRegex(@"^(0|[1-9]\d{0,2})(\.(0|[1-9]\d{0,2})){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedQuad();

    public static bool TryParse(JsonElement data, out DeviceObservation? observation)
    {
        observation = null;
        if (data.ValueKind != JsonValueKind.Object) return false;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
            if (!Fields.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value)) return false;
        if (fields.Count != Fields.Count || Text(fields["kind"]) != Kind || !Integer(fields["version"], 1, 1)
            || !DateTimeOffset.TryParseExact(Text(fields["observedAt"]), TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var observedAt)
            || !TryAddress(Text(fields["ip"]), out var ip)) return false;
        string? mac = null;
        if (fields["mac"].ValueKind != JsonValueKind.Null && !TryUnicastMac(Text(fields["mac"]), out mac)) return false;
        int? vlan = null;
        if (fields["vlanId"].ValueKind != JsonValueKind.Null)
        {
            if (!Integer(fields["vlanId"], 0, 4094)) return false;
            vlan = fields["vlanId"].GetInt32();
        }
        observation = new DeviceObservation(observedAt, ip!, mac, vlan);
        return true;
    }

    // Literal addresses only, as in the query parser: no legacy short IPv4 forms, no zone identifiers.
    private static bool TryAddress(string? text, out IPAddress? address)
    {
        address = null;
        if (text is null) return false;
        var literal = text.Contains(':') ? !text.Contains('%') : DottedQuad().IsMatch(text);
        return literal && IPAddress.TryParse(text, out address);
    }

    // A 48-bit unicast address: the group bit of the first octet is clear and the address is not all zeros.
    private static bool TryUnicastMac(string? text, out string? normalized)
    {
        normalized = null;
        if (text is null || !MacPattern().IsMatch(text)) return false;
        var octets = text.Split(':', '-');
        if ((Convert.ToInt32(octets[0], 16) & 1) != 0 || octets.All(octet => octet == "00" || octet == "0")) return false;
        normalized = string.Join(':', octets.Select(octet => octet.ToLowerInvariant()));
        return true;
    }

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Integer(JsonElement value, long minimum, long maximum) => value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number) && number >= minimum && number <= maximum;
}

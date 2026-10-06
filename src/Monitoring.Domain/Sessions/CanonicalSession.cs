using System.Net;
using System.Text.Json;
using System.Globalization;

namespace Monitoring.Domain.Sessions;

public sealed record CanonicalSession(JsonElement Data, IPAddress SourceIp, IPAddress DestinationIp,
    int SourcePort, int DestinationPort, string Protocol, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
    int? VlanId, string Provenance, bool? Inferred, bool? Partial, string? CloseReason,
    long? PacketCount, long? ByteCount)
{
    public static bool TryParse(JsonElement data, out CanonicalSession? session)
    {
        session = null;
        if (SyntheticSessionContract.TryParse(data, out _))
        {
            session = Create(data, "synthetic");
            return true;
        }
        if (!CapturedSessionContract.TryParse(data, out _)) return false;
        session = Create(data, "capture") with
        {
            VlanId = data.GetProperty("vlanId").ValueKind == JsonValueKind.Null ? null : data.GetProperty("vlanId").GetInt32(),
            Inferred = true, Partial = data.GetProperty("partial").GetBoolean(),
            CloseReason = data.GetProperty("closeReason").GetString(),
            PacketCount = data.GetProperty("packetCount").GetInt64(), ByteCount = data.GetProperty("byteCount").GetInt64()
        };
        return true;
    }

    private static CanonicalSession Create(JsonElement data, string provenance) => new(data.Clone(),
        IPAddress.Parse(data.GetProperty("sourceIp").GetString()!),
        IPAddress.Parse(data.GetProperty("destinationIp").GetString()!),
        data.GetProperty("sourcePort").GetInt32(), data.GetProperty("destinationPort").GetInt32(),
        data.GetProperty("protocol").GetString()!,
        DateTimeOffset.Parse(data.GetProperty("startedAt").GetString()!, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(data.GetProperty("endedAt").GetString()!, CultureInfo.InvariantCulture),
        null, provenance, null, null, null, null, null);
}

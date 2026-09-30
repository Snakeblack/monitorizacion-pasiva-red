using System.Text.Json;
using System.Text.Json.Nodes;
using Monitoring.Domain.Sessions;

namespace Monitoring.Tests;

public sealed class SyntheticSessionContractTests
{
    internal const string ValidData = """
        {"kind":"synthetic-session","version":1,"sourceIp":"192.0.2.1","destinationIp":"2001:db8::2","sourcePort":0,"destinationPort":65535,"protocol":"TCP","startedAt":"2026-09-29T12:00:00Z","endedAt":"2026-09-29T12:00:00.123Z"}
        """;

    [Theory]
    [InlineData("TCP", "", "192.0.2.1", "2001:db8::2", 0, 65535)]
    [InlineData("UDP", ".1", "2001:db8::1", "192.0.2.2", 65535, 0)]
    [InlineData("TCP", ".12", "192.0.2.1", "192.0.2.2", 7, 8)]
    [InlineData("UDP", ".123", "2001:db8::1", "2001:db8::2", 53, 53)]
    public void ValidSessionsPreserveOriginalValues(string protocol, string fraction, string source, string destination, int sourcePort, int destinationPort)
    {
        var data = JsonNode.Parse(ValidData)!;
        data["protocol"] = protocol;
        data["sourceIp"] = source;
        data["destinationIp"] = destination;
        data["sourcePort"] = sourcePort;
        data["destinationPort"] = destinationPort;
        data["startedAt"] = $"2026-09-29T12:00:00{fraction}Z";
        data["endedAt"] = $"2026-09-29T12:00:00{fraction}Z";
        var input = JsonSerializer.SerializeToElement(data);
        Assert.True(SyntheticSessionContract.TryParse(input, out var session));
        Assert.Equal(input.GetRawText(), session!.Data.GetRawText());
    }

    public static IEnumerable<object[]> InvalidData()
    {
        var original = JsonNode.Parse(ValidData)!.AsObject();
        foreach (var field in original.Select(pair => pair.Key).ToArray())
        {
            var missing = original.DeepClone().AsObject();
            missing.Remove(field);
            yield return [missing.ToJsonString()];
            var wrongType = original.DeepClone();
            wrongType[field] = field is "version" or "sourcePort" or "destinationPort" ? JsonValue.Create("1") : JsonValue.Create(1);
            yield return [wrongType.ToJsonString()];
        }
        var extra = original.DeepClone();
        extra["vlan"] = 3;
        yield return [extra.ToJsonString()];
        foreach (var (field, value) in new (string, JsonNode?)[]
        {
            ("kind", JsonValue.Create("other")), ("version", JsonValue.Create(2)),
            ("sourceIp", JsonValue.Create("not-ip")), ("destinationIp", JsonValue.Create("999.1.1.1")),
            ("sourcePort", JsonValue.Create(-1)), ("destinationPort", JsonValue.Create(65536)),
            ("sourcePort", JsonValue.Create(1.5)), ("protocol", JsonValue.Create("tcp")),
            ("startedAt", JsonValue.Create("2026-09-29T12:00:00+00:00")),
            ("endedAt", JsonValue.Create("2026-09-29T12:00:00.1234Z")),
            ("endedAt", JsonValue.Create("2026-09-29T11:59:59Z")), ("sourceIp", null)
        })
        {
            var invalid = original.DeepClone();
            invalid[field] = value;
            yield return [invalid.ToJsonString()];
        }
        yield return [ValidData.Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal)];
        yield return ["[]"];
        yield return ["null"];
    }

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void UnknownOrInvalidSessionsRemainUnrecognized(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(SyntheticSessionContract.TryParse(document.RootElement, out var session));
        Assert.Null(session);
    }
}

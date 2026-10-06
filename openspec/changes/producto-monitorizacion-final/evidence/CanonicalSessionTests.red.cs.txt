using System.Text.Json;
using System.Text.Json.Nodes;
using Monitoring.Domain.Sessions;

namespace Monitoring.Tests;

public sealed class CanonicalSessionTests
{
    internal const string Captured = """
        {"kind":"captured-session","version":1,"sourceIp":"2001:0db8:0000:0000:0000:0000:0000:0001","destinationIp":"192.0.2.2","sourcePort":443,"destinationPort":50000,"protocol":"TCP","startedAt":"2026-09-29T12:00:00.100Z","endedAt":"2026-09-29T12:00:01Z","vlanId":null,"correlationVersion":1,"inferred":true,"partial":true,"closeReason":"restart","packetCount":12,"byteCount":1024}
        """;

    [Fact]
    public void CapturedContractNormalizesMetadataWithoutChangingAcceptedData()
    {
        using var doc = JsonDocument.Parse(Captured);
        Assert.True(CanonicalSession.TryParse(doc.RootElement, out var session));
        Assert.Equal("2001:db8::1", session!.SourceIp.ToString());
        Assert.Equal("capture", session.Provenance);
        Assert.True(session.Inferred);
        Assert.True(session.Partial);
        Assert.Equal(12L, session.PacketCount);
        Assert.Null(session.VlanId);
        Assert.Equal(doc.RootElement.GetRawText(), session.Data.GetRawText());
    }

    [Fact]
    public void HistoricalSyntheticHasNoInventedCaptureMetadata()
    {
        using var doc = JsonDocument.Parse(SyntheticSessionContractTests.ValidData);
        Assert.True(CanonicalSession.TryParse(doc.RootElement, out var session));
        Assert.Equal("synthetic", session!.Provenance);
        Assert.Null(session.Inferred);
        Assert.Null(session.Partial);
        Assert.Null(session.PacketCount);
        Assert.Null(session.ByteCount);
        Assert.Null(session.VlanId);
        Assert.Equal(65535, session.DestinationPort);
    }

    [Theory]
    [InlineData("version", "2")]
    [InlineData("packetCount", "-1")]
    [InlineData("byteCount", "1.1")]
    [InlineData("vlanId", "4095")]
    [InlineData("inferred", "false")]
    [InlineData("partial", "1")]
    [InlineData("extra", "true")]
    [InlineData("endedAt", "\"2026-09-29T11:00:00Z\"")]
    [InlineData("sourceIp", "\"invalid\"")]
    [InlineData("startedAt", "\"2026-09-29T12:00:00.1001Z\"")]
    [InlineData("closeReason", "\"unknown\"")]
    public void InvalidCapturedContractIsRejected(string field, string value)
    {
        var input = JsonNode.Parse(Captured)!.AsObject();
        input[field] = JsonNode.Parse(value);
        Assert.False(CanonicalSession.TryParse(JsonSerializer.SerializeToElement(input), out _));
    }

    [Fact]
    public void DocumentKeysAreUnambiguousAndIncludeAllIdentityComponents()
    {
        Assert.Equal("c2l0ZQ.c2Vuc29y.ZXZlbnQ", new SessionIdentity("site", "sensor", "event").DocumentKey);
        Assert.NotEqual(new SessionIdentity("a.b", "c", "d").DocumentKey,
            new SessionIdentity("a", "b.c", "d").DocumentKey);
        Assert.NotEqual(new SessionIdentity("site", "sensor", "event").DocumentKey,
            new SessionIdentity("site", "sensor-2", "event").DocumentKey);
    }
}

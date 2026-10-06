using Monitoring.Probe;

namespace Monitoring.Probe.Tests;

public sealed class CaptureTests
{
    internal static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
    internal static PacketMetadata Packet(string protocol = "TCP", int? vlan = null, int seconds = 0, bool reverse = false,
        bool syn = false, bool ack = false) => new(Start.AddSeconds(seconds), 64,
            reverse ? "10.0.0.2" : "10.0.0.1", reverse ? "10.0.0.1" : "10.0.0.2",
            reverse ? 443 : 12345, reverse ? 12345 : 443, protocol, vlan, syn, ack);
    internal static string Line(params string[] values) => string.Join('\t', values);

    [Fact]
    public void ParserExtractsIpv6VlanAndMillisecondsWithoutLosingValidLines()
    {
        var parser = new TsharkParser();
        Assert.Null(parser.Parse("invalid"));
        var packet = parser.Parse(Line("1791194400.123456789", "80", "00:11:22:33:44:55", "", "", "",
            "2001:0db8:0:0:0:0:0:1", "2001:db8::2", "", "17", "", "", "53", "123", "42", "", "", "", ""));
        Assert.Equal("2001:db8::1", packet!.SourceIp);
        Assert.Equal(42, packet.VlanId);
        Assert.Equal(53, packet.SourcePort);
        Assert.Equal(Start.AddMilliseconds(123), packet.Timestamp);
        Assert.Equal(1, parser.ParserErrors);
        Assert.Equal(2, parser.PacketsSeen);
    }

    [Theory]
    [InlineData("-1", "17", "53", "123", "", "10.0.0.1")]
    [InlineData("64", "17", "65536", "123", "", "10.0.0.1")]
    [InlineData("64", "17", "53", "123", "4095", "10.0.0.1")]
    [InlineData("64", "17", "53", "123", "", "bad-ip")]
    public void ParserRejectsInvalidMetadata(string length, string proto, string sourcePort, string destinationPort, string vlan, string source)
    {
        var parser = new TsharkParser();
        Assert.Null(parser.Parse(Line("1791194400", length, "", "", source, "10.0.0.2", "", "", proto, "",
            "", "", sourcePort, destinationPort, vlan, "", "", "", "")));
        Assert.Equal(1, parser.ParserErrors);
    }

    [Fact]
    public void InversePacketsShareOnlySameScopeAndVlanAndFirstDirection()
    {
        var correlator = new FlowCorrelator(new("madrid", "probe-1"), new());
        correlator.Accept(Packet(syn: true));
        correlator.Accept(Packet(seconds: 1, reverse: true, syn: true, ack: true));
        correlator.Accept(Packet(vlan: 42, seconds: 2));
        var events = correlator.Close("shutdown");
        Assert.Equal(2, events.Count);
        var untagged = Assert.Single(events.Where(e => e.Data.VlanId is null));
        Assert.Equal("10.0.0.1", untagged.Data.SourceIp);
        Assert.Equal(2, untagged.Data.PacketCount);
        Assert.Equal(128, untagged.Data.ByteCount);
        Assert.False(untagged.Data.Partial);
        Assert.True(untagged.Data.Inferred);
        var other = new FlowCorrelator(new("madrid", "probe-2"), new());
        other.Accept(Packet());
        Assert.NotEqual(untagged.EventId, Assert.Single(other.Close("shutdown")).EventId);
    }

    [Theory]
    [InlineData("TCP", 299, 300)]
    [InlineData("UDP", 59, 60)]
    public void ExactIdleBoundaryClosesAndNextFlowGetsNewIdentity(string protocol, int before, int boundary)
    {
        var c = new FlowCorrelator(new("s", "p"), new());
        c.Accept(Packet(protocol));
        Assert.Empty(c.Expire(Start.AddSeconds(before)));
        var closed = Assert.Single(c.Expire(Start.AddSeconds(boundary)));
        Assert.Equal("inactivity", closed.Data.CloseReason);
        Assert.True(closed.Data.Partial);
        c.Accept(Packet(protocol, seconds: boundary));
        Assert.NotEqual(closed.EventId, Assert.Single(c.Close("shutdown")).EventId);
    }

    [Fact]
    public void MaximumDurationWinsEvenWithRecentPacketsAndRestartPreservesIdentity()
    {
        var c = new FlowCorrelator(new("s", "p"), new());
        c.Accept(Packet());
        c.Accept(Packet(seconds: 3599));
        // A gap closes the first flow; maintain a fresh flow for the duration case.
        c = new(new("s", "p"), new(TcpIdle: TimeSpan.FromHours(2)));
        c.Accept(Packet());
        c.Accept(Packet(seconds: 3599));
        Assert.Equal("max-duration", Assert.Single(c.Expire(Start.AddHours(1))).Data.CloseReason);
        c.Accept(Packet(seconds: 3601));
        var snapshot = c.Snapshot();
        var restarted = new FlowCorrelator(new("s", "p"), new());
        restarted.Restore(snapshot);
        var closed = Assert.Single(restarted.Close("restart"));
        Assert.Equal(snapshot.Single().EventId, closed.EventId);
        Assert.True(closed.Data.Partial);
        Assert.Equal("restart", closed.Data.CloseReason);
    }

    [Fact]
    public void SaturationIsCountedAndOtherProtocolsAreObservations()
    {
        var c = new FlowCorrelator(new("s", "p"), new(MaximumFlows: 1));
        c.Accept(Packet());
        c.Accept(Packet(vlan: 1));
        c.Accept(Packet("ICMP"));
        Assert.Equal(1, c.ActiveFlows);
        Assert.Equal(1, c.DroppedPackets);
        Assert.Equal(1, c.Observations);
        Assert.Equal(1, Assert.Single(c.Close("shutdown")).Data.PacketCount);
    }
}

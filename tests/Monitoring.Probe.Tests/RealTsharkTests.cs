using System.Diagnostics;
using Monitoring.Probe;

namespace Monitoring.Probe.Tests;

// Runs the real extractor (tshark) over a synthetic PCAP. It proves the field list, separators and boolean spelling the
// parser relies on against the tool's genuine output, which hand-written lines cannot. Install tshark to run it; setting
// MONITORING_SKIP_TSHARK_TESTS=1 is the only way to opt out, so a missing tool never passes silently.
public sealed class RealTsharkTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mixed.pcap");

    private static bool OptedOut => Environment.GetEnvironmentVariable("MONITORING_SKIP_TSHARK_TESTS") == "1";

    private static bool TsharkAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("tshark", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task<List<string>> ReadAsync(CaptureMetrics metrics)
    {
        var lines = new List<string>();
        var capture = new TsharkCapture(new CaptureOptions("unused", FixturePcap: Fixture), metrics);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var line in capture.ReadLinesAsync(timeout.Token)) lines.Add(line);
        return lines;
    }

    [Fact]
    public async Task RealTsharkOutputIsParsedCorrelatedAndKeepsAFullHandshakeNonPartial()
    {
        if (OptedOut) return;
        Assert.True(TsharkAvailable(), "tshark is required for this test (set MONITORING_SKIP_TSHARK_TESTS=1 to opt out explicitly).");
        var metrics = new CaptureMetrics();
        var lines = await ReadAsync(metrics);
        Assert.Equal(8, lines.Count);

        var parser = new TsharkParser();
        var scope = new ProbeScope("site-a", "sensor-a");
        var correlator = new FlowCorrelator(scope, new CorrelationOptions());
        foreach (var line in lines)
            if (parser.Parse(line) is { } packet) correlator.Accept(packet);
        // Seven IP packets, one ARP frame, no parser errors; ICMP is an observation, not a session.
        Assert.Equal((8L, 0L, 1L), (parser.PacketsSeen, parser.ParserErrors, parser.NonIpFrames));
        Assert.Equal(1, correlator.Observations);
        Assert.Equal(3, correlator.ActiveFlows);

        var events = correlator.Close("shutdown").Select(item => item.Data).OrderBy(data => data.SourceIp, StringComparer.Ordinal).ToArray();
        var tcp = Assert.Single(events, data => data.SourceIp == "192.0.2.1");
        Assert.Equal(("192.0.2.2", 1234, 443, "TCP", 4L, 221L, (int?)null), (tcp.DestinationIp, tcp.SourcePort, tcp.DestinationPort, tcp.Protocol, tcp.PacketCount, tcp.ByteCount, tcp.VlanId));
        Assert.False(tcp.Partial); // SYN, SYN-ACK and reverse traffic were all seen
        Assert.Equal("2026-09-21T14:13:20.100Z", tcp.StartedAt);
        Assert.Equal("2026-09-21T14:13:20.400Z", tcp.EndedAt);
        var udp = Assert.Single(events, data => data.SourceIp == "198.51.100.5");
        Assert.Equal(("UDP", 42, true), (udp.Protocol, udp.VlanId, udp.Partial));
        var v6 = Assert.Single(events, data => data.SourceIp == "2001:db8::1");
        Assert.Equal(("TCP", 40000, true), (v6.Protocol, v6.SourcePort, v6.Partial));
    }

    [Fact]
    public async Task ARealCaptureRunsThroughTheEngineIntoTheDurableSpoolWithoutPersistingAPcap()
    {
        if (OptedOut) return;
        Assert.True(TsharkAvailable(), "tshark is required for this test (set MONITORING_SKIP_TSHARK_TESTS=1 to opt out explicitly).");
        var directory = Path.Combine(Path.GetTempPath(), "monitoring-real-tshark-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scope = new ProbeScope("site-a", "sensor-a");
            var metrics = new CaptureMetrics();
            var parser = new TsharkParser();
            using var spool = new SqliteSpool(Path.Combine(directory, "spool.db"), scope, new SpoolOptions());
            var engine = new ProbeEngine(new TsharkCapture(new CaptureOptions("unused", FixturePcap: Fixture), metrics), parser,
                new FlowCorrelator(scope, new CorrelationOptions()), spool, metrics, TimeProvider.System, fixture: true);
            await engine.CaptureOnceAsync();
            Assert.Equal("capturing", metrics.State);
            var status = spool.Status(DateTimeOffset.UtcNow);
            Assert.Equal(3L, status.Events);
            var batch = spool.Next(DateTimeOffset.UtcNow)!;
            Assert.Equal(3, batch.EventCount);
            using var json = System.Text.Json.JsonDocument.Parse(batch.Body);
            Assert.Equal("site-a", json.RootElement.GetProperty("siteId").GetString());
            // Nothing but the spool database (and its journal files) exists on disk: no PCAP and no payload are persisted.
            Assert.All(Directory.GetFiles(directory), file => Assert.StartsWith("spool.db", Path.GetFileName(file)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

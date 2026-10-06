using System.Runtime.CompilerServices;
using Monitoring.Probe;

namespace Monitoring.Probe.Tests;

public sealed class ExtractorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monitoring-capture-test-" + Guid.NewGuid().ToString("N"));
    public ExtractorTests() => Directory.CreateDirectory(directory);
    [Fact]
    public void LiveCapturePipesOnlyToStdoutAndTsharkPersistsNoPcap()
    {
        var options = new CaptureOptions("eth-span");
        var args = TsharkCapture.TsharkArguments(options);
        Assert.Contains("-r", args); Assert.Equal("-", args[args.ToList().IndexOf("-r") + 1]);
        Assert.DoesNotContain("-w", args); Assert.DoesNotContain("-i", args);
        Assert.Contains("frame.time_epoch", args); Assert.Contains("ipv6.src", args); Assert.Contains("vlan.id", args);
        Assert.Contains("-M", args);
        var dump = TsharkCapture.DumpcapArguments(options);
        Assert.Equal("-", dump[dump.ToList().IndexOf("-w") + 1]);
        Assert.Contains("eth-span", dump); Assert.Contains("8", dump); Assert.Contains("256", dump);
    }

    [Fact]
    public async Task InvalidLineDoesNotStopValidCaptureAndUnexpectedExitClosesPartial()
    {
        var scope = new ProbeScope("madrid", "sensor-real"); var metrics = new CaptureMetrics(); var parser = new TsharkParser();
        using var spool = new SqliteSpool(Path.Combine(directory, "spool.db"), scope, new());
        var engine = new ProbeEngine(new FaultyCapture(), parser, new(scope, new()), spool, metrics, TimeProvider.System);
        await engine.CaptureOnceAsync();
        Assert.Equal(3, parser.PacketsSeen); Assert.Equal(1, parser.ParserErrors);
        Assert.Equal("degraded", metrics.State); Assert.Equal(1, metrics.Restarts);
        var batch = spool.Next(DateTimeOffset.UtcNow)!;
        var json = System.Text.Json.JsonDocument.Parse(batch.Body).RootElement;
        Assert.Equal("madrid", json.GetProperty("siteId").GetString());
        Assert.Equal(2, json.GetProperty("events")[0].GetProperty("data").GetProperty("packetCount").GetInt32());
        Assert.Equal("restart", json.GetProperty("events")[0].GetProperty("data").GetProperty("closeReason").GetString());
        Assert.True(json.GetProperty("events")[0].GetProperty("data").GetProperty("partial").GetBoolean());
        Assert.Empty(spool.LoadCheckpoint());
    }

    [Fact]
    public void RecoveryClosesDurableCheckpointWithSameIdentityInSingleSpoolCommit()
    {
        var scope = new ProbeScope("s", "p"); var flow = new FlowCorrelator(scope, new()); flow.Accept(CaptureTests.Packet());
        var id = flow.Snapshot().Single().EventId;
        using var spool = new SqliteSpool(Path.Combine(directory, "restart.db"), scope, new());
        spool.SaveCapture([], flow.Snapshot(), DateTimeOffset.UtcNow);
        var engine = new ProbeEngine(new FaultyCapture(), new(), new(scope, new()), spool, new(), TimeProvider.System);
        engine.Recover(); engine.Recover();
        Assert.Empty(spool.LoadCheckpoint()); Assert.Equal(1, spool.Status(DateTimeOffset.UtcNow).Events);
        using var json = System.Text.Json.JsonDocument.Parse(spool.Next(DateTimeOffset.UtcNow)!.Body);
        Assert.Equal(id, json.RootElement.GetProperty("events")[0].GetProperty("eventId").GetString());
    }
    private sealed class FaultyCapture : ILineCapture
    {
        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return CaptureTests.Line("1791194400.0", "64", "", "", "10.0.0.1", "10.0.0.2", "", "", "6", "", "12345", "443", "", "", "", "1", "0", "0", "0");
            yield return "invalid";
            yield return CaptureTests.Line("1791194401.0", "64", "", "", "10.0.0.2", "10.0.0.1", "", "", "6", "", "443", "12345", "", "", "", "1", "1", "0", "0");
            throw new IOException("capture failed");
        }
    }
    public void Dispose() => Directory.Delete(directory, true);
}

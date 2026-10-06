using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitoring.Probe;

namespace Monitoring.Probe.Tests;

public sealed class SpoolTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monitoring-probe-test-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(directory, "spool.db");
    private static readonly ProbeScope Scope = new("s", "p");
    public SpoolTests() => Directory.CreateDirectory(directory);
    internal static ProbeEvent Event(string id = "event-1") => new(id, "2026-10-05T10:00:00.000Z",
        new("captured-session", 1, "10.0.0.1", "10.0.0.2", 12345, 443, "TCP", "2026-10-05T10:00:00.000Z",
            "2026-10-05T10:00:01.000Z", null, 1, true, true, "inactivity", 1, 64));

    [Fact]
    public void DurableBatchAndCheckpointSurviveRestartWithExactBytesAndIds()
    {
        byte[] bytes; string id; var c = new FlowCorrelator(Scope, new()); c.Accept(CaptureTests.Packet());
        using (var spool = new SqliteSpool(PathName, Scope, new()))
        {
            spool.SaveCapture([Event()], c.Snapshot(), CaptureTests.Start);
            var batch = spool.Next(CaptureTests.Start)!; bytes = batch.Body; id = batch.BatchId;
            Assert.Equal("event-1", JsonDocument.Parse(bytes).RootElement.GetProperty("events")[0].GetProperty("eventId").GetString());
            Assert.Equal(1, spool.Status(CaptureTests.Start).Events);
        }
        using var restarted = new SqliteSpool(PathName, Scope, new());
        var replay = restarted.Next(CaptureTests.Start)!;
        Assert.Equal(id, replay.BatchId); Assert.Equal(bytes, replay.Body);
        Assert.Equal(c.Snapshot().Single().EventId, Assert.Single(restarted.LoadCheckpoint()).EventId);
        using var connection = new SqliteConnection($"Data Source={PathName}"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar());
    }

    [Fact]
    public void FullSpoolEvictsOldestAndLossCounterSurvivesRestart()
    {
        using (var spool = new SqliteSpool(PathName, Scope, new(MaximumEvents: 2)))
        {
            spool.SaveCapture([Event("old")], [], CaptureTests.Start);
            spool.SaveCapture([Event("second")], [], CaptureTests.Start.AddSeconds(1));
            spool.SaveCapture([Event("new")], [], CaptureTests.Start.AddSeconds(2));
            var status = spool.Status(CaptureTests.Start.AddSeconds(3));
            Assert.Equal(2, status.Events); Assert.Equal(1, status.LostEvents); Assert.Equal(0, status.DeliveredEvents);
            Assert.Contains("second", System.Text.Encoding.UTF8.GetString(spool.Next(CaptureTests.Start.AddSeconds(3))!.Body));
            Assert.True(status.DiskBytes <= 64 * 1024 * 1024);
        }
        using var restarted = new SqliteSpool(PathName, Scope, new(MaximumEvents: 2));
        Assert.Equal(1, restarted.Status(CaptureTests.Start).LostEvents);
    }

    [Fact]
    public void AckCannotDeleteBatchWithDifferentBytesAndOnlyDeletesSentBatch()
    {
        using var spool = new SqliteSpool(PathName, Scope, new());
        spool.SaveCapture([Event("a")], [], CaptureTests.Start);
        var sent = spool.Next(CaptureTests.Start)!;
        spool.SaveCapture([Event("b")], [], CaptureTests.Start.AddSeconds(1));
        Assert.False(spool.Acknowledge(sent with { Body = "different"u8.ToArray() }));
        Assert.Equal(2, spool.Status(CaptureTests.Start).Events);
        Assert.True(spool.Acknowledge(sent));
        Assert.Equal(1, spool.Status(CaptureTests.Start).Events);
        Assert.Equal(1, spool.Status(CaptureTests.Start).DeliveredEvents);
        Assert.Contains("\"b\"", System.Text.Encoding.UTF8.GetString(spool.Next(CaptureTests.Start.AddSeconds(1))!.Body));
    }

    [Fact]
    public void BatchLimitAndInvalidContractDoNotCommitPartialCheckpoint()
    {
        using var spool = new SqliteSpool(PathName, Scope, new());
        spool.SaveCapture(Enumerable.Range(0, 501).Select(i => Event($"e-{i}")).ToArray(), [], CaptureTests.Start);
        var first = spool.Next(CaptureTests.Start)!;
        Assert.Equal(500, first.EventCount); Assert.True(first.Body.Length <= 1048576);
        Assert.True(spool.Acknowledge(first));
        Assert.Equal(1, spool.Next(CaptureTests.Start)!.EventCount);
        Assert.Throws<ArgumentException>(() => spool.SaveCapture([Event("bad") with { Data = Event().Data with { Version = 2 } }], [], CaptureTests.Start));
        Assert.Equal(1, spool.Status(CaptureTests.Start).Events);
    }

    [Fact]
    public void PhysicalBudgetIncludesWalAndRepeatedEvictionRemainsBounded()
    {
        var options = new SpoolOptions(MaximumDiskBytes: 262144, MaximumPayloadBytes: 10000, MaximumEvents: 10);
        using var spool = new SqliteSpool(PathName, Scope, options);
        for (var i = 0; i < 100; i++) spool.SaveCapture([Event($"e-{i}")], [], CaptureTests.Start.AddSeconds(i));
        var status = spool.Status(CaptureTests.Start.AddSeconds(100));
        Assert.Equal(90, status.LostEvents); Assert.Equal(10, status.Events);
        Assert.InRange(status.DiskBytes, 1, options.MaximumDiskBytes);
        Assert.InRange(status.OldestAgeSeconds!.Value, 1, 10);
    }

    [Fact]
    public void SizingIncludesBurstAndLaboratoryMargin()
    {
        Assert.Equal(2250000, SpoolSizing.RequiredBytes(1, 100));
        Assert.Equal(4500000, SpoolSizing.RequiredBytes(2, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpoolSizing.RequiredBytes(0, 100));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Monitoring.Probe;

namespace Monitoring.Probe.Tests;

public sealed class DeliveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "monitoring-delivery-test-" + Guid.NewGuid().ToString("N"));
    private readonly FixedClock clock = new();
    private readonly ProbeScope scope = new("madrid", "p1");
    public DeliveryTests() => Directory.CreateDirectory(directory);
    private SqliteSpool Spool() => new(Path.Combine(directory, "spool.db"), scope, new());

    [Fact]
    public async Task LostAckThenRestartReplaysExactBytesAndDeletesOnlyAfterEmpty200()
    {
        var bodies = new List<byte[]>(); var accepted = new HashSet<string>(); var calls = 0;
        using var server = Server(async context =>
        {
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body); bodies.Add(body.ToArray());
            Assert.Equal("/api/v1/ingestion/batches", context.Request.Path);
            using var json = System.Text.Json.JsonDocument.Parse(body.ToArray());
            accepted.Add(json.RootElement.GetProperty("events")[0].GetProperty("eventId").GetString()!);
            context.Response.StatusCode = ++calls == 1 ? 503 : 200;
        });
        using (var spool = Spool())
        {
            spool.SaveCapture([SpoolTests.Event()], [], clock.GetUtcNow());
            var sender = new ProbeDelivery(server.CreateClient(), spool, clock, new(), () => 0);
            Assert.True(await sender.SendNextAsync());
            Assert.Equal(1, spool.Status(clock.GetUtcNow()).Events);
            Assert.Equal(1, spool.Status(clock.GetUtcNow()).Retries);
        }
        clock.Advance(TimeSpan.FromSeconds(2));
        using var restarted = Spool();
        Assert.True(await new ProbeDelivery(server.CreateClient(), restarted, clock, new(), () => 0).SendNextAsync());
        Assert.Equal(2, bodies.Count); Assert.Equal(bodies[0], bodies[1]); Assert.Single(accepted);
        Assert.Equal(0, restarted.Status(clock.GetUtcNow()).Events);
        Assert.Equal(1, restarted.Status(clock.GetUtcNow()).DeliveredEvents);
    }

    [Theory]
    [InlineData(400, true, false)] [InlineData(409, true, false)]
    [InlineData(401, false, true)] [InlineData(403, false, true)]
    [InlineData(429, false, false)] [InlineData(500, false, false)]
    [InlineData(202, false, false)]
    public async Task FailureClassificationRetainsSpoolAndNeverCreditsDelivery(int status, bool isolated, bool suspended)
    {
        using var server = Server(context => { context.Response.StatusCode = status; return Task.CompletedTask; });
        using var spool = Spool(); spool.SaveCapture([SpoolTests.Event()], [], clock.GetUtcNow());
        Assert.True(await new ProbeDelivery(server.CreateClient(), spool, clock, new()).SendNextAsync());
        var result = spool.Status(clock.GetUtcNow());
        Assert.Equal(1, result.Events); Assert.Equal(0, result.DeliveredEvents);
        Assert.Equal(isolated ? 1 : 0, result.IsolatedEvents); Assert.Equal(suspended, result.IdentitySuspended);
        Assert.Null(spool.Next(clock.GetUtcNow()));
    }

    [Fact]
    public async Task NonEmpty200IsNotAcknowledgedAndRetryAfterIsRespected()
    {
        var calls = 0;
        using var server = Server(async context =>
        {
            if (++calls == 1) { context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "30"; }
            else { context.Response.StatusCode = 200; await context.Response.WriteAsync("not-empty"); }
        });
        using var spool = Spool(); spool.SaveCapture([SpoolTests.Event()], [], clock.GetUtcNow());
        var sender = new ProbeDelivery(server.CreateClient(), spool, clock, new(), () => 0);
        Assert.True(await sender.SendNextAsync()); clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(await sender.SendNextAsync()); Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.True(await sender.SendNextAsync());
        Assert.Equal(1, spool.Status(clock.GetUtcNow()).Events); Assert.Equal(0, spool.Status(clock.GetUtcNow()).DeliveredEvents);
    }

    [Fact]
    public async Task ConcurrentCallsIssueOnlyOneRequestAndTimeoutKeepsDurableBody()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        using var server = Server(async context => { Interlocked.Increment(ref calls); started.SetResult(); await release.Task.WaitAsync(context.RequestAborted); context.Response.StatusCode = 200; });
        using var spool = Spool(); spool.SaveCapture([SpoolTests.Event()], [], clock.GetUtcNow());
        var sender = new ProbeDelivery(server.CreateClient(), spool, clock, new(RequestTimeout: TimeSpan.FromMilliseconds(100)));
        var first = sender.SendNextAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await sender.SendNextAsync()); await first; release.SetResult();
        Assert.Equal(1, calls); Assert.Equal(1, spool.Status(clock.GetUtcNow()).Events);
    }

    [Fact]
    public void ExponentialJitterRetryAfterAndRetryCapAreFinite()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ProbeDelivery.Backoff(0, 0));
        Assert.Equal(TimeSpan.FromSeconds(3), ProbeDelivery.Backoff(1, 1));
        Assert.Equal(TimeSpan.FromSeconds(60), ProbeDelivery.Backoff(int.MaxValue, .5));
        Assert.Equal(TimeSpan.FromSeconds(30), ProbeDelivery.Backoff(1, 0, TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(60), ProbeDelivery.Backoff(1, 0, TimeSpan.FromHours(1)));
    }
    private static TestServer Server(RequestDelegate handler) => new(new WebHostBuilder().Configure(app => app.Run(handler)));
    public void Dispose() => Directory.Delete(directory, true);
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset now = CaptureTests.Start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
}

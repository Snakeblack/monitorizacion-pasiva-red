namespace Monitoring.Probe;
public sealed class ProbeEngine(ILineCapture capture, TsharkParser parser, FlowCorrelator correlator, IProbeSpool spool,
    CaptureMetrics metrics, TimeProvider clock, bool fixture = false)
{
    private readonly object gate = new();
    public async Task CaptureOnceAsync(CancellationToken cancellationToken = default)
    {
        using var idleStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var idle = fixture ? Task.CompletedTask : ExpireIdleAsync(idleStop.Token);
        var reason = "shutdown";
        try
        {
            await foreach (var line in capture.ReadLinesAsync(cancellationToken))
            {
                var packet = parser.Parse(line); if (packet is null) continue;
                lock (gate) spool.SaveCapture(correlator.Accept(packet), correlator.Snapshot(), clock.GetUtcNow());
                metrics.State = "capturing"; metrics.Cause = null;
            }
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { reason = "restart"; metrics.State = "degraded"; metrics.Cause = "extractor-failure"; Interlocked.Increment(ref metrics.Restarts); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            idleStop.Cancel(); try { await idle; } catch (OperationCanceledException) when (idleStop.IsCancellationRequested) { }
            lock (gate) spool.SaveCapture(correlator.Close(reason), [], clock.GetUtcNow());
        }
    }
    private async Task ExpireIdleAsync(CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), clock, token);
            lock (gate) { var events = correlator.Expire(clock.GetUtcNow()); if (events.Count > 0) spool.SaveCapture(events, correlator.Snapshot(), clock.GetUtcNow()); }
        }
    }
    public void Recover()
    {
        lock (gate)
        {
            var checkpoint = spool.LoadCheckpoint(); if (checkpoint.Count == 0) return;
            correlator.Restore(checkpoint); spool.SaveCapture(correlator.Close("restart"), [], clock.GetUtcNow());
            Interlocked.Increment(ref metrics.Restarts);
        }
    }
}

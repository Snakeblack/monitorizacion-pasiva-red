using System.Diagnostics.Metrics;

namespace Monitoring.Probe;

public sealed class ProbeTelemetry : IDisposable
{
    private readonly Meter meter = new("Monitoring.Probe", "1.0.0");
    public ProbeTelemetry(TsharkParser parser, FlowCorrelator correlator, CaptureMetrics capture, IProbeSpool spool)
    {
        meter.CreateObservableCounter("monitoring.probe.packets_seen", () => parser.PacketsSeen);
        meter.CreateObservableCounter("monitoring.probe.parser_errors", () => parser.ParserErrors);
        meter.CreateObservableCounter("monitoring.probe.queue_drops", () => Interlocked.Read(ref capture.QueueDrops));
        meter.CreateObservableCounter("monitoring.probe.flow_drops", () => correlator.DroppedPackets);
        meter.CreateObservableCounter("monitoring.probe.capture_restarts", () => Interlocked.Read(ref capture.Restarts));
        meter.CreateObservableCounter<long>("monitoring.probe.capture_drops", () => capture.CaptureDrops is { } drops ? [new Measurement<long>(drops)] : []);
        meter.CreateObservableGauge("monitoring.probe.active_flows", () => correlator.ActiveFlows);
        meter.CreateObservableGauge("monitoring.probe.spool_events", () => Status().Events);
        meter.CreateObservableGauge("monitoring.probe.spool_bytes", () => Status().DiskBytes);
        meter.CreateObservableGauge<double>("monitoring.probe.spool_oldest_seconds", () => Status().OldestAgeSeconds is { } age ? [new Measurement<double>(age)] : []);
        meter.CreateObservableCounter("monitoring.probe.spool_loss", () => Status().LostEvents);
        meter.CreateObservableCounter("monitoring.probe.retries", () => Status().Retries);
        meter.CreateObservableCounter("monitoring.probe.drain_events", () => Status().DeliveredEvents);
        meter.CreateObservableGauge("monitoring.probe.identity_suspended", () => Status().IdentitySuspended ? 1 : 0);
        meter.CreateObservableGauge("monitoring.probe.spool_isolated", () => Status().IsolatedEvents);
        meter.CreateObservableGauge("monitoring.probe.capture_degraded", () => capture.State == "degraded" ? 1 : 0);
        SpoolStatus Status() => spool.Status(DateTimeOffset.UtcNow);
    }
    public void Dispose() => meter.Dispose();
}

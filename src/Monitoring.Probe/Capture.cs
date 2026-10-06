using System.Globalization;
using System.Net;

namespace Monitoring.Probe;

public sealed record ProbeScope(string SiteId, string SensorId);
public sealed record PacketMetadata(DateTimeOffset Timestamp, int Length, string SourceIp, string DestinationIp,
    int? SourcePort, int? DestinationPort, string Protocol, int? VlanId, bool Syn = false, bool Ack = false,
    string? SourceMac = null, string? DestinationMac = null);
public sealed record CorrelationOptions(int MaximumFlows = 10000, long MaximumMemoryBytes = 16 * 1024 * 1024,
    TimeSpan? TcpIdle = null, TimeSpan? UdpIdle = null, TimeSpan? MaximumDuration = null);
public sealed record CapturedData(string Kind, int Version, string SourceIp, string DestinationIp, int SourcePort,
    int DestinationPort, string Protocol, string StartedAt, string EndedAt, int? VlanId, int CorrelationVersion,
    bool Inferred, bool Partial, string CloseReason, long PacketCount, long ByteCount);
public sealed record ProbeEvent(string EventId, string OccurredAt, CapturedData Data);
public sealed record FlowState(string EventId, ProbeScope Scope, PacketMetadata First, DateTimeOffset Last,
    long PacketCount, long ByteCount, bool SawReverse, bool SawSyn, bool SawSynAck);
public sealed class TsharkParser
{
    public long ParserErrors { get; private set; }
    public long PacketsSeen { get; private set; }
    public PacketMetadata? Parse(string line)
    {
        PacketsSeen++;
        var fields = line.Length <= 16384 ? line.Split('\t') : [];
        if (fields.Length != 19 || !decimal.TryParse(fields[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var epoch)
            || epoch < 0 || epoch > 253402300799m || !int.TryParse(fields[1], out var length) || length < 0
            || !IPAddress.TryParse(fields[4].Length > 0 ? fields[4] : fields[6], out var source)
            || !IPAddress.TryParse(fields[5].Length > 0 ? fields[5] : fields[7], out var destination)
            || !int.TryParse(fields[8].Length > 0 ? fields[8] : fields[9], out var protocol)) return Invalid();
        var name = protocol switch { 6 => "TCP", 17 => "UDP", _ => $"IP-{protocol}" };
        int? sourcePort = null, destinationPort = null, vlan = null;
        if (name is "TCP" or "UDP")
        {
            var offset = name == "TCP" ? 10 : 12;
            if (!ushort.TryParse(fields[offset], out var sp) || !ushort.TryParse(fields[offset + 1], out var dp)) return Invalid();
            sourcePort = sp; destinationPort = dp;
        }
        if (fields[14].Length > 0)
        {
            if (!int.TryParse(fields[14], out var id) || id is < 0 or > 4094) return Invalid();
            vlan = id;
        }
        return new(DateTimeOffset.FromUnixTimeMilliseconds((long)decimal.Floor(epoch * 1000)), length,
            source.ToString(), destination.ToString(), sourcePort, destinationPort, name, vlan,
            fields[15] == "1", fields[16] == "1", Empty(fields[2]), Empty(fields[3]));
    }
    private PacketMetadata? Invalid() { ParserErrors++; return null; }
    private static string? Empty(string value) => value.Length == 0 ? null : value;
}
public sealed class FlowCorrelator
{
    private readonly ProbeScope scope;
    private readonly CorrelationOptions options;
    private readonly Dictionary<FlowKey, FlowState> flows = [];
    private readonly TimeSpan tcpIdle, udpIdle, maximumDuration;
    // Fixed conservative charge covers bounded endpoint strings, record, key and dictionary overhead.
    private const int FlowMemoryCharge = 2048;
    public FlowCorrelator(ProbeScope scope, CorrelationOptions options)
    {
        ProbeJson.ValidateScope(scope);
        this.scope = scope; this.options = options;
        tcpIdle = options.TcpIdle ?? TimeSpan.FromMinutes(5);
        udpIdle = options.UdpIdle ?? TimeSpan.FromMinutes(1);
        maximumDuration = options.MaximumDuration ?? TimeSpan.FromHours(1);
        if (options.MaximumFlows <= 0 || options.MaximumMemoryBytes < FlowMemoryCharge || tcpIdle <= TimeSpan.Zero
            || udpIdle <= TimeSpan.Zero || maximumDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }
    public long DroppedPackets { get; private set; }
    public long Observations { get; private set; }
    public int ActiveFlows => flows.Count;
    public IReadOnlyList<ProbeEvent> Accept(PacketMetadata packet)
    {
        var closed = Expire(packet.Timestamp);
        if (packet.Protocol is not ("TCP" or "UDP")) { Observations++; return closed; }
        var key = Key(packet);
        if (!flows.TryGetValue(key, out var flow))
        {
            if (flows.Count >= options.MaximumFlows || (flows.Count + 1L) * FlowMemoryCharge > options.MaximumMemoryBytes)
            { DroppedPackets++; return closed; }
            flow = new(Guid.NewGuid().ToString("N"), scope, packet, packet.Timestamp, 0, 0, false, false, false);
        }
        // Late timestamps never move end before start; capture remains inferred, not a TCP reconstruction.
        flows[key] = flow with { Last = packet.Timestamp > flow.Last ? packet.Timestamp : flow.Last,
            PacketCount = checked(flow.PacketCount + 1), ByteCount = checked(flow.ByteCount + packet.Length),
            SawReverse = flow.SawReverse || packet.SourceIp != flow.First.SourceIp || packet.SourcePort != flow.First.SourcePort,
            SawSyn = flow.SawSyn || packet.Syn && !packet.Ack,
            SawSynAck = flow.SawSynAck || packet.Syn && packet.Ack };
        return closed;
    }
    public IReadOnlyList<ProbeEvent> Expire(DateTimeOffset now)
    {
        var closed = new List<ProbeEvent>();
        foreach (var (key, flow) in flows.ToArray())
        {
            var reason = now - flow.First.Timestamp >= maximumDuration ? "max-duration"
                : now - flow.Last >= (flow.First.Protocol == "TCP" ? tcpIdle : udpIdle) ? "inactivity" : null;
            if (reason is null) continue;
            closed.Add(ToEvent(flow, reason)); flows.Remove(key);
        }
        return closed;
    }
    public IReadOnlyList<ProbeEvent> Close(string reason)
    {
        if (reason is not ("shutdown" or "restart")) throw new ArgumentException("Invalid close reason.", nameof(reason));
        var closed = flows.Values.Select(f => ToEvent(f, reason)).ToArray(); flows.Clear(); return closed;
    }
    public IReadOnlyList<FlowState> Snapshot() => flows.Values.ToArray();
    public void Restore(IEnumerable<FlowState> checkpoint)
    {
        foreach (var flow in checkpoint)
        {
            if (flow.Scope != scope || flows.Count >= options.MaximumFlows || (flows.Count + 1L) * FlowMemoryCharge > options.MaximumMemoryBytes)
                throw new InvalidDataException("Checkpoint exceeds configured scope or capacity.");
            flows.Add(Key(flow.First), flow);
        }
    }
    private static ProbeEvent ToEvent(FlowState flow, string reason) => new(flow.EventId, ProbeJson.Timestamp(flow.Last),
        new("captured-session", 1, flow.First.SourceIp, flow.First.DestinationIp, flow.First.SourcePort!.Value,
            flow.First.DestinationPort!.Value, flow.First.Protocol, ProbeJson.Timestamp(flow.First.Timestamp), ProbeJson.Timestamp(flow.Last),
            flow.First.VlanId, 1, true, reason == "restart" || !flow.SawReverse || flow.First.Protocol == "TCP" && !(flow.SawSyn && flow.SawSynAck),
            reason, flow.PacketCount, flow.ByteCount));
    private static FlowKey Key(PacketMetadata p)
    {
        var a = new Endpoint(p.SourceIp, p.SourcePort!.Value); var b = new Endpoint(p.DestinationIp, p.DestinationPort!.Value);
        var first = string.CompareOrdinal(a.Ip, b.Ip) < 0 || a.Ip == b.Ip && a.Port <= b.Port;
        return new(first ? a : b, first ? b : a, p.Protocol, p.VlanId);
    }
    private sealed record Endpoint(string Ip, int Port);
    private sealed record FlowKey(Endpoint A, Endpoint B, string Protocol, int? Vlan);
}

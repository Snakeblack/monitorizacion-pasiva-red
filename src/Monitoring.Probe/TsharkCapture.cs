using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Monitoring.Probe;

public sealed record CaptureOptions(string Interface, string TsharkPath = "tshark", string DumpcapPath = "dumpcap",
    string? FixturePcap = null, int QueueCapacity = 1024, int SnapshotLength = 256, int BufferMegabytes = 8,
    int MaximumDissectorPackets = 10000);
public interface ILineCapture
{
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
}
public sealed class CaptureMetrics
{
    public long QueueDrops;
    public long? CaptureDrops;
    public long Restarts;
    public string State = "starting";
    public string? Cause;
}
public sealed class TsharkCapture(CaptureOptions options, CaptureMetrics metrics) : ILineCapture
{
    public static readonly string[] Fields = ["frame.time_epoch", "frame.len", "eth.src", "eth.dst", "ip.src", "ip.dst", "ipv6.src", "ipv6.dst",
        "ip.proto", "ipv6.nxt", "tcp.srcport", "tcp.dstport", "udp.srcport", "udp.dstport", "vlan.id", "tcp.flags.syn", "tcp.flags.ack", "tcp.flags.fin", "tcp.flags.reset"];
    public static IReadOnlyList<string> TsharkArguments(CaptureOptions options)
    {
        Validate(options);
        var args = new List<string> { "-n", "-l", "-r", options.FixturePcap ?? "-", "-T", "fields", "-E", "separator=/t", "-E", "occurrence=f", "-E", "quote=n", "-E", "escape=y",
            "-M", options.MaximumDissectorPackets.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-o", "tcp.analyze_sequence_numbers:FALSE", "-o", "tcp.desegment_tcp_streams:FALSE", "-o", "ip.defragment:FALSE", "-o", "ipv6.defragment:FALSE" };
        foreach (var field in Fields) { args.Add("-e"); args.Add(field); } return args;
    }
    public static IReadOnlyList<string> DumpcapArguments(CaptureOptions options)
    {
        Validate(options);
        return ["-i", options.Interface, "-B", options.BufferMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-s", options.SnapshotLength.ToString(System.Globalization.CultureInfo.InvariantCulture), "-P", "-w", "-"];
    }
    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(options.QueueCapacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceAsync(queue.Writer, stopped.Token);
        try { await foreach (var line in queue.Reader.ReadAllAsync(cancellationToken)) yield return line; }
        finally { stopped.Cancel(); try { await producer; } catch (OperationCanceledException) when (stopped.IsCancellationRequested) { } }
    }
    private async Task ProduceAsync(ChannelWriter<string> writer, CancellationToken token)
    {
        Process? tshark = null, dumpcap = null;
        try
        {
            tshark = Start(options.TsharkPath, TsharkArguments(options), options.FixturePcap is null);
            var errors = DrainErrorsAsync(tshark.StandardError, false, token);
            Task? pipe = null, dumpErrors = null;
            if (options.FixturePcap is null)
            {
                dumpcap = Start(options.DumpcapPath, DumpcapArguments(options), false);
                dumpErrors = DrainErrorsAsync(dumpcap.StandardError, true, token);
                pipe = PipeAsync(dumpcap.StandardOutput.BaseStream, tshark.StandardInput.BaseStream, token);
            }
            while (await ReadBoundedLineAsync(tshark.StandardOutput, token) is { } line)
                if (!writer.TryWrite(line)) Interlocked.Increment(ref metrics.QueueDrops);
            await tshark.WaitForExitAsync(token); await errors;
            if (pipe is not null) await pipe;
            if (dumpcap is not null) { await dumpcap.WaitForExitAsync(token); await dumpErrors!; }
            if (tshark.ExitCode != 0 || dumpcap is not null && dumpcap.ExitCode != 0) throw new IOException("extractor-exit");
            writer.TryComplete();
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        { writer.TryComplete(exception); }
        finally
        {
            Stop(dumpcap); Stop(tshark);
        }
    }
    private async Task DrainErrorsAsync(StreamReader stream, bool captureDrops, CancellationToken token)
    {
        while (await ReadBoundedLineAsync(stream, token) is { } line)
        {
            // Only observed capture-driver counters are published; absence remains unknown.
            if (!captureDrops) continue;
            var match = System.Text.RegularExpressions.Regex.Match(line, @"Packets dropped:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            if (match.Success && long.TryParse(match.Groups[1].Value, out var count)) metrics.CaptureDrops = count;
        }
    }
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var value = new StringBuilder(); var buffer = new char[1]; var overflow = false;
        while (await reader.ReadAsync(buffer, token) != 0)
        {
            if (buffer[0] == '\n') return overflow ? "invalid-oversized-line" : value.ToString().TrimEnd('\r');
            if (value.Length < 16384) value.Append(buffer[0]); else overflow = true;
        }
        return value.Length == 0 ? null : overflow ? "invalid-oversized-line" : value.ToString();
    }
    private static async Task PipeAsync(Stream source, Stream target, CancellationToken token)
    { try { await source.CopyToAsync(target, 65536, token); } finally { target.Close(); } }
    private static Process Start(string executable, IReadOnlyList<string> arguments, bool input)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = input, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("extractor-start");
    }
    private static void Stop(Process? process)
    { if (process is null) return; try { if (!process.HasExited) process.Kill(true); } finally { process.Dispose(); } }
    private static void Validate(CaptureOptions value)
    {
        if (string.IsNullOrWhiteSpace(value.Interface) || value.QueueCapacity is < 1 or > 65536 || value.SnapshotLength is < 64 or > 65535
            || value.BufferMegabytes is < 1 or > 256 || value.MaximumDissectorPackets <= 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

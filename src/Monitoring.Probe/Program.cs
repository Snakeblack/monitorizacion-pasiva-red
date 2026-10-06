using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Monitoring.Probe;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--status")
        { Console.Error.WriteLine("Usage: Monitoring.Probe <configuration.json> [--status]"); return 2; }
        try
        {
            var config = JsonSerializer.Deserialize<ProbeConfiguration>(await File.ReadAllTextAsync(args[0]), ProbeJson.Options)
                ?? throw new ArgumentException("Missing configuration."); config.Validate();
            using var spool = new SqliteSpool(config.SpoolPath, config.Scope, config.Spool);
            if (args.Length == 2) { Console.WriteLine(JsonSerializer.Serialize(spool.Status(DateTimeOffset.UtcNow), ProbeJson.Options)); return 0; }
            using var stopping = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stopping.Cancel(); };
            using var terminate = OperatingSystem.IsLinux() ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stopping.Cancel(); }) : null;
            var parser = new TsharkParser(); var correlator = new FlowCorrelator(config.Scope, config.Correlation); var captureMetrics = new CaptureMetrics();
            var capture = new TsharkCapture(config.Capture, captureMetrics);
            var engine = new ProbeEngine(capture, parser, correlator, spool, captureMetrics, TimeProvider.System, config.Capture.FixturePcap is not null);
            engine.Recover();
            using var handler = new HttpClientHandler();
            using var certificate = config.ClientCertificatePem is null ? null : X509Certificate2.CreateFromPemFile(config.ClientCertificatePem, config.ClientKeyPem);
            if (certificate is not null) handler.ClientCertificates.Add(certificate);
            using var client = new HttpClient(handler) { BaseAddress = config.Endpoint is null ? null : new(config.Endpoint), Timeout = Timeout.InfiniteTimeSpan };
            var delivery = config.Endpoint is null ? null : new ProbeDelivery(client, spool, TimeProvider.System, new());
            using var telemetry = new ProbeTelemetry(parser, correlator, captureMetrics, spool);
            var deliveryLoop = delivery is null ? Task.CompletedTask : DeliverAsync(delivery, stopping.Token);
            var restartAttempt = 0;
            try
            {
                do
                {
                    await engine.CaptureOnceAsync(stopping.Token);
                    Console.WriteLine(JsonSerializer.Serialize(new { capture = captureMetrics.State, cause = captureMetrics.Cause,
                        parser.PacketsSeen, parser.ParserErrors, captureMetrics.Restarts, captureMetrics.QueueDrops,
                        captureMetrics.CaptureDrops, correlator.DroppedPackets, correlator.Observations, spool = spool.Status(DateTimeOffset.UtcNow) }, ProbeJson.Options));
                    if (config.Capture.FixturePcap is not null) break;
                    if (!stopping.IsCancellationRequested) await Task.Delay(ProbeDelivery.Backoff(restartAttempt++, Random.Shared.NextDouble()), stopping.Token);
                } while (!stopping.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            finally { stopping.Cancel(); try { await deliveryLoop; } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { } }
            return captureMetrics.State == "degraded" ? 3 : 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or Microsoft.Data.Sqlite.SqliteException or System.Security.Cryptography.CryptographicException)
        { Console.Error.WriteLine("probe-failed:" + exception.GetType().Name); return 2; }
    }
    private static async Task DeliverAsync(ProbeDelivery delivery, CancellationToken token)
    {
        while (true) { await delivery.SendNextAsync(token); await Task.Delay(TimeSpan.FromMilliseconds(250), token); }
    }
}

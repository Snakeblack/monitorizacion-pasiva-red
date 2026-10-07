using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Persistence;
using System.Security.Authentication;

namespace Monitoring.Host.Security;

// Downloads CRLs over HTTP(S). The list is signed, so the channel adds no trust; size and time are bounded so a hostile or broken
// endpoint cannot exhaust the host, and every failure is just "no new information".
public sealed class HttpCrlSource(HttpClient client, ILogger<HttpCrlSource> logger) : ICrlSource
{
    public const int MaximumBytes = 10 * 1024 * 1024;

    public async Task<byte[]?> FetchAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaximumBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("CRL could not be retrieved ({FailureType}).", exception.GetType().Name);
            return null;
        }
    }
}

// Keeps revocation fresh. The first refresh happens before the host serves traffic so a restart never leaves a window that is
// "unknown" longer than necessary, but it never blocks start-up: unknown simply keeps ingestion closed.
public sealed class RevocationRefreshWorker(RevocationService revocation, ProbeTrustOptions options, ILogger<RevocationRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.RefreshInterval);
        try
        {
            do
            {
                try { await revocation.RefreshAsync(stoppingToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning("Revocation refresh failed ({FailureType}); ingestion stays closed until a fresh list is verified.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}

public sealed class UnconfiguredProbeRegistry : IProbeRegistry
{
    public Task<ProbeBinding?> FindAsync(string issuerSha256, string serialHex, CancellationToken cancellationToken) => Task.FromResult<ProbeBinding?>(null);
}

public static class ProbeTrustRegistration
{
    public static bool AddProbeCertificateAuthentication(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(ProbeTrustOptions.Section).Get<ProbeTrustOptions>() ?? new ProbeTrustOptions();
        if (!options.Validate(builder.Environment)) return false;

        var anchors = new X509Certificate2Collection();
        foreach (var path in options.CaCertificatePaths)
        {
            var anchor = LoadAnchor(path);
            anchors.Add(anchor);
        }
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(anchors);
        builder.Services.AddHttpClient<ICrlSource, HttpCrlSource>();
        builder.Services.AddSingleton<ProbeAuthMetrics>();
        builder.Services.AddSingleton(provider => new RevocationService(provider.GetRequiredService<ICrlSource>(), anchors, options, provider.GetRequiredService<TimeProvider>()));
        // Without a database nothing is registered: every certificate is refused (the same unconfigured, fail-closed stance as the other ports).
        builder.Services.TryAddScoped<IProbeRegistry, UnconfiguredProbeRegistry>();
        builder.Services.AddScoped<ProbeCertificateValidator>(provider => new ProbeCertificateValidator(anchors,
            provider.GetRequiredService<RevocationService>(), provider.GetRequiredService<IProbeRegistry>(), options, provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddHostedService<RevocationRefreshWorker>();
        // Handshake: TLS 1.2+ only and a client certificate may be presented. The trust decision is the validator's (chain, purpose,
        // revocation, registry) and is enforced on the ingestion route, so a handshake that accepts any certificate grants nothing.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
        {
            https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            https.ClientCertificateValidation = (_, _, _) => true;
        }));
        return true;
    }

    private static X509Certificate2 LoadAnchor(string path)
    {
        var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
        var isCa = certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority);
        var now = DateTimeOffset.UtcNow;
        if (!isCa || now < certificate.NotBefore || now > certificate.NotAfter)
            throw new InvalidOperationException("Every configured probe CA must be a currently valid CA certificate.");
        return certificate;
    }
}

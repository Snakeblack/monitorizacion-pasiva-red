using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Monitoring.Host.Ingestion;
using Monitoring.Host.Security;
using Monitoring.Persistence;

namespace Monitoring.Tests;

// Real TLS: a Kestrel listener, real client certificates and the production registration, against real PostgreSQL. It proves the
// transport and trust wiring end to end; the issuing CA and the real revocation endpoint (EJBCA) remain a manual integration.
public sealed class ProbeMutualTlsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Batch = "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"madrid\",\"sensorId\":\"sensor-1\",\"events\":[{\"eventId\":\"event-1\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":{\"value\":7}}]}";

    private sealed class Source(Func<byte[]?> next) : ICrlSource
    {
        public Task<byte[]?> FetchAsync(string location, CancellationToken cancellationToken) => Task.FromResult(next());
    }

    private sealed class Writer : IIngestionBatchWriter
    {
        public List<TrustedSensorIdentity> Seen { get; } = [];
        public Task<IngestionWriteResult> WriteAsync(TrustedSensorIdentity identity, Monitoring.Domain.Ingestion.IngestionBatch batch, CancellationToken cancellationToken)
        {
            lock (Seen) Seen.Add(identity);
            return Task.FromResult(IngestionWriteResult.Accepted);
        }
    }

    private readonly TestPki _pki = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("mtls-").FullName;
    private readonly List<WebApplication> _apps = [];

    private static X509Certificate2 WithKey(X509Certificate2 certificate) => X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);

    private async Task<(WebApplication App, string Https, string Http, Writer Writer)> StartAsync(string connection, TimeSpan refresh)
    {
        var caFile = Path.Combine(_directory, "ca.pem");
        File.WriteAllText(caFile, _pki.Ca.ExportCertificatePem());
        using var serverIssued = _pki.Issue("localhost", eku: TestPki.ServerAuth);
        var serverCertificate = WithKey(serverIssued);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ApplicationName = "Monitoring.Host" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Probes:Trust:CaCertificatePaths:0"] = caFile,
            ["Probes:Trust:CrlLocations:0"] = "https://crl.test/ca.crl",
            ["Probes:Trust:RefreshInterval"] = refresh.ToString("c")
        });
        builder.Services.AddDbContext<MonitoringDbContext>(options => options.UseNpgsql(connection));
        builder.Services.AddScoped<IProbeRegistry, ProbeRegistry>();
        var writer = new Writer();
        builder.Services.AddSingleton<IIngestionBatchWriter>(writer);
        builder.Services.AddSingleton<ITrustedSensorIdentityProvider, HostContextTrustedSensorIdentityProvider>();
        builder.Services.AddSingleton<IngestionRejectionMetrics>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.AddProbeCertificateAuthentication();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate));
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        builder.Services.AddSingleton<ICrlSource>(new Source(() => _pki.BuildCrl()));
        var app = builder.Build();
        app.UseMiddleware<ProbeCertificateMiddleware>();
        app.MapBatchEndpoint();
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.ToArray();
        _apps.Add(app);
        return (app, addresses.Single(a => a.StartsWith("https", StringComparison.Ordinal)), addresses.Single(a => a.StartsWith("http:", StringComparison.Ordinal)), writer);
    }

    private static HttpClient Client(X509Certificate2? certificate)
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        if (certificate is not null) handler.ClientCertificates.Add(WithKey(certificate));
        return new HttpClient(handler);
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string baseAddress) =>
        (await client.PostAsync(baseAddress + BatchEndpoint.Route, new StringContent(Batch, Encoding.UTF8, "application/json"))).StatusCode;

    private async Task<string> RegisterAsync(string connection, X509Certificate2 certificate, string site = "madrid", string sensor = "sensor-1")
    {
        await using var db = SessionTestDatabase.Context(connection);
        Assert.Equal(ProbeRegistration.Registered, await new ProbeRegistry(db).RegisterAsync(site, sensor, ProbeCertificateValidator.IssuerKey(certificate),
            CrlParser.Normalize(certificate.SerialNumberBytes.Span), "ops", "test", CancellationToken.None));
        return ProbeCertificateValidator.IssuerKey(certificate);
    }

    private static async Task<HttpStatusCode> EventuallyAsync(Func<Task<HttpStatusCode>> attempt, HttpStatusCode expected)
    {
        var status = await attempt();
        for (var tries = 0; tries < 100 && status != expected; tries++)
        {
            await Task.Delay(100);
            status = await attempt();
        }
        return status;
    }

    [Fact]
    public async Task OnlyAValidRegisteredCertificateOverTlsIsAcceptedAndItWritesAsItsRegisteredIdentity()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var (_, https, http, writer) = await StartAsync(connection, TimeSpan.FromMilliseconds(200));
        using var certificate = _pki.Issue("sensor-9-claimed-by-name");
        using var foreignPki = new TestPki("Foreign CA");
        using var foreign = foreignPki.Issue("probe");
        using var unregistered = _pki.Issue("probe");
        await RegisterAsync(connection, certificate);
        await RegisterAsync(connection, foreign);

        using var good = Client(certificate);
        Assert.Equal(HttpStatusCode.OK, await EventuallyAsync(() => PostAsync(good, https), HttpStatusCode.OK));
        Assert.Equal(new TrustedSensorIdentity("madrid", "sensor-1"), writer.Seen.Single());
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(anonymous, https));
        using var untrusted = Client(foreign);
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(untrusted, https));
        using var notRegistered = Client(unregistered);
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(notRegistered, https));
        // Plaintext never authenticates, even if a certificate registered in the registry exists.
        using var plain = Client(certificate);
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(plain, http));
        Assert.Single(writer.Seen);
    }

    [Fact]
    public async Task RevocationAndDisablingTakeEffectAndSurviveARestartWhileARenewedCertificateKeepsTheIdentity()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var (app, https, _, _) = await StartAsync(connection, TimeSpan.FromMilliseconds(200));
        using var current = _pki.Issue("probe");
        using var revoked = _pki.Issue("probe");
        await RegisterAsync(connection, current);
        await RegisterAsync(connection, revoked);
        using var currentClient = Client(current);
        using var revokedClient = Client(revoked);
        Assert.Equal(HttpStatusCode.OK, await EventuallyAsync(() => PostAsync(revokedClient, https), HttpStatusCode.OK));
        // Revocation reaches the next CRL refresh.
        _pki.Revoke(revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, await EventuallyAsync(() => PostAsync(revokedClient, https), HttpStatusCode.Unauthorized));
        // Disabling takes effect immediately and persists across a restart; the renewed certificate (new serial) writes as the same sensor.
        await using (var db = SessionTestDatabase.Context(connection))
        {
            Assert.Equal(ProbeDisabling.Disabled, await new ProbeRegistry(db).DisableAsync(ProbeCertificateValidator.IssuerKey(current), CrlParser.Normalize(current.SerialNumberBytes.Span), "ops", "renewed", CancellationToken.None));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(currentClient, https));
        await app.StopAsync();
        using var renewed = _pki.Issue("probe");
        await RegisterAsync(connection, renewed);
        var (_, httpsAfter, _, writerAfter) = await StartAsync(connection, TimeSpan.FromMilliseconds(200));
        using var renewedClient = Client(renewed);
        Assert.Equal(HttpStatusCode.OK, await EventuallyAsync(() => PostAsync(renewedClient, httpsAfter), HttpStatusCode.OK));
        Assert.Equal(new TrustedSensorIdentity("madrid", "sensor-1"), writerAfter.Seen.Single());
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(currentClient, httpsAfter));
    }

    [Fact]
    public async Task WithoutAnyReachableRevocationListIngestionStaysClosed()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var caFile = Path.Combine(_directory, "ca.pem");
        File.WriteAllText(caFile, _pki.Ca.ExportCertificatePem());
        using var serverIssued = _pki.Issue("localhost", eku: TestPki.ServerAuth);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ApplicationName = "Monitoring.Host" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Probes:Trust:CaCertificatePaths:0"] = caFile, ["Probes:Trust:CrlLocations:0"] = "https://crl.test/ca.crl"
        });
        builder.Services.AddDbContext<MonitoringDbContext>(options => options.UseNpgsql(connection));
        builder.Services.AddScoped<IProbeRegistry, ProbeRegistry>();
        builder.Services.AddSingleton<IIngestionBatchWriter>(new Writer());
        builder.Services.AddSingleton<ITrustedSensorIdentityProvider, HostContextTrustedSensorIdentityProvider>();
        builder.Services.AddSingleton<IngestionRejectionMetrics>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.AddProbeCertificateAuthentication();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(WithKey(serverIssued))));
        builder.Services.AddSingleton<ICrlSource>(new Source(() => null));
        var app = builder.Build();
        app.UseMiddleware<ProbeCertificateMiddleware>();
        app.MapBatchEndpoint();
        await app.StartAsync();
        _apps.Add(app);
        using var certificate = _pki.Issue("probe");
        await RegisterAsync(connection, certificate);
        using var client = Client(certificate);
        var https = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(client, https));
    }

    public void Dispose()
    {
        // Stop first so background services see a normal shutdown instead of a torn-down container.
        foreach (var app in _apps)
        {
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _pki.Dispose();
        Directory.Delete(_directory, true);
    }
}

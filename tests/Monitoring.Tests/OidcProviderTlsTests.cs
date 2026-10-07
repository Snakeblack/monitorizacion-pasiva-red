using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Tests;

// The API reaches its identity provider over TLS (ADR-025). The provider here is a real Kestrel HTTPS listener whose certificate comes from a
// throw-away CA, so the whole back channel (discovery, key set, certificate chain, name and validity) is exercised, not a mocked key source.
public sealed class OidcProviderTlsTests : IAsyncDisposable
{
    private const string Day = "from=2026-10-05T12:00:00.000Z&to=2026-10-06T11:00:00Z";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly TestPki _providerCa = new("Test Identity CA");
    private readonly TestPki _otherCa = new("Some Other CA");
    private readonly OidcTestIdp _idp = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("oidc-tls-").FullName;
    private readonly List<WebApplication> _providers = [];
    private readonly List<WebApplicationFactory<Program>> _hosts = [];
    private int _keySetRequests;

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private static X509Certificate2 WithKey(X509Certificate2 certificate) => X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);

    private string CaFile(TestPki pki, string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, pki.Ca.ExportCertificatePem());
        return path;
    }

    // A provider that serves a discovery document and its key set over HTTPS; returns the issuer it advertises.
    private async Task<string> StartProviderAsync(X509Certificate2 serverCertificate)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ApplicationName = "Monitoring.Tests" });
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(WithKey(serverCertificate))));
        var app = builder.Build();
        var issuer = "";
        app.MapGet("/realms/monitoring/.well-known/openid-configuration", () => Results.Json(new { issuer, jwks_uri = issuer + "/keys" }));
        app.MapGet("/realms/monitoring/keys", async () =>
        {
            Interlocked.Increment(ref _keySetRequests);
            var configuration = await _idp.GetConfigurationAsync(CancellationToken.None);
            return Results.Json(new
            {
                keys = configuration.SigningKeys.OfType<RsaSecurityKey>().Select(key =>
                {
                    var parameters = key.Rsa?.ExportParameters(false) ?? key.Parameters;
                    return new { kty = "RSA", use = "sig", alg = "RS256", kid = key.KeyId, n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent) };
                })
            });
        });
        await app.StartAsync();
        _providers.Add(app);
        var port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
        issuer = $"https://localhost:{port}/realms/monitoring";
        return issuer;
    }

    private WebApplicationFactory<Program> Api(string issuer, params string[] trustedCaFiles) => Api(issuer, TimeSpan.FromMinutes(1), trustedCaFiles);

    // `floor` is the least time between two forced key refreshes; production uses one minute, tests shorten it so rotation can be exercised.
    private WebApplicationFactory<Program> Api(string issuer, TimeSpan floor, params string[] trustedCaFiles)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            ProbeTestTrust.For(builder, "Production");
            builder.UseSetting("Identity:Mode", "Oidc");
            builder.UseSetting("Identity:Authority", issuer);
            builder.UseSetting("Identity:Audience", OidcTestIdp.Audience);
            for (var index = 0; index < trustedCaFiles.Length; index++) builder.UseSetting($"Identity:TrustedCaPaths:{index}", trustedCaFiles[index]);
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options => options.RefreshInterval = floor);
                services.RemoveAll<ISessionSearch>();
                services.AddSingleton<ISessionSearch>(new OidcIdentityTests.RecordingSearch());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTime());
            });
        });
        _hosts.Add(factory);
        return factory;
    }

    private Task<HttpStatusCode> GetAsync(WebApplicationFactory<Program> api, string issuer) => GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer));

    private static async Task<HttpStatusCode> GetWithTokenAsync(WebApplicationFactory<Program> api, string token)
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/v1/sessions?" + Day);
        return response.StatusCode;
    }

    [Fact]
    public async Task KeysFetchedOverTlsFromAProviderSignedByTheTrustedCaAreAccepted()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
    }

    [Fact]
    public async Task TrustFromSeveralConfiguredAuthoritiesIsAdditive()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, CaFile(_otherCa, "other-ca.pem"), CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
    }

    [Fact]
    public async Task AProviderWhoseCertificateChainsToAnUntrustedAuthorityGrantsNoIdentity()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var withoutTrust = Api(issuer);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(withoutTrust, issuer));
        using var wrongTrust = Api(issuer, CaFile(_otherCa, "other-ca.pem"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(wrongTrust, issuer));
    }

    [Fact]
    public async Task AKeyAddedToTheProviderIsTrustedOnTheFirstRequestThatNamesItWithoutARestart()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, TimeSpan.FromSeconds(1), CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
        await Task.Delay(TimeSpan.FromMilliseconds(1_200)); // past the floor between forced refreshes

        _idp.Rotate("key-2", keepOld: true);
        // The first token signed with the new key is accepted, not bounced while the key set is re-read in the background (ADR-026)...
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer, keyId: "key-2")));
        // ...and the key it replaced keeps working until the provider retires it.
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer, keyId: "key-1")));
    }

    [Fact]
    public async Task ATokenNamingAKeyTheProviderNeverPublishedStaysRejectedAndCannotMakeTheApiHammerTheProvider()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
        var before = Volatile.Read(ref _keySetRequests);
        using var foreign = _idp.ForeignKey();
        for (var attempt = 0; attempt < 20; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, await GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer, signWith: foreign, keyId: "never-published")));
        // The configuration manager honours the first forced refresh at once and then the one-minute floor: twenty forged tokens cost the provider
        // at most one extra request, not twenty.
        Assert.InRange(Volatile.Read(ref _keySetRequests) - before, 0, 1);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
    }

    [Fact]
    public async Task AForgedTokenThatReusesARotatedKeyIdWithAnotherSignatureIsStillRejected()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, TimeSpan.FromSeconds(1), CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, issuer));
        await Task.Delay(TimeSpan.FromMilliseconds(1_200));
        _idp.Rotate("key-2", keepOld: true);
        using var foreign = _idp.ForeignKey();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetWithTokenAsync(api, _idp.Issue(roles: ["administrador-inventario"], scopes: [("site-a", "sensor-a")], issuer: issuer, signWith: foreign, keyId: "key-2")));
    }

    [Fact]
    public async Task AKeyTheProviderRetiredStopsBeingTrustedOnceTheKeySetHasBeenReRead()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, TimeSpan.FromSeconds(1), CaFile(_providerCa, "idp-ca.pem"));
        var retiring = _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer);
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, retiring));
        await Task.Delay(TimeSpan.FromMilliseconds(1_200));

        _idp.Rotate("key-2", keepOld: false); // the provider drops key-1
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer, keyId: "key-2")));
        // The key set the API now holds no longer lists key-1, so a token signed with it must not pass (ADR-026).
        Assert.Equal(HttpStatusCode.Unauthorized, await GetWithTokenAsync(api, retiring));
    }

    [Fact]
    public async Task AFailedRefreshWhileTheProviderIsDownKeepsTheKeysAlreadyTrusted()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, TimeSpan.FromSeconds(1), CaFile(_providerCa, "idp-ca.pem"));
        var known = _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer);
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, known));
        await Task.Delay(TimeSpan.FromMilliseconds(1_200));
        await StopProvidersAsync();
        using var foreign = _idp.ForeignKey();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetWithTokenAsync(api, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: issuer, signWith: foreign, keyId: "never-published")));
        Assert.Equal(HttpStatusCode.OK, await GetWithTokenAsync(api, known));
    }

    [Fact]
    public async Task ACertificateForAnotherNameIsRejectedEvenWhenItsAuthorityIsTrusted()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("elsewhere.test", eku: TestPki.ServerAuth, dnsName: "elsewhere.test"));
        using var api = Api(issuer, CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(api, issuer));
    }

    [Fact]
    public async Task AnExpiredProviderCertificateIsRejectedEvenWhenItsAuthorityIsTrusted()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1), TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, CaFile(_providerCa, "idp-ca.pem"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(api, issuer));
    }

    [Fact]
    public async Task ConfiguringTrustChangesTheBackChannelOnlyNotTheIssuerOrAudience()
    {
        var issuer = await StartProviderAsync(_providerCa.Issue("localhost", eku: TestPki.ServerAuth, dnsName: "localhost"));
        using var api = Api(issuer, CaFile(_providerCa, "idp-ca.pem"));
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a")], issuer: "https://evil.test/realms/monitoring"));
        using var response = await client.GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("blank")]
    [InlineData("private-key-only")]
    public void AConfiguredTrustFileThatIsUnusablePreventsStartup(string kind)
    {
        var path = Path.Combine(_directory, kind + ".pem");
        switch (kind)
        {
            case "garbage": File.WriteAllText(path, "this is not a certificate"); break;
            case "private-key-only": File.WriteAllText(path, System.Security.Cryptography.RSA.Create(2048).ExportPkcs8PrivateKeyPem()); break;
            case "blank": path = "  "; break;
        }
        using var api = Api("https://idp.test/realms/monitoring", path);
        Assert.ThrowsAny<Exception>(() => api.CreateClient());
    }

    private async Task StopProvidersAsync()
    {
        foreach (var provider in _providers) await provider.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts) host.Dispose();
        foreach (var provider in _providers) await provider.DisposeAsync();
        _providerCa.Dispose();
        _otherCa.Dispose();
        _idp.Dispose();
    }
}

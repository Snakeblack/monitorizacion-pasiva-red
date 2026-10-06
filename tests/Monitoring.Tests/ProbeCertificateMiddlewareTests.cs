using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Host.Ingestion;
using Monitoring.Host.Security;
using Microsoft.Extensions.FileProviders;

namespace Monitoring.Tests;

public sealed class ProbeCertificateMiddlewareTests : IDisposable
{
    private sealed class Tls(X509Certificate2? certificate) : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; } = certificate;
        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate);
    }

    private sealed class Registry : IProbeRegistry
    {
        public ProbeBinding? Binding { get; set; }
        public Task<ProbeBinding?> FindAsync(string issuerSha256, string serialHex, CancellationToken cancellationToken) => Task.FromResult(Binding);
    }

    private sealed class Source(Func<byte[]?> next) : ICrlSource
    {
        public Task<byte[]?> FetchAsync(string location, CancellationToken cancellationToken) => Task.FromResult(next());
    }

    private readonly TestPki _pki = new();
    private readonly Registry _registry = new();
    private readonly ProbeTrustOptions _options = new() { CrlLocations = ["https://crl.test/ca.crl"] };

    private async Task<(int Status, TrustedSensorIdentity? Identity, bool NextCalled)> RunAsync(X509Certificate2? certificate, bool https = true,
        string path = BatchEndpoint.Route, ITrustedSensorIdentityFeature? preexisting = null)
    {
        var anchors = new X509Certificate2Collection(new X509Certificate2(_pki.Ca));
        var revocation = new RevocationService(new Source(() => _pki.BuildCrl()), anchors, _options, TimeProvider.System);
        await revocation.RefreshAsync(CancellationToken.None);
        var validator = new ProbeCertificateValidator(anchors, revocation, _registry, _options, TimeProvider.System);
        TrustedSensorIdentity? seen = null;
        var called = false;
        var middleware = new ProbeCertificateMiddleware(context =>
        {
            called = true;
            seen = context.Features.Get<ITrustedSensorIdentityFeature>()?.Identity;
            return Task.CompletedTask;
        }, new ProbeAuthMetrics(), NullLogger<ProbeCertificateMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Request.Scheme = https ? "https" : "http";
        if (https) http.Features.Set<ITlsConnectionFeature>(new Tls(certificate));
        if (preexisting is not null) http.Features.Set(preexisting);
        await middleware.InvokeAsync(http, validator);
        return (http.Response.StatusCode, seen, called);
    }

    private sealed class Spoofed : ITrustedSensorIdentityFeature
    {
        public TrustedSensorIdentity Identity { get; } = new("attacker-site", "attacker-sensor");
    }

    [Fact]
    public async Task ARegisteredCertificateSetsTheIdentityTheEndpointTrusts()
    {
        using var certificate = _pki.Issue("anything");
        _registry.Binding = new ProbeBinding("madrid", "sensor-1", true);
        var (status, identity, called) = await RunAsync(certificate);
        Assert.True(called);
        Assert.Equal(new TrustedSensorIdentity("madrid", "sensor-1"), identity);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task NoCertificateAnUnregisteredOneOrPlaintextNeverReachTheEndpoint()
    {
        using var certificate = _pki.Issue("probe");
        var anonymous = await RunAsync(null);
        Assert.Equal((401, false), (anonymous.Status, anonymous.NextCalled));
        _registry.Binding = null;
        var unregistered = await RunAsync(certificate);
        Assert.Equal((401, false), (unregistered.Status, unregistered.NextCalled));
        _registry.Binding = new ProbeBinding("madrid", "sensor-1", true);
        var plaintext = await RunAsync(certificate, https: false);
        Assert.Equal((401, false), (plaintext.Status, plaintext.NextCalled));
    }

    [Fact]
    public async Task ADisabledProbeIsRefusedAndAnIdentityPlantedBeforehandIsNeverTrusted()
    {
        using var certificate = _pki.Issue("probe");
        _registry.Binding = new ProbeBinding("madrid", "sensor-1", false);
        var disabled = await RunAsync(certificate, preexisting: new Spoofed());
        Assert.Equal((401, false, null), (disabled.Status, disabled.NextCalled, disabled.Identity));
        _registry.Binding = new ProbeBinding("madrid", "sensor-1", true);
        var overridden = await RunAsync(certificate, preexisting: new Spoofed());
        Assert.Equal(new TrustedSensorIdentity("madrid", "sensor-1"), overridden.Identity);
    }

    [Fact]
    public async Task OtherRoutesAreNotSubjectToProbeAuthentication()
    {
        var (status, _, called) = await RunAsync(null, path: "/api/v1/sessions/search");
        Assert.Equal((200, true), (status, called));
    }

    public void Dispose() => _pki.Dispose();
}

public sealed class ProbeTrustOptionsTests
{
    private sealed class Named(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Monitoring";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IHostEnvironment Environment(string name) => new Named(name);

    [Fact]
    public void OutsideDevelopmentCertificateAuthenticationMustBeFullyConfiguredOrTheHostRefusesToStart()
    {
        Assert.Throws<InvalidOperationException>(() => new ProbeTrustOptions().Validate(Environment("Production")));
        Assert.Throws<InvalidOperationException>(() => new ProbeTrustOptions { CaCertificatePaths = ["ca.pem"] }.Validate(Environment("Production")));
        Assert.Throws<InvalidOperationException>(() => new ProbeTrustOptions { CaCertificatePaths = ["ca.pem"], CrlLocations = ["file:///etc/crl"] }.Validate(Environment("Production")));
        Assert.Throws<InvalidOperationException>(() => new ProbeTrustOptions { CaCertificatePaths = ["ca.pem"], CrlLocations = ["https://crl/ca.crl"], MaxRevocationAge = TimeSpan.FromMinutes(6) }.Validate(Environment("Production")));
        Assert.Throws<InvalidOperationException>(() => new ProbeTrustOptions { CaCertificatePaths = ["ca.pem"], CrlLocations = ["https://crl/ca.crl"], RefreshInterval = TimeSpan.FromMinutes(5) }.Validate(Environment("Production")));
        Assert.True(new ProbeTrustOptions { CaCertificatePaths = ["ca.pem"], CrlLocations = ["https://crl/ca.crl"] }.Validate(Environment("Production")));
    }

    [Fact]
    public void LocalEnvironmentsMayRunWithoutCertificateAuthentication()
    {
        Assert.False(new ProbeTrustOptions().Validate(Environment("Development")));
        Assert.False(new ProbeTrustOptions().Validate(Environment("Testing")));
    }
}

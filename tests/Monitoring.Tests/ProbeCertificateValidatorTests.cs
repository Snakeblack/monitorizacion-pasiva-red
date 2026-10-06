using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Monitoring.Host.Security;

namespace Monitoring.Tests;

public sealed class ProbeCertificateValidatorTests : IDisposable
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Registry : IProbeRegistry
    {
        public Dictionary<(string Issuer, string Serial), (string Site, string Sensor, bool Active)> Entries { get; } = [];
        public Task<ProbeBinding?> FindAsync(string issuerSha256, string serialHex, CancellationToken cancellationToken) =>
            Task.FromResult<ProbeBinding?>(Entries.TryGetValue((issuerSha256, serialHex), out var entry) ? new ProbeBinding(entry.Site, entry.Sensor, entry.Active) : null);
    }

    private sealed class Source : ICrlSource
    {
        public Func<byte[]?> Next { get; set; } = () => null;
        public int Fetches { get; private set; }
        public Task<byte[]?> FetchAsync(string location, CancellationToken cancellationToken) { Fetches++; return Task.FromResult(Next()); }
    }

    private readonly TestPki _pki = new();
    private readonly Clock _clock = new();
    private readonly Source _source = new();
    private readonly Registry _registry = new();
    private readonly ProbeTrustOptions _options = new() { MaxRevocationAge = TimeSpan.FromMinutes(5), CrlLocations = ["https://crl.test/ca.crl"] };

    private (ProbeCertificateValidator Validator, RevocationService Revocation) Build(TestPki? trust = null)
    {
        var anchors = new X509Certificate2Collection(new X509Certificate2((trust ?? _pki).Ca));
        var revocation = new RevocationService(_source, anchors, _options, _clock);
        return (new ProbeCertificateValidator(anchors, revocation, _registry, _options, _clock), revocation);
    }

    private static (string Issuer, string Serial) Key(X509Certificate2 certificate) =>
        (ProbeCertificateValidator.IssuerKey(certificate), CrlParser.Normalize(certificate.SerialNumberBytes.Span));

    private X509Certificate2 RegisteredProbe(string commonName = "probe", string site = "madrid", string sensor = "sensor-1")
    {
        var certificate = _pki.Issue(commonName);
        _registry.Entries[Key(certificate)] = (site, sensor, true);
        return certificate;
    }

    private async Task<ProbeIdentityResult> ValidateAsync(X509Certificate2? certificate, ProbeCertificateValidator validator, RevocationService revocation, bool refresh = true)
    {
        _source.Next = () => _pki.BuildCrl(_clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
        if (refresh) await revocation.RefreshAsync(CancellationToken.None);
        return await validator.ValidateAsync(certificate, CancellationToken.None);
    }

    [Fact]
    public async Task ARegisteredCertificateYieldsTheSiteAndSensorOfItsRegistryEntryNotOfItsName()
    {
        var (validator, revocation) = Build();
        // The common name claims another sensor; only the registry binding counts.
        using var certificate = RegisteredProbe("sensor-9-impersonation", "madrid", "sensor-1");
        var result = await ValidateAsync(certificate, validator, revocation);
        Assert.Equal(("madrid", "sensor-1", null), (result.Identity!.SiteId, result.Identity.SensorId, result.Cause));
    }

    [Fact]
    public async Task NoCertificateGrantsNothing()
    {
        var (validator, revocation) = Build();
        var result = await ValidateAsync(null, validator, revocation);
        Assert.Equal((null, "no-certificate"), (result.Identity, result.Cause));
    }

    [Fact]
    public async Task ACertificateFromAnotherCaIsNotTrustedEvenIfItsSerialIsRegistered()
    {
        var (validator, revocation) = Build();
        using var foreign = new TestPki("Another CA");
        using var certificate = foreign.Issue("probe");
        _registry.Entries[Key(certificate)] = ("madrid", "sensor-1", true);
        Assert.Equal("untrusted-chain", (await ValidateAsync(certificate, validator, revocation)).Cause);
    }

    [Fact]
    public async Task ExpiredAndNotYetValidCertificatesAreRejected()
    {
        var (validator, revocation) = Build();
        using var expired = _pki.Issue("probe", DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));
        using var future = _pki.Issue("probe", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(10));
        _registry.Entries[Key(expired)] = ("madrid", "sensor-1", true);
        _registry.Entries[Key(future)] = ("madrid", "sensor-1", true);
        Assert.Equal("expired", (await ValidateAsync(expired, validator, revocation)).Cause);
        Assert.Equal("not-yet-valid", (await ValidateAsync(future, validator, revocation)).Cause);
    }

    [Fact]
    public async Task ACertificateNotIssuedForClientAuthenticationIsRejected()
    {
        var (validator, revocation) = Build();
        using var serverOnly = _pki.Issue("probe", eku: TestPki.ServerAuth);
        _registry.Entries[Key(serverOnly)] = ("madrid", "sensor-1", true);
        Assert.Equal("wrong-purpose", (await ValidateAsync(serverOnly, validator, revocation)).Cause);
    }

    [Fact]
    public async Task ACertificateRevokedInTheCurrentCrlIsRejected()
    {
        var (validator, revocation) = Build();
        using var certificate = RegisteredProbe();
        _pki.Revoke(certificate);
        Assert.Equal("revoked", (await ValidateAsync(certificate, validator, revocation)).Cause);
    }

    [Fact]
    public async Task RevocationTakesEffectOnTheNextRefreshWithinTheFreshnessWindow()
    {
        var (validator, revocation) = Build();
        using var certificate = RegisteredProbe();
        Assert.NotNull((await ValidateAsync(certificate, validator, revocation)).Identity);
        _pki.Revoke(certificate);
        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.Equal("revoked", (await ValidateAsync(certificate, validator, revocation)).Cause);
    }

    [Fact]
    public async Task WithoutAFreshRevocationStatusIngestionIsClosedNeverAssumedGood()
    {
        var (validator, revocation) = Build();
        using var certificate = RegisteredProbe();
        // Never refreshed: nothing can be proven.
        Assert.Equal("revocation-unknown", (await ValidateAsync(certificate, validator, revocation, refresh: false)).Cause);
        Assert.NotNull((await ValidateAsync(certificate, validator, revocation)).Identity);
        // Four minutes later the status is still fresh; at five minutes without a successful refresh it is not.
        _clock.Now = _clock.Now.AddMinutes(4).AddSeconds(59);
        Assert.NotNull((await validator.ValidateAsync(certificate, CancellationToken.None)).Identity);
        _clock.Now = _clock.Now.AddSeconds(2);
        Assert.Equal("revocation-unknown", (await validator.ValidateAsync(certificate, CancellationToken.None)).Cause);
        // A successful refresh restores it.
        _source.Next = () => _pki.BuildCrl(_clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
        await revocation.RefreshAsync(CancellationToken.None);
        Assert.NotNull((await validator.ValidateAsync(certificate, CancellationToken.None)).Identity);
    }

    [Fact]
    public async Task AFailedOrForgedRefreshNeverExtendsTheFreshnessOfTheLastGoodList()
    {
        var (validator, revocation) = Build();
        using var certificate = RegisteredProbe();
        Assert.NotNull((await ValidateAsync(certificate, validator, revocation)).Identity);
        using var forged = TestPki.ForgedKey();
        foreach (Func<byte[]?> next in new Func<byte[]?>[] { () => null, () => [1, 2, 3], () => _pki.BuildCrl(signWith: forged) })
        {
            _clock.Now = _clock.Now.AddMinutes(3);
            _source.Next = next;
            await revocation.RefreshAsync(CancellationToken.None);
        }
        // Nine minutes after the last good list, every refresh since failed or was forged: closed.
        Assert.Equal("revocation-unknown", (await validator.ValidateAsync(certificate, CancellationToken.None)).Cause);
    }

    [Fact]
    public async Task AListPastItsOwnNextUpdateIsNotFreshEvenIfJustFetched()
    {
        var (validator, revocation) = Build();
        using var certificate = RegisteredProbe();
        _source.Next = () => _pki.BuildCrl(_clock.Now.AddHours(-3), _clock.Now.AddMinutes(-1));
        await revocation.RefreshAsync(CancellationToken.None);
        Assert.Equal("revocation-unknown", (await validator.ValidateAsync(certificate, CancellationToken.None)).Cause);
    }

    [Fact]
    public async Task UnregisteredAndDisabledProbesAreRejected()
    {
        var (validator, revocation) = Build();
        using var unregistered = _pki.Issue("probe");
        Assert.Equal("not-registered", (await ValidateAsync(unregistered, validator, revocation)).Cause);
        using var disabled = RegisteredProbe();
        _registry.Entries[Key(disabled)] = ("madrid", "sensor-1", false);
        Assert.Equal("disabled", (await ValidateAsync(disabled, validator, revocation)).Cause);
    }

    [Fact]
    public async Task ARenewedCertificateKeepsTheStableIdentityWhileTheOldOneIsDisabledExplicitly()
    {
        var (validator, revocation) = Build();
        using var current = RegisteredProbe();
        using var renewed = _pki.Issue("probe");
        _registry.Entries[Key(renewed)] = ("madrid", "sensor-1", true); // the same probe, a new serial
        var before = await ValidateAsync(current, validator, revocation);
        var after = await ValidateAsync(renewed, validator, revocation);
        Assert.Equal(("madrid", "sensor-1"), (before.Identity!.SiteId, before.Identity.SensorId));
        Assert.Equal(("madrid", "sensor-1"), (after.Identity!.SiteId, after.Identity.SensorId));
        _registry.Entries[Key(current)] = ("madrid", "sensor-1", false);
        Assert.Equal("disabled", (await validator.ValidateAsync(current, CancellationToken.None)).Cause);
        Assert.NotNull((await validator.ValidateAsync(renewed, CancellationToken.None)).Identity);
    }

    [Fact]
    public async Task ACaCertificateIsNeverAClientIdentity()
    {
        var (validator, revocation) = Build();
        _registry.Entries[Key(_pki.Ca)] = ("madrid", "sensor-1", true);
        var result = await ValidateAsync(_pki.Ca, validator, revocation);
        Assert.Null(result.Identity);
    }

    public void Dispose() => _pki.Dispose();
}

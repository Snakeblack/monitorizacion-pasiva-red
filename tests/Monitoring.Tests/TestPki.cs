using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Tests;

// A throw-away PKI built in process (CA, client certificates, signed CRLs) to exercise the probe certificate rules against real X.509
// structures. It is not EJBCA: it proves the API's checks, not the issuing, renewal or revocation procedures of the real CA.
internal sealed class TestPki : IDisposable
{
    internal const string ClientAuth = "1.3.6.1.5.5.7.3.2";
    internal const string ServerAuth = "1.3.6.1.5.5.7.3.1";
    private readonly RSA _caKey = RSA.Create(2048);
    private long _serial = 1000;
    internal X509Certificate2 Ca { get; }
    internal List<(byte[] Serial, DateTimeOffset RevokedAt)> Revoked { get; } = [];
    private int _crlNumber;

    internal TestPki(string name = "Test Probe CA")
    {
        var request = new CertificateRequest($"CN={name}", _caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        Ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    internal X509Certificate2 Issue(string commonName, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, string eku = ClientAuth, byte[]? serial = null,
        string? san = null, string? dnsName = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], false));
        if (san is not null || dnsName is not null)
        {
            var names = new SubjectAlternativeNameBuilder();
            if (san is not null) names.AddUri(new Uri(san));
            if (dnsName is not null) names.AddDnsName(dnsName);
            request.CertificateExtensions.Add(names.Build());
        }
        serial ??= BitConverter.GetBytes(Interlocked.Increment(ref _serial)).Reverse().ToArray();
        using var issued = request.Create(Ca.SubjectName, X509SignatureGenerator.CreateForRSA(_caKey, RSASignaturePadding.Pkcs1), 
            notBefore ?? DateTimeOffset.UtcNow.AddHours(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30), serial);
        return issued.CopyWithPrivateKey(key);
    }

    internal void Revoke(X509Certificate2 certificate) => Revoked.Add((certificate.SerialNumberBytes.ToArray(), DateTimeOffset.UtcNow));

    // A CRL signed by this CA with the revoked serials known so far.
    internal byte[] BuildCrl(DateTimeOffset? thisUpdate = null, DateTimeOffset? nextUpdate = null, RSA? signWith = null)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach (var (serial, at) in Revoked) builder.AddEntry(serial, at, X509RevocationReason.KeyCompromise);
        var now = DateTimeOffset.UtcNow;
        return builder.Build(Ca.SubjectName, X509SignatureGenerator.CreateForRSA(signWith ?? _caKey, RSASignaturePadding.Pkcs1), ++_crlNumber,
            nextUpdate ?? now.AddHours(1), HashAlgorithmName.SHA256, X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Ca, true, false), thisUpdate ?? now.AddMinutes(-1));
    }

    internal static RSA ForgedKey() => RSA.Create(2048);

    public void Dispose() { Ca.Dispose(); _caKey.Dispose(); }
}

// Hosts outside Development/Testing must have certificate authentication configured; tests that are about something else get a valid,
// throw-away CA here so they exercise the production start-up path.
internal static class ProbeTestTrust
{
    private static readonly Lazy<string> CaFile = new(() =>
    {
        using var pki = new TestPki();
        var path = Path.Combine(Directory.CreateTempSubdirectory("probe-trust-").FullName, "ca.pem");
        File.WriteAllText(path, pki.Ca.ExportCertificatePem());
        return path;
    });

    internal static void For(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder, string environment, bool withRetention = true)
    {
        if (environment is "Development" or "Testing") return;
        builder.UseSetting("Probes:Trust:CaCertificatePaths:0", CaFile.Value);
        builder.UseSetting("Probes:Trust:CrlLocations:0", "https://crl.invalid/ca.crl");
        // Production also requires retention; these tests use fixed dates, so the window must never reach them.
        if (withRetention) builder.UseSetting("Retention:SessionRetention", "36500.00:00:00");
    }
}

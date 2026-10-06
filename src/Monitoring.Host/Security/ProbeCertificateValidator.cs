using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Monitoring.Host.Ingestion;

namespace Monitoring.Host.Security;

// Turns a presented client certificate into a sensor identity or a stable rejection cause. Trust comes from the configured CAs, the
// authorization from the registry: the subject name is never read.
public sealed class ProbeCertificateValidator(X509Certificate2Collection anchors, RevocationService revocation, IProbeRegistry registry,
    ProbeTrustOptions options, TimeProvider clock)
{
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

    public static string IssuerKey(X500DistinguishedName name) => Convert.ToHexString(SHA256.HashData(name.RawData));

    public static string IssuerKey(X509Certificate2 certificate) => IssuerKey(certificate.IssuerName);

    public async Task<ProbeIdentityResult> ValidateAsync(X509Certificate2? certificate, CancellationToken cancellationToken)
    {
        if (certificate is null) return ProbeIdentityResult.Rejected("no-certificate");
        if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority))
        {
            return ProbeIdentityResult.Rejected("ca-certificate");
        }

        var now = clock.GetUtcNow();
        if (!ChainsToAnAnchor(certificate, now.UtcDateTime)) return ProbeIdentityResult.Rejected("untrusted-chain");
        if (now < certificate.NotBefore.ToUniversalTime()) return ProbeIdentityResult.Rejected("not-yet-valid");
        if (now > certificate.NotAfter.ToUniversalTime()) return ProbeIdentityResult.Rejected("expired");
        if (!IsForClientAuthentication(certificate)) return ProbeIdentityResult.Rejected("wrong-purpose");
        if (!HasAcceptableKey(certificate)) return ProbeIdentityResult.Rejected("weak-key");

        var issuer = IssuerKey(certificate);
        var serial = CrlParser.Normalize(certificate.SerialNumberBytes.Span);
        switch (revocation.Check(issuer, serial))
        {
            case RevocationStatus.Revoked: return ProbeIdentityResult.Rejected("revoked");
            case RevocationStatus.Unknown: return ProbeIdentityResult.Rejected("revocation-unknown");
        }

        var binding = await registry.FindAsync(issuer, serial, cancellationToken).ConfigureAwait(false);
        if (binding is null) return ProbeIdentityResult.Rejected("not-registered");
        return binding.Active
            ? ProbeIdentityResult.Accepted(new TrustedSensorIdentity(binding.SiteId, binding.SensorId))
            : ProbeIdentityResult.Rejected("disabled");
    }

    // Validity dates are judged separately (to name the cause); everything else — signature, anchor, extensions — must be clean.
    private bool ChainsToAnAnchor(X509Certificate2 certificate, DateTime verificationTime)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // revocation is the CRL service's job, fail-closed
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.VerificationTime = verificationTime;
        // The ignored-validity flag still reports NotTimeValid; any other status is a real chain failure.
        return chain.Build(certificate) && chain.ChainStatus.All(status => status.Status == X509ChainStatusFlags.NotTimeValid)
            && chain.ChainElements.Skip(1).All(element => element.Certificate.NotBefore.ToUniversalTime() <= verificationTime
                && verificationTime <= element.Certificate.NotAfter.ToUniversalTime());
    }

    private static bool IsForClientAuthentication(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (usage is null || !usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ClientAuthentication)) return false;
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return keyUsage is null || (keyUsage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement)) != 0;
    }

    private bool HasAcceptableKey(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is not null) return rsa.KeySize >= options.MinimumRsaKeyBits;
        using var ecdsa = certificate.GetECDsaPublicKey();
        return ecdsa is not null && ecdsa.KeySize >= options.MinimumEcKeyBits;
    }
}

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Host.Security;

// How the API trusts the identity provider's TLS certificate when the provider sits behind a private CA (ADR-025). The anchors replace the
// operating system's roots for this one channel (discovery document and key set), so a certificate from any other CA is refused even if the
// machine trusts it; name and validity are still enforced. Revocation is not checked: the provider is a server the operator pins by CA,
// and the keys it serves are public by design.
public static class IdentityBackchannel
{
    public static X509Certificate2[] LoadAnchors(IEnumerable<string> paths)
    {
        var anchors = new List<X509Certificate2>();
        foreach (var path in paths)
        {
            try { anchors.Add(X509CertificateLoader.LoadCertificateFromFile(path)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException($"Identity:TrustedCaPaths names a file that is not a readable CA certificate ({path}).", exception);
            }
        }
        return [.. anchors];
    }

    public static HttpMessageHandler CreateHandler(X509Certificate2[] anchors) => new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) => Accepts(certificate, errors, anchors)
        }
    };

    internal static bool Accepts(X509Certificate? presented, SslPolicyErrors errors, X509Certificate2[] anchors)
    {
        // The platform's own verdict on the name and on the presence of a certificate stands; only its judgement of the chain is replaced.
        if (presented is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0) return false;
        using var certificate = new X509Certificate2(presented);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }
}

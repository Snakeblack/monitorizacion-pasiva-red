using Monitoring.Host.Ingestion;

namespace Monitoring.Host.Security;

public sealed class ProbeTrustOptions
{
    public const string Section = "Probes:Trust";

    // PEM or DER files of the CAs allowed to issue probe client certificates. Each one is a trust anchor and its own CRL issuer.
    public string[] CaCertificatePaths { get; set; } = [];
    public string[] CrlLocations { get; set; } = [];
    // A revocation status older than this (or past its NextUpdate) closes ingestion for every certificate of that CA.
    public TimeSpan MaxRevocationAge { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MinimumRsaKeyBits { get; set; } = 2048;
    public int MinimumEcKeyBits { get; set; } = 256;

    // True when certificate authentication is on. Outside Development/Testing it is mandatory and must be fully and safely
    // configured: at least one CA, HTTP(S) CRL locations, a revocation window of at most five minutes refreshed more often than that.
    public bool Validate(IHostEnvironment environment)
    {
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (local && CaCertificatePaths.Length == 0) return false;
        if (CaCertificatePaths.Length == 0 || CaCertificatePaths.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Probes:Trust:CaCertificatePaths must name the CA certificates allowed to issue probe certificates.");
        if (CrlLocations.Length == 0 || CrlLocations.Any(location => !Uri.TryCreate(location, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            throw new InvalidOperationException("Probes:Trust:CrlLocations must be absolute HTTP(S) URLs.");
        if (MaxRevocationAge <= TimeSpan.Zero || MaxRevocationAge > TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("Probes:Trust:MaxRevocationAge must be between zero and five minutes.");
        if (RefreshInterval <= TimeSpan.Zero || RefreshInterval >= MaxRevocationAge)
            throw new InvalidOperationException("Probes:Trust:RefreshInterval must be positive and shorter than MaxRevocationAge.");
        return true;
    }
}

// The registry binds a certificate (issuer + serial) to the site and sensor it may write as. The identity never comes from the subject.
public sealed record ProbeBinding(string SiteId, string SensorId, bool Active);

public interface IProbeRegistry
{
    Task<ProbeBinding?> FindAsync(string issuerSha256, string serialHex, CancellationToken cancellationToken);
}

public interface ICrlSource
{
    // Null when the list could not be retrieved; the caller treats that as "no new information", never as "nothing revoked".
    Task<byte[]?> FetchAsync(string location, CancellationToken cancellationToken);
}

// Stable, non-sensitive causes; they feed metrics and audit and never reveal chain details to the caller.
public sealed record ProbeIdentityResult(TrustedSensorIdentity? Identity, string? Cause)
{
    public static ProbeIdentityResult Accepted(TrustedSensorIdentity identity) => new(identity, null);
    public static ProbeIdentityResult Rejected(string cause) => new(null, cause);
}

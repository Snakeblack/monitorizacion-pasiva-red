namespace Monitoring.Host.Security;

public enum IdentityMode { Development, Oidc }

// Configuration of human identity. The explicit Development mode (a server-side trusted read scope) exists only for the
// Development and Testing environments; every other environment must use a fully configured OIDC provider or the host refuses to start.
public sealed class IdentityOptions
{
    public string? Mode { get; set; }
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    // Where the discovery document is read when the provider's issuer name is not reachable from the API (split horizon, ADR-024).
    // It never changes who is trusted: tokens are still validated against Authority as issuer and against the keys this document lists.
    public string? MetadataAddress { get; set; }
    // PEM or DER files of the CAs that may sign the provider's TLS certificate when it sits behind a private CA (ADR-025). When set they
    // replace the operating system's roots for the provider channel; when empty the platform's roots apply, as for any HTTPS client.
    public string[] TrustedCaPaths { get; set; } = [];
    public string RolesClaim { get; set; } = "roles";
    public string ScopesClaim { get; set; } = "monitoring_scopes";
    public int ClockSkewSeconds { get; set; } = 30;
    // How often the provider's signing keys are re-read even when no token names an unknown key. It bounds how long a key the provider has retired
    // (for example a compromised one) is still trusted (ADR-026).
    public int KeysRefreshMinutes { get; set; } = 5;

    public IdentityMode Resolve(IHostEnvironment environment)
    {
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var mode = Mode switch
        {
            null or "" when local => IdentityMode.Development,
            "Development" => IdentityMode.Development,
            "Oidc" => IdentityMode.Oidc,
            _ => throw new InvalidOperationException("Identity:Mode must be 'Oidc' (or 'Development' in Development/Testing).")
        };
        if (mode == IdentityMode.Development)
        {
            if (!local) throw new InvalidOperationException("The development read mode must not be enabled outside Development/Testing.");
            return mode;
        }
        if (string.IsNullOrWhiteSpace(Authority) || string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Identity:Authority and Identity:Audience are required for OIDC.");
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) || (authority.Scheme != Uri.UriSchemeHttps && !local))
            throw new InvalidOperationException("Identity:Authority must be an absolute HTTPS URL.");
        if (!string.IsNullOrWhiteSpace(MetadataAddress)
            && (!Uri.TryCreate(MetadataAddress, UriKind.Absolute, out var metadata) || (metadata.Scheme != Uri.UriSchemeHttps && !(local && metadata.Scheme == Uri.UriSchemeHttp))
                || !string.IsNullOrEmpty(metadata.UserInfo)))
            throw new InvalidOperationException("Identity:MetadataAddress must be an absolute HTTPS URL without credentials.");
        if (TrustedCaPaths.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Identity:TrustedCaPaths must name CA certificate files, none of them blank.");
        if (ClockSkewSeconds is < 0 or > 300) throw new InvalidOperationException("Identity:ClockSkewSeconds must be between 0 and 300.");
        if (KeysRefreshMinutes is < 5 or > 60) throw new InvalidOperationException("Identity:KeysRefreshMinutes must be between 5 and 60 (the token library does not refresh more often than every five minutes).");
        return mode;
    }
}

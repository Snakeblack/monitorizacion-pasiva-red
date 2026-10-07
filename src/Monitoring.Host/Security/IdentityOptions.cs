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
    public string RolesClaim { get; set; } = "roles";
    public string ScopesClaim { get; set; } = "monitoring_scopes";
    public int ClockSkewSeconds { get; set; } = 30;

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
        if (ClockSkewSeconds is < 0 or > 300) throw new InvalidOperationException("Identity:ClockSkewSeconds must be between 0 and 300.");
        return mode;
    }
}

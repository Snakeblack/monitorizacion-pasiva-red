using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Monitoring.Tests;

// In-process stand-in for Keycloak: signs tokens with local RSA keys and serves the key set the API trusts. It checks the API's
// validation rules (issuer, audience, signature, lifetime, algorithm, rotation); it is not Keycloak and proves nothing about its
// token shape beyond the claims documented in the design (roles, monitoring_scopes).
internal sealed class OidcTestIdp : IConfigurationManager<OpenIdConnectConfiguration>, IDisposable
{
    internal const string Issuer = "https://idp.test/realms/monitoring";
    internal const string Audience = "monitoring-api";

    private readonly Dictionary<string, RSA> _keys = [];
    private readonly object _gate = new();
    internal bool Unavailable { get; set; }
    internal string CurrentKeyId { get; private set; } = "key-1";

    internal OidcTestIdp() => _keys["key-1"] = RSA.Create(2048);

    internal void Rotate(string newKeyId, bool keepOld)
    {
        lock (_gate)
        {
            if (!keepOld) foreach (var old in _keys.Values) old.Dispose();
            if (!keepOld) _keys.Clear();
            _keys[newKeyId] = RSA.Create(2048);
            CurrentKeyId = newKeyId;
        }
    }

    internal string Issue(string? subject = "oidc|user-1", string[]? roles = null, (string Site, string Sensor)[]? scopes = null, string issuer = Issuer,
        string audience = Audience, DateTime? notBefore = null, DateTime? expires = null, RSA? signWith = null, string? keyId = null, string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var claims = new Dictionary<string, object>();
        if (subject is not null) claims["sub"] = subject;
        claims["roles"] = roles ?? [];
        claims["monitoring_scopes"] = (scopes ?? []).Select(scope => (object)new Dictionary<string, string> { ["site"] = scope.Site, ["sensor"] = scope.Sensor }).ToArray();
        RSA key;
        lock (_gate) key = signWith ?? _keys[keyId ?? CurrentKeyId];
        var credentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = keyId ?? CurrentKeyId }, algorithm);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Claims = claims, SigningCredentials = credentials,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1), Expires = expires ?? DateTime.UtcNow.AddMinutes(10), IssuedAt = DateTime.UtcNow.AddMinutes(-1)
        });
    }

    // alg=none: unsigned, which a correct validator must never accept.
    internal static string Unsigned(string subject = "oidc|user-1")
    {
        static string B64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = subject, ["iss"] = Issuer, ["aud"] = Audience, ["roles"] = new[] { "administrador-inventario" },
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        });
        return $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(payload)}.";
    }

    // alg-confusion attempt: an HMAC token whose secret is the public key material the API publishes.
    internal string HmacWithPublicKey()
    {
        byte[] secret;
        lock (_gate) secret = _keys[CurrentKeyId].ExportSubjectPublicKeyInfo();
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Claims = new Dictionary<string, object> { ["sub"] = "oidc|attacker", ["roles"] = new[] { "administrador-inventario" } },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secret) { KeyId = CurrentKeyId }, SecurityAlgorithms.HmacSha256),
            Expires = DateTime.UtcNow.AddMinutes(5)
        });
    }

    internal RSA ForeignKey() => RSA.Create(2048);

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        if (Unavailable) throw new InvalidOperationException("The identity provider is unreachable.");
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        lock (_gate)
            foreach (var (id, key) in _keys) configuration.SigningKeys.Add(new RsaSecurityKey(key.ExportParameters(false)) { KeyId = id });
        return Task.FromResult(configuration);
    }

    public void RequestRefresh() { }

    public void Dispose() { foreach (var key in _keys.Values) key.Dispose(); }
}

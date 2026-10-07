using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Monitoring.Domain.Security;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Sessions;

namespace Monitoring.Host.Security;

public enum AccessOutcome { Granted, Unauthenticated, Forbidden }

// The result of authenticating and authorizing one human operation. Cause is a minimal code, never a token or a fragment of one.
// ClientSelectorsAreAdvisory marks the development context, where the server-side scope wins over any selector the client sends.
public sealed record AccessDecision(AccessOutcome Outcome, string? Subject = null, IReadOnlyCollection<AuthorizedPair>? Scopes = null,
    string? Cause = null, bool ClientSelectorsAreAdvisory = false);

public interface IAccessProvider
{
    Task<AccessDecision> AuthorizeAsync(HttpContext context, Operation operation, CancellationToken cancellationToken);
}

// The explicit development read mode: a configured server-side scope, only in Development/Testing, never derived from the client.
public sealed class DevelopmentAccessProvider(IHostEnvironment environment, ITrustedSessionReadContextProvider contexts) : IAccessProvider
{
    public Task<AccessDecision> AuthorizeAsync(HttpContext context, Operation operation, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            return Task.FromResult(new AccessDecision(AccessOutcome.Unauthenticated, Cause: "development-mode-disabled"));
        var trusted = contexts.Resolve(context);
        return Task.FromResult(trusted is null
            ? new AccessDecision(AccessOutcome.Unauthenticated, Cause: "no-trusted-context")
            : new AccessDecision(AccessOutcome.Granted, trusted.Subject, [new AuthorizedPair(trusted.SiteId, trusted.SensorId)], ClientSelectorsAreAdvisory: true));
    }
}

// Validates the bearer token (issuer, audience, signature, lifetime, algorithm), then derives roles and scopes only from its claims.
public sealed class OidcAccessProvider(IdentityOptions options, ILogger<OidcAccessProvider> logger) : IAccessProvider
{
    private const int MaximumIdentifierLength = 128;

    public async Task<AccessDecision> AuthorizeAsync(HttpContext context, Operation operation, CancellationToken cancellationToken)
    {
        AuthenticateResult result;
        try
        {
            result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No trusted signing keys (identity provider unreachable): nobody is identified, so access is closed. The exception
            // can carry the provider's address, so only its type is logged.
            logger.LogWarning("Token validation could not complete ({FailureType}); access closed.", exception.GetType().Name);
            return new AccessDecision(AccessOutcome.Unauthenticated, Cause: "identity-provider-unavailable");
        }
        if (!result.Succeeded || result.Principal is null) return new AccessDecision(AccessOutcome.Unauthenticated, Cause: result.None ? "no-credentials" : "token-invalid");
        var subject = result.Principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject)) return new AccessDecision(AccessOutcome.Unauthenticated, Cause: "no-subject");

        var roles = result.Principal.FindAll(options.RolesClaim).Select(claim => claim.Value).Where(Roles.Known.Contains).ToHashSet(StringComparer.Ordinal);
        if (!RoleMatrix.Permits(roles, operation)) return new AccessDecision(AccessOutcome.Forbidden, subject, Cause: "role-not-permitted");
        var scopes = ReadScopes(result.Principal.FindAll(options.ScopesClaim).Select(claim => claim.Value));
        return scopes.Count == 0
            ? new AccessDecision(AccessOutcome.Forbidden, subject, Cause: "no-scope")
            : new AccessDecision(AccessOutcome.Granted, subject, scopes);
    }

    // Each scope claim value is a JSON object {"site":"...","sensor":"..."}; anything else is ignored, never widened.
    private static List<AuthorizedPair> ReadScopes(IEnumerable<string> values)
    {
        var scopes = new HashSet<AuthorizedPair>();
        foreach (var value in values)
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
                    || !root.TryGetProperty("site", out var site) || !root.TryGetProperty("sensor", out var sensor)
                    || site.ValueKind != JsonValueKind.String || sensor.ValueKind != JsonValueKind.String) continue;
                var (siteId, sensorId) = (site.GetString(), sensor.GetString());
                if (!string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(sensorId) && siteId.Length <= MaximumIdentifierLength && sensorId.Length <= MaximumIdentifierLength)
                    scopes.Add(new AuthorizedPair(siteId, sensorId));
            }
            catch (JsonException) { }
        }
        return [.. scopes];
    }
}

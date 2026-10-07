using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Monitoring.Host.Security;

public static class IdentityRegistration
{
    // Asymmetric algorithms only: a token signed with a shared secret (or not signed) is never accepted, which also closes the
    // classic "HMAC with the public key" confusion.
    private static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512
    ];

    public static IdentityMode AddMonitoringIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("Identity").Get<IdentityOptions>() ?? new IdentityOptions();
        var mode = options.Resolve(environment);
        services.AddSingleton(options);
        if (mode == IdentityMode.Development)
        {
            services.TryAddSingleton<IAccessProvider, DevelopmentAccessProvider>();
            return mode;
        }
        var authority = options.Authority!.TrimEnd('/');
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearer =>
        {
            bearer.Authority = authority;
            bearer.Audience = options.Audience;
            // Keep claim names exactly as issued (sub, roles, monitoring_scopes); no legacy remapping.
            bearer.MapInboundClaims = false;
            bearer.RequireHttpsMetadata = !(environment.IsDevelopment() || environment.IsEnvironment("Testing"));
            // Cached signing keys follow the provider's rotation; an unknown key id forces a refresh, bounded below.
            bearer.AutomaticRefreshInterval = TimeSpan.FromHours(1);
            bearer.RefreshInterval = TimeSpan.FromMinutes(1);
            // Any failure to authenticate, including having no trusted signing keys because the provider is unreachable, ends as a
            // controlled "not authenticated" (401 at the operation) instead of an unhandled exception; nothing from the token or the
            // provider's address is kept.
            bearer.Events = new JwtBearerEvents { OnAuthenticationFailed = context => { context.Fail("authentication-failed"); return Task.CompletedTask; } };
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = authority,
                ValidateAudience = true, ValidAudience = options.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                ValidAlgorithms = AllowedAlgorithms, ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds)
            };
        });
        services.AddSingleton<IAccessProvider, OidcAccessProvider>();
        return mode;
    }
}

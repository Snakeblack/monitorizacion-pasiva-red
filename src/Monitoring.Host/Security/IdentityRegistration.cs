using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
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
        var refresher = new SigningKeyRefresher(TimeSpan.FromSeconds(3));
        var anchors = options.TrustedCaPaths.Length == 0 ? [] : IdentityBackchannel.LoadAnchors(options.TrustedCaPaths);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearer =>
        {
            bearer.Authority = authority;
            bearer.Audience = options.Audience;
            if (!string.IsNullOrWhiteSpace(options.MetadataAddress)) bearer.MetadataAddress = options.MetadataAddress;
            if (anchors.Length > 0) bearer.BackchannelHttpHandler = IdentityBackchannel.CreateHandler(anchors);
            // Keep claim names exactly as issued (sub, roles, monitoring_scopes); no legacy remapping.
            bearer.MapInboundClaims = false;
            bearer.RequireHttpsMetadata = !(environment.IsDevelopment() || environment.IsEnvironment("Testing"));
            // Cached signing keys follow the provider's rotation: they are re-read on a schedule (so a retired key stops being trusted, ADR-026) and an
            // unknown key id forces a refresh, never more than once a minute.
            bearer.AutomaticRefreshInterval = TimeSpan.FromMinutes(options.KeysRefreshMinutes);
            bearer.RefreshInterval = TimeSpan.FromMinutes(1);
            // Any failure to authenticate, including having no trusted signing keys because the provider is unreachable, ends as a
            // controlled "not authenticated" (401 at the operation) instead of an unhandled exception; nothing from the token or the
            // provider's address is kept.
            bearer.Events = new JwtBearerEvents { OnAuthenticationFailed = context => RetryAfterKeyRefreshAsync(context, refresher) };
            // A provider that hangs must not hold key refreshes for the stock sixty seconds.
            bearer.BackchannelTimeout = TimeSpan.FromSeconds(10);
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = authority,
                ValidateAudience = true, ValidAudience = options.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                ValidAlgorithms = AllowedAlgorithms, ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds)
            };
        });
        // Runs after the bearer handler's own post-configuration, which is what creates the configuration manager.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, bearer =>
        {
            if (bearer.ConfigurationManager is BaseConfigurationManager manager) manager.LastKnownGoodLifetime = TimeSpan.Zero;
        });
        services.AddSingleton<IAccessProvider, OidcAccessProvider>();
        return mode;
    }

    // A token signed with a key the cache has not seen yet is what the first request after a rotation looks like. The stock handler asks for a
    // refresh but still rejects that request, which would sign users out at every rotation (ADR-026). Here the key set is re-read (see
    // SigningKeyRefresher for the bounds) and the same token is validated once more with exactly the same parameters; every other failure, or the
    // same failure again, ends as a controlled "not authenticated".
    private static async Task RetryAfterKeyRefreshAsync(AuthenticationFailedContext context, SigningKeyRefresher refresher)
    {
        if (context.Exception is SecurityTokenSignatureKeyNotFoundException
            && context.Options.ConfigurationManager is { } manager
            && context.Request.Headers.Authorization.ToString() is { } header
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            try
            {
                var configuration = await refresher.RefreshAsync(manager, new JsonWebToken(token).Kid, context.Options.RefreshInterval).ConfigureAwait(false);
                if (configuration is not null)
                {
                    var parameters = context.Options.TokenValidationParameters.Clone();
                    parameters.IssuerSigningKeys = configuration.SigningKeys;
                    var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters).ConfigureAwait(false);
                    if (result.IsValid)
                    {
                        context.Principal = new ClaimsPrincipal(result.ClaimsIdentity);
                        context.Success();
                        return;
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
            {
                // A token that cannot even be read is simply not authenticated.
            }
        }
        context.Fail("authentication-failed");
    }
}

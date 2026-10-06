using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Sessions;

namespace Monitoring.Tests;

public sealed class OidcIdentityTests : IDisposable
{
    private const string Day = "from=2026-10-05T12:00:00.000Z&to=2026-10-06T11:00:00Z";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly OidcTestIdp _idp = new();

    internal sealed class RecordingSearch : ISessionSearch
    {
        public List<SessionSearchRequest> Requests { get; } = [];
        public Task<SessionSearchPage> SearchAsync(SessionSearchRequest request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return Task.FromResult(new SessionSearchPage([], null, new SearchFreshness(FreshnessState.Current, Now, null)));
        }
    }

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    internal static WebApplicationFactory<Program> Host(OidcTestIdp idp, RecordingSearch search, string environment = "Production", Dictionary<string, string?>? settings = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Identity:Mode", "Oidc");
            builder.UseSetting("Identity:Authority", OidcTestIdp.Issuer);
            builder.UseSetting("Identity:Audience", OidcTestIdp.Audience);
            foreach (var (key, value) in settings ?? []) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionSearch>();
                services.AddSingleton<ISessionSearch>(search);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTime());
                // The API asks this provider for its keys instead of fetching a real discovery document.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.ConfigurationManager = idp);
            });
        });
        return factory;
    }

    private static async Task<HttpStatusCode> GetAsync(WebApplicationFactory<Program> factory, string? token, string query = Day, params (string Name, string Value)[] headers)
    {
        using var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var (name, value) in headers) client.DefaultRequestHeaders.Add(name, value);
        using var response = await client.GetAsync("/api/v1/sessions?" + query);
        return response.StatusCode;
    }

    private static readonly (string, string)[] ScopeA = [("site-a", "sensor-a")];

    [Fact]
    public async Task AValidTokenGrantsTheRolesAndTheScopesItCarries()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, _idp.Issue(roles: ["analista"], scopes: [("site-a", "sensor-a"), ("site-b", "sensor-b")])));
        var request = Assert.Single(search.Requests);
        Assert.Equal([new AuthorizedPair("site-a", "sensor-a"), new AuthorizedPair("site-b", "sensor-b")], request.Scope.OrderBy(pair => pair.SiteId));
        Assert.Equal("oidc|user-1", request.Subject);
    }

    [Fact]
    public async Task ASelectorOnlyNarrowsTheTokenScopesAndAForeignOneIs403()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        var token = _idp.Issue(roles: ["auditor"], scopes: [("site-a", "sensor-a"), ("site-b", "sensor-b")]);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, token, Day + "&siteId=site-b"));
        Assert.Equal([new AuthorizedPair("site-b", "sensor-b")], Assert.Single(search.Requests).Scope);
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(factory, token, Day + "&siteId=site-c"));
        Assert.Single(search.Requests);
    }

    [Fact]
    public async Task ARoleThatIsNotInTheMatrixOrNoScopesIsForbiddenNotUnauthenticated()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(factory, _idp.Issue(roles: ["realm-admin"], scopes: ScopeA)));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(factory, _idp.Issue(roles: [], scopes: ScopeA)));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(factory, _idp.Issue(roles: ["analista"], scopes: [])));
        Assert.Empty(search.Requests);
    }

    [Fact]
    public async Task NothingTheClientSendsCanGrantRolesOrScopes()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        var token = _idp.Issue(roles: ["auditor"], scopes: []);
        // Headers claiming roles, scopes or a trusted identity are ignored: the token alone decides.
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(factory, token, Day, ("X-Roles", "administrador-inventario"), ("X-Scopes", "site-a/sensor-a"),
            ("X-Site-Id", "site-a"), ("X-Sensor-Id", "sensor-a")));
        Assert.Empty(search.Requests);
    }

    public static TheoryData<string> RejectedTokens() => new()
    {
        "missing", "garbage", "expired", "not-yet-valid", "wrong-issuer", "wrong-audience", "foreign-signature", "unsigned", "hmac-with-public-key",
        "no-subject", "unknown-key-id", "empty-subject"
    };

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task ATokenThatCannotBeVerifiedGrantsNoIdentityAndIs401(string kind)
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        using var foreign = _idp.ForeignKey();
        string? token = kind switch
        {
            "missing" => null,
            "garbage" => "not.a.jwt",
            "expired" => _idp.Issue(roles: ["analista"], scopes: ScopeA, notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1)),
            "not-yet-valid" => _idp.Issue(roles: ["analista"], scopes: ScopeA, notBefore: DateTime.UtcNow.AddHours(1), expires: DateTime.UtcNow.AddHours(2)),
            "wrong-issuer" => _idp.Issue(roles: ["analista"], scopes: ScopeA, issuer: "https://evil.test/realms/monitoring"),
            "wrong-audience" => _idp.Issue(roles: ["analista"], scopes: ScopeA, audience: "another-api"),
            "foreign-signature" => _idp.Issue(roles: ["analista"], scopes: ScopeA, signWith: foreign),
            "unsigned" => OidcTestIdp.Unsigned(),
            "hmac-with-public-key" => _idp.HmacWithPublicKey(),
            "no-subject" => _idp.Issue(subject: null, roles: ["analista"], scopes: ScopeA),
            "empty-subject" => _idp.Issue(subject: "  ", roles: ["analista"], scopes: ScopeA),
            "unknown-key-id" => _idp.Issue(roles: ["analista"], scopes: ScopeA, signWith: foreign, keyId: "never-published"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(factory, token));
        Assert.Empty(search.Requests);
    }

    [Fact]
    public async Task WithoutTrustedKeysFromTheProviderAccessIsClosed()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        var token = _idp.Issue(roles: ["analista"], scopes: ScopeA);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, token));
        _idp.Unavailable = true;
        // A new key set cannot be obtained; a token signed by a key the API has not cached is not accepted.
        _idp.Rotate("key-2", keepOld: false);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(factory, _idp.Issue(roles: ["analista"], scopes: ScopeA)));
    }

    [Fact]
    public async Task KeyRotationIsFollowedAndARetiredKeyStopsBeingTrusted()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        var oldToken = _idp.Issue(roles: ["analista"], scopes: ScopeA);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, oldToken));
        _idp.Rotate("key-2", keepOld: true);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, _idp.Issue(roles: ["analista"], scopes: ScopeA)));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, oldToken)); // both published during the overlap
        _idp.Rotate("key-3", keepOld: false);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(factory, _idp.Issue(roles: ["analista"], scopes: ScopeA)));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(factory, oldToken));
    }

    [Fact]
    public async Task OidcModeNeverHonoursTheDevelopmentTrustedContext()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search, environment: "Testing", settings: new() { ["TrustedSessionRead:SiteId"] = "site-a", ["TrustedSessionRead:SensorId"] = "sensor-a" });
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(factory, null, Day, ("X-Site-Id", "site-a")));
        Assert.Empty(search.Requests);
    }

    [Theory]
    [InlineData("Production", "Development", null, null)]            // the explicit development read mode is never allowed in production
    [InlineData("Production", null, null, null)]                      // production without identity configuration
    [InlineData("Production", "Oidc", null, "monitoring-api")]        // authority missing
    [InlineData("Production", "Oidc", "https://idp.test/realms/monitoring", null)] // audience missing
    [InlineData("Production", "Oidc", "http://idp.test/realms/monitoring", "monitoring-api")] // plain HTTP authority
    [InlineData("Production", "Bogus", "https://idp.test/realms/monitoring", "monitoring-api")]
    [InlineData("Staging", "Development", null, null)]
    public void AnInsecureOrIncompleteProductionConfigurationPreventsStartup(string environment, string? mode, string? authority, string? audience)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (mode is not null) builder.UseSetting("Identity:Mode", mode);
            if (authority is not null) builder.UseSetting("Identity:Authority", authority);
            if (audience is not null) builder.UseSetting("Identity:Audience", audience);
        });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void DevelopmentAndTestingStillStartInTheExplicitDevelopmentReadModeByDefault()
    {
        foreach (var environment in new[] { "Development", "Testing" })
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
            using var client = factory.CreateClient();
            Assert.NotNull(client);
        }
    }

    [Fact]
    public async Task AHumanTokenNeverSubstitutesTheProbeIdentityOnIngestion()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            _idp.Issue(roles: ["administrador-inventario"], scopes: [("site", "sensor")]));
        // Even the most privileged person, with the batch addressed to their own scope, is not a probe.
        const string batch = "{\"schemaVersion\":1,\"batchId\":\"b\",\"siteId\":\"site\",\"sensorId\":\"sensor\",\"events\":[{\"eventId\":\"e\",\"occurredAt\":\"2026-10-06T10:00:00Z\",\"data\":{}}]}";
        using var response = await client.PostAsync("/api/v1/ingestion/batches", new StringContent(batch, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AProbeIdentityNeverAuthorizesReadingSessionsOrInventory()
    {
        var search = new RecordingSearch();
        using var factory = Host(_idp, search).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Monitoring.Host.Ingestion.ITrustedSensorIdentityProvider>();
            services.AddSingleton<Monitoring.Host.Ingestion.ITrustedSensorIdentityProvider>(new ProbeIdentity());
        }));
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/v1/sessions?" + Day, "/api/v1/sessions/event?siteId=site&sensorId=sensor", "/api/v1/inventory/devices", "/api/v1/inventory/candidates" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Empty(search.Requests);
    }

    private sealed class ProbeIdentity : Monitoring.Host.Ingestion.ITrustedSensorIdentityProvider
    {
        public Monitoring.Host.Ingestion.TrustedSensorIdentity? Resolve(HttpContext context) => new("site", "sensor");
    }

    public void Dispose() => _idp.Dispose();
}

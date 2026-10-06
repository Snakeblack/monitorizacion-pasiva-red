using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Monitoring.Host.Sessions;

namespace Monitoring.Tests;

public sealed class InventoryEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static (string, string)[] ScopeA => [("site", "sensor")];

    // Candidates X (site/sensor vlan 42) and Y (site/sensor-2 vlan 42) from projected observations.
    private async Task<(string Connection, OidcTestIdp Idp, WebApplicationFactory<Program> Host, Guid X, Guid Y)> SeedAsync()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "x", "site", "sensor", DeviceObservationContractTests.Observation("2026-10-06T10:00:00Z", "192.0.2.10"));
        await SessionTestDatabase.AcceptAsync(connection, "y", "site", "sensor-2", DeviceObservationContractTests.Observation("2026-10-06T10:01:00Z", "192.0.2.10"));
        await SessionTestDatabase.ProjectAsync(connection);
        var idp = new OidcTestIdp();
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            ProbeTestTrust.For(builder, "Production");
            builder.UseSetting("ConnectionStrings:Monitoring", connection);
            builder.UseSetting("Identity:Mode", "Oidc");
            builder.UseSetting("Identity:Authority", OidcTestIdp.Issuer);
            builder.UseSetting("Identity:Audience", OidcTestIdp.Audience);
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType is { } type && type.Namespace == typeof(SessionProjectionWorker).Namespace).ToArray()) services.Remove(descriptor);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.ConfigurationManager = idp);
            });
        });
        Guid Id(string sensor) => Guid.Parse(SessionTestDatabase.TextAsync(connection, $"SELECT candidate_id::text FROM monitoring.device_candidate WHERE sensor_id='{sensor}'").Result);
        return (connection, idp, host, Id("sensor"), Id("sensor-2"));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string? token)
    {
        var client = host.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task TheRoleMatrixHoldsOverHttpForEveryEndpoint()
    {
        var (connection, idp, host, x, _) = await SeedAsync();
        using var _ = idp; using var __ = host;
        // (role, GET devices, GET candidates, GET observations, POST device)
        var cases = new (string Role, HttpStatusCode Devices, HttpStatusCode Candidates, HttpStatusCode Observations, HttpStatusCode Create)[]
        {
            ("analista", HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("auditor", HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("administrador-inventario", HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Created)
        };
        foreach (var (role, devices, candidates, observations, create) in cases)
        {
            using var client = Client(host, idp.Issue(roles: [role], scopes: ScopeA));
            Assert.Equal(devices, (await client.GetAsync("/api/v1/inventory/devices")).StatusCode);
            Assert.Equal(candidates, (await client.GetAsync("/api/v1/inventory/candidates")).StatusCode);
            Assert.Equal(observations, (await client.GetAsync("/api/v1/inventory/observations")).StatusCode);
            Assert.Equal(create, (await client.PostAsync("/api/v1/inventory/devices", Json(new { name = $"d-{role}", description = "", siteId = "site", sensorId = "sensor" }))).StatusCode);
            // Decisions on a candidate: only the administrator, and an analyst or auditor never reaches the data.
            var decision = await client.PostAsync($"/api/v1/inventory/candidates/{x}/reject", Json(new { expectedRevision = 99, reason = "nope" }));
            Assert.Equal(role == "administrador-inventario" ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden, decision.StatusCode);
        }
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
    }

    [Fact]
    public async Task AnonymousAndInvalidTokensGet401OnEveryInventoryRoute()
    {
        var (_, idp, host, x, _) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        foreach (var token in new string?[] { null, "garbage", idp.Issue(roles: ["administrador-inventario"], scopes: ScopeA, audience: "other") })
        {
            using var client = Client(host, token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/inventory/devices")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/inventory/candidates")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/v1/inventory/candidates/{x}/confirm", Json(new { expectedRevision = 1, deviceName = "n", deviceDescription = "" }))).StatusCode);
        }
    }

    [Fact]
    public async Task AdministratorsConfirmMergeAndCorrectWithRevisionsAndGet409OnStaleOnes()
    {
        var (connection, idp, host, x, y) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        using var admin = Client(host, idp.Issue(subject: "oidc|admin", roles: ["administrador-inventario"], scopes: [("site", "sensor"), ("site", "sensor-2")]));

        var confirm = await admin.PostAsync($"/api/v1/inventory/candidates/{x}/confirm", Json(new { expectedRevision = 1, deviceName = "Printer", deviceDescription = "2F" }));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using var confirmed = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync());
        Assert.Equal(("confirmed", 2), (confirmed.RootElement.GetProperty("state").GetString(), confirmed.RootElement.GetProperty("revision").GetInt32()));
        var deviceId = confirmed.RootElement.GetProperty("deviceId").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/inventory/candidates/{x}/confirm", Json(new { expectedRevision = 1, deviceName = "Again", deviceDescription = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/inventory/candidates/{y}/merge", Json(new { expectedRevision = 1, deviceId, expectedDeviceRevision = 7 }))).StatusCode);
        var merge = await admin.PostAsync($"/api/v1/inventory/candidates/{y}/merge", Json(new { expectedRevision = 1, deviceId, expectedDeviceRevision = 1 }));
        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);

        var patch = await admin.PatchAsync($"/api/v1/inventory/devices/{deviceId}", Json(new { expectedRevision = 2, name = "Printer (renamed)", description = "2F" }));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsync($"/api/v1/inventory/devices/{deviceId}", Json(new { expectedRevision = 2, name = "Stale", description = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsync($"/api/v1/inventory/devices/{Guid.NewGuid()}", Json(new { expectedRevision = 1, name = "x", description = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/inventory/candidates/{Guid.NewGuid()}/reject", Json(new { expectedRevision = 1, reason = "x" }))).StatusCode);
        // Every successful change is audited with the OIDC subject.
        Assert.Equal(3L, await Count(connection, "SELECT count(*) FROM monitoring.inventory_audit WHERE actor='oidc|admin'"));
    }

    [Fact]
    public async Task InvalidAndOversizedBodiesAreRejectedWithoutTouchingTheInventory()
    {
        var (connection, idp, host, x, _) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        using var admin = Client(host, idp.Issue(roles: ["administrador-inventario"], scopes: ScopeA));
        foreach (var body in new[]
        {
            "not json", "[]", "{}", "{\"name\":\"\",\"description\":\"\",\"siteId\":\"site\",\"sensorId\":\"sensor\"}",
            "{\"name\":\"n\",\"description\":\"\",\"siteId\":\"site\",\"sensorId\":\"sensor\",\"isAdmin\":true}",   // unknown field
            "{\"name\":\"n\",\"description\":\"\",\"siteId\":\"site\"}",                                          // missing field
            "{\"name\":12,\"description\":\"\",\"siteId\":\"site\",\"sensorId\":\"sensor\"}"
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/inventory/devices", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        var huge = new StringContent("{\"name\":\"" + new string('n', 100_000) + "\",\"description\":\"\",\"siteId\":\"site\",\"sensorId\":\"sensor\"}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await admin.PostAsync("/api/v1/inventory/devices", huge)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/v1/inventory/candidates/not-a-guid/reject", Json(new { expectedRevision = 1, reason = "x" }))).StatusCode);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
    }

    [Fact]
    public async Task ScopesComeOnlyFromTheTokenAndForeignOnesAreForbiddenWithoutLeakingData()
    {
        var (connection, idp, host, x, y) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        using var onlyA = Client(host, idp.Issue(roles: ["administrador-inventario"], scopes: ScopeA));
        // Creating in a scope the token does not hold, deciding on a foreign candidate, and listing foreign data.
        Assert.Equal(HttpStatusCode.Forbidden, (await onlyA.PostAsync("/api/v1/inventory/devices", Json(new { name = "n", description = "", siteId = "site", sensorId = "sensor-2" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await onlyA.PostAsync($"/api/v1/inventory/candidates/{y}/reject", Json(new { expectedRevision = 1, reason = "x" }))).StatusCode);
        using var listing = JsonDocument.Parse(await (await onlyA.GetAsync("/api/v1/inventory/candidates")).Content.ReadAsStringAsync());
        Assert.Equal([x.ToString()], listing.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("candidateId").GetString()));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.inventory_audit"));
    }

    [Fact]
    public async Task ListingsPageWithSealedCursorsBoundToTheCallerAndTheQuery()
    {
        var (_, idp, host, _, _) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        var scopes = new[] { ("site", "sensor"), ("site", "sensor-2") };
        using var analyst = Client(host, idp.Issue(subject: "oidc|a", roles: ["analista"], scopes: scopes));
        using var first = JsonDocument.Parse(await (await analyst.GetAsync("/api/v1/inventory/candidates?pageSize=1")).Content.ReadAsStringAsync());
        Assert.Single(first.RootElement.GetProperty("items").EnumerateArray());
        var cursor = first.RootElement.GetProperty("nextCursor").GetString()!;
        Assert.DoesNotContain("site", cursor);
        using var second = JsonDocument.Parse(await (await analyst.GetAsync($"/api/v1/inventory/candidates?pageSize=1&cursor={Uri.EscapeDataString(cursor)}")).Content.ReadAsStringAsync());
        Assert.Single(second.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
        // A different page size or filter, an altered cursor and another person are all refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await analyst.GetAsync($"/api/v1/inventory/candidates?pageSize=2&cursor={Uri.EscapeDataString(cursor)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await analyst.GetAsync($"/api/v1/inventory/candidates?pageSize=1&state=rejected&cursor={Uri.EscapeDataString(cursor)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await analyst.GetAsync($"/api/v1/inventory/candidates?pageSize=1&cursor={Uri.EscapeDataString(cursor[..^4] + "AAAA")}")).StatusCode);
        using var other = Client(host, idp.Issue(subject: "oidc|b", roles: ["analista"], scopes: scopes));
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/v1/inventory/candidates?pageSize=1&cursor={Uri.EscapeDataString(cursor)}")).StatusCode);
        foreach (var invalid in new[] { "pageSize=0", "pageSize=101", "pageSize=x", "state=deleted", "unknown=1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await analyst.GetAsync("/api/v1/inventory/candidates?" + invalid)).StatusCode);
    }

    [Fact]
    public async Task DenialsAreAuditedWithTheActorOperationAndCauseButNeverTokensOrData()
    {
        var (connection, idp, host, x, _) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        var token = idp.Issue(subject: "oidc|auditor-7", roles: ["auditor"], scopes: ScopeA);
        using var auditor = Client(host, token);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.GetAsync("/api/v1/inventory/candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.PostAsync($"/api/v1/inventory/candidates/{x}/reject", Json(new { expectedRevision = 1, reason = "x" }))).StatusCode);
        using var anonymous = Client(host, "garbage-token-value");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/inventory/devices")).StatusCode);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.access_denial_audit WHERE subject='oidc|auditor-7' AND operation='ReadCandidatesAndObservations' AND cause='role-not-permitted' AND correlation_id <> ''"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.access_denial_audit WHERE subject='oidc|auditor-7' AND operation='ManageInventory' AND cause='role-not-permitted'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.access_denial_audit WHERE subject IS NULL AND operation='ReadInventory' AND cause='token-invalid'"));
        // Nothing sensitive is stored: not the token, not a fragment of it, not addresses or event data.
        Assert.Equal(0L, await Count(connection, $"SELECT count(*) FROM monitoring.access_denial_audit a WHERE a::text LIKE '%garbage-token-value%' OR a::text LIKE '%{token[..20]}%' OR a::text LIKE '%192.0.2%' OR a::text LIKE '%aa:bb:cc%'"));
    }

    [Fact]
    public async Task ASessionsReadDeniedForAForeignSelectorIsAuditedToo()
    {
        var (connection, idp, host, _, _) = await SeedAsync();
        using var _1 = idp; using var _2 = host;
        using var client = Client(host, idp.Issue(subject: "oidc|analyst", roles: ["analista"], scopes: ScopeA));
        var now = DateTime.UtcNow;
        var query = $"from={Uri.EscapeDataString(now.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))}&to={Uri.EscapeDataString(now.AddSeconds(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))}&siteId=other";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/sessions?" + query)).StatusCode);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.access_denial_audit WHERE subject='oidc|analyst' AND operation='ReadSessions' AND cause='scope-not-authorized'"));
    }
}

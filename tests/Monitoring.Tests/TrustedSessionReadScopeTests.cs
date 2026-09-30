using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Monitoring.Host.Sessions;

namespace Monitoring.Tests;

public sealed class TrustedSessionReadScopeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task ConfiguredScopeReturnsFivePersistedFieldsWithoutClientScopeHeaders(string environment)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event", "site-a", "sensor-a");
        await SessionTestDatabase.ProjectAsync(connection);
        using var factory = CreateHost(environment, connection, "site-a", "sensor-a");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sessions/event");
        Assert.False(request.Headers.Contains("X-Site-Id"));
        Assert.False(request.Headers.Contains("X-Sensor-Id"));
        Assert.Equal("/api/v1/sessions/event", request.RequestUri!.OriginalString);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "data", "eventId", "occurredAt", "sensorId", "siteId" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("event", root.GetProperty("eventId").GetString());
        Assert.Equal("site-a", root.GetProperty("siteId").GetString());
        Assert.Equal("sensor-a", root.GetProperty("sensorId").GetString());
        Assert.Equal("2026-09-29T12:00:00.100Z", root.GetProperty("occurredAt").GetString());
        Assert.Equal("TCP", root.GetProperty("data").GetProperty("protocol").GetString());
        Assert.Equal("192.0.2.1", root.GetProperty("data").GetProperty("sourceIp").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task ClientHeadersAndQueryDoNotReplaceConfiguredScope(string environment)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "shared", "site-a", "sensor-a");
        await SessionTestDatabase.AcceptAsync(connection, "shared", "other-site", "other-sensor",
            SyntheticSessionContractTests.ValidData.Replace("TCP", "UDP", StringComparison.Ordinal));
        await SessionTestDatabase.ProjectAsync(connection);
        using var factory = CreateHost(environment, connection, "site-a", "sensor-a");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Site-Id", "other-site");
        client.DefaultRequestHeaders.Add("X-Sensor-Id", "other-sensor");
        using var response = await client.GetAsync("/api/v1/sessions/shared?siteId=other-site&sensorId=other-sensor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("site-a", root.GetProperty("siteId").GetString());
        Assert.Equal("sensor-a", root.GetProperty("sensorId").GetString());
        Assert.Equal("TCP", root.GetProperty("data").GetProperty("protocol").GetString());
    }

    [Fact]
    public async Task EventOutsideConfiguredScopeReturnsNotFoundWithoutForeignBody()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "foreign", "other-site", "other-sensor",
            SyntheticSessionContractTests.ValidData.Replace("TCP", "UDP", StringComparison.Ordinal));
        await SessionTestDatabase.ProjectAsync(connection);
        using var factory = CreateHost("Testing", connection, "site-a", "sensor-a");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/sessions/foreign");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("other-site", body, StringComparison.Ordinal);
        Assert.DoesNotContain("other-sensor", body, StringComparison.Ordinal);
        Assert.DoesNotContain("UDP", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionLeavesFeatureUnsetAndReturnsUnauthorized()
    {
        var probe = new FeatureProbe();
        var reader = new FailingReader();
        using var factory = CreateHost("Production", siteId: "site-a", sensorId: "sensor-a", configure: services =>
        {
            services.AddSingleton(probe);
            services.AddSingleton<IStartupFilter>(new FeatureProbeFilter(probe));
            services.RemoveAll<ISessionReader>();
            services.AddSingleton<ISessionReader>(reader);
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/sessions/event")).StatusCode);
        Assert.Equal(0, reader.Calls);
        Assert.Null(probe.Feature);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task MissingConfigurationReturnsUnauthorizedWithoutReading(string environment)
    {
        var reader = new FailingReader();
        using var factory = CreateHost(environment, configure: services =>
        {
            services.RemoveAll<ISessionReader>();
            services.AddSingleton<ISessionReader>(reader);
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/sessions/event")).StatusCode);
        Assert.Equal(0, reader.Calls);
    }

    private static WebApplicationFactory<Program> CreateHost(
        string environment,
        string? connection = null,
        string? siteId = null,
        string? sensorId = null,
        Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Monitoring", connection ?? "");
            if (siteId is not null)
            {
                builder.UseSetting("TrustedSessionRead:SiteId", siteId);
            }

            if (sensorId is not null)
            {
                builder.UseSetting("TrustedSessionRead:SensorId", sensorId);
            }

            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType == typeof(SessionProjectionWorker)).ToArray())
                {
                    services.Remove(descriptor);
                }

                configure?.Invoke(services);
            });
        });

    private sealed class FeatureProbe
    {
        public ITrustedSessionReadContextFeature? Feature { get; set; }
    }

    private sealed class FeatureProbeFilter(FeatureProbe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (HttpContext context, RequestDelegate nextMiddleware) =>
                {
                    await nextMiddleware(context);
                    probe.Feature = context.Features.Get<ITrustedSessionReadContextFeature>();
                });
                next(app);
            };
    }

    private sealed class FailingReader : ISessionReader
    {
        public int Calls { get; private set; }

        public Task<Monitoring.Domain.Sessions.SessionDetail?> FindAsync(
            string siteId, string sensorId, string eventId, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Reader must not execute without a trusted read context.");
        }
    }
}

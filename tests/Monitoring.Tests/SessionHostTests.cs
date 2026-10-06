using System.Net;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Monitoring.Host.Ingestion;
using Monitoring.Host.Sessions;

namespace Monitoring.Tests;

public sealed class SessionHostTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task DetailReturnsExactlyFiveFieldsAndSharedIdStaysInTrustedScope(string environment)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "shared", "site-a", "sensor-a");
        await SessionTestDatabase.AcceptAsync(connection, "shared", "site-b", "sensor-a",
            SyntheticSessionContractTests.ValidData.Replace("TCP", "UDP", StringComparison.Ordinal));
        await SessionTestDatabase.AcceptAsync(connection, "shared", "site-a", "sensor-b",
            SyntheticSessionContractTests.ValidData.Replace("192.0.2.1", "192.0.2.9", StringComparison.Ordinal));
        await SessionTestDatabase.AcceptAsync(connection, "other-only", "site-b", "sensor-a");
        await SessionTestDatabase.ProjectAsync(connection);
        foreach (var (site, sensor, expectedProtocol, expectedIp) in new[]
        {
            ("site-a", "sensor-a", "TCP", "192.0.2.1"),
            ("site-b", "sensor-a", "UDP", "192.0.2.1"),
            ("site-a", "sensor-b", "TCP", "192.0.2.9")
        })
        {
            using var factory = CreateHost(connection, new(site, sensor), environment);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Site-Id", "site-b");
            client.DefaultRequestHeaders.Add("X-Sensor-Id", "sensor-a");
            using var response = await client.GetAsync("/api/v1/sessions/shared?siteId=site-b&sensorId=sensor-a");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(new[] { "data", "eventId", "occurredAt", "sensorId", "siteId" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal("shared", root.GetProperty("eventId").GetString());
            Assert.Equal(site, root.GetProperty("siteId").GetString());
            Assert.Equal(sensor, root.GetProperty("sensorId").GetString());
            Assert.Equal("2026-09-29T12:00:00.100Z", root.GetProperty("occurredAt").GetString());
            Assert.Equal(expectedProtocol, root.GetProperty("data").GetProperty("protocol").GetString());
            Assert.Equal(expectedIp, root.GetProperty("data").GetProperty("sourceIp").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/sessions/missing")).StatusCode);
            if (site == "site-a")
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/sessions/other-only?siteId=site-b")).StatusCode);
            }
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Unknown")]
    public async Task EnvironmentGuardRejectsEvenSubstitutedProviderBeforeReading(string environment)
    {
        using var factory = CreateHost(null, new("site", "sensor"), environment);
        var reader = new FailingReader();
        using var guarded = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionReader>();
            services.AddSingleton<ISessionReader>(reader);
        }));
        using var client = guarded.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/sessions/event")).StatusCode);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task SensorIdentityHeadersAndQueryDoNotAuthorizeReading()
    {
        using var factory = CreateHost(null, null, "Testing");
        using var sensorHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedSensorIdentityProvider>();
            services.AddSingleton<ITrustedSensorIdentityProvider>(new SensorProvider());
        }));
        using var client = sensorHost.CreateClient();
        client.DefaultRequestHeaders.Add("X-Site-Id", "site");
        client.DefaultRequestHeaders.Add("X-Sensor-Id", "sensor");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/sessions/event?siteId=site&sensorId=sensor")).StatusCode);
    }

    [Fact]
    public void ProviderReadsOnlySeparateServerFeature()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Site-Id"] = "untrusted";
        context.Features.Set<ITrustedSensorIdentityFeature>(new SensorFeature());
        var provider = new HostContextTrustedSessionReadContextProvider();
        Assert.Null(provider.Resolve(context));
        context.Features.Set<ITrustedSessionReadContextFeature>(new ReadFeature(new("trusted-site", "trusted-sensor")));
        Assert.Equal(new TrustedSessionReadContext("trusted-site", "trusted-sensor"), provider.Resolve(context));
    }

    [Fact]
    public async Task ConfiguredHostRunsProjectionWorkerAndMigrationDoesNotConsumePending()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        Assert.Equal(0, (await MigrationProcess.RunAsync(connection)).ExitCode);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        using var factory = CreateHost(connection, new("site", "sensor"));
        using var client = factory.CreateClient();
        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<SessionProjectionWorker>());
        await SessionWorkerTests.WaitAsync(async () => await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL") == 1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/sessions/event")).StatusCode);
    }

    [Fact]
    public async Task AckPrecedesControlledProjectionAndResendDoesNotConsumeQuotaAgain()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var factory = CreateHost(connection, new("site", "sensor"), suppressWorker: true)
            .WithWebHostBuilder(builder => builder.UseSetting("Ingestion:MaxNewEventsPerMinute", "500"))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSensorIdentityProvider>();
                services.AddSingleton<ITrustedSensorIdentityProvider>(new SensorProvider());
            }));
        using var client = factory.CreateClient();
        var batch = """
            {"schemaVersion":1,"batchId":"batch","siteId":"site","sensorId":"sensor","events":[
            {"eventId":"event","occurredAt":"2026-09-29T12:00:00.100Z","data":DATA},
            {"eventId":"arbitrary","occurredAt":"2026-09-29T12:00:00Z","data":{"arbitrary":true}}]}
            """.Replace("DATA", SyntheticSessionContractTests.ValidData, StringComparison.Ordinal);
        using var ack = await client.PostAsync("/api/v1/ingestion/batches", new StringContent(batch, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        Assert.Empty(await ack.Content.ReadAsStringAsync());
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.ingestion_inbox WHERE event_id='arbitrary' AND processed_at IS NULL"));
        await using (var db = SessionTestDatabase.Context(connection))
        {
            using var document = JsonDocument.Parse("{}");
            var events = Enumerable.Range(0, 498).Select(index => new Monitoring.Domain.Ingestion.IngestionEvent(
                $"quota-{index}", "2026-09-29T12:00:00Z", document.RootElement.Clone())).ToArray();
            Assert.Equal(Monitoring.Persistence.Ingestion.InboxWriteResult.Accepted,
                await new Monitoring.Persistence.Ingestion.InboxWriter(db, new Monitoring.Persistence.Ingestion.IngestionOptions { MaxNewEventsPerMinute = 500 }).WriteAsync("site", "sensor",
                    new Monitoring.Domain.Ingestion.IngestionBatch(1, "quota", "site", "sensor", events), default));
        }
        using var resend = await client.PostAsync("/api/v1/ingestion/batches", new StringContent(batch, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        Assert.Empty(await resend.Content.ReadAsStringAsync());
        Assert.Equal(500L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        var overQuota = batch.Replace("\"eventId\":\"event\"", "\"eventId\":\"over-quota\"", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsync("/api/v1/ingestion/batches", new StringContent(overQuota, Encoding.UTF8, "application/json"))).StatusCode);
    }

    internal static WebApplicationFactory<Program> CreateHost(string? connection, TrustedSessionReadContext? context, string environment = "Testing", bool suppressWorker = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Monitoring", connection ?? "");
            builder.ConfigureTestServices(services =>
            {
                if (suppressWorker)
                {
                    foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                        && service.ImplementationType == typeof(SessionProjectionWorker)).ToArray())
                    {
                        services.Remove(descriptor);
                    }
                }
                services.RemoveAll<ITrustedSessionReadContextProvider>();
                services.AddSingleton<ITrustedSessionReadContextProvider>(new ReadProvider(context));
            });
        });

    private sealed record ReadFeature(TrustedSessionReadContext Context) : ITrustedSessionReadContextFeature;
    private sealed class SensorFeature : ITrustedSensorIdentityFeature
    {
        public TrustedSensorIdentity Identity => new("site", "sensor");
    }
    internal sealed class ReadProvider(TrustedSessionReadContext? context) : ITrustedSessionReadContextProvider
    {
        public TrustedSessionReadContext? Resolve(HttpContext httpContext) => context;
    }
    private sealed class SensorProvider : ITrustedSensorIdentityProvider
    {
        public TrustedSensorIdentity? Resolve(HttpContext context) => new("site", "sensor");
    }
    private sealed class FailingReader : ISessionReader
    {
        public int Calls { get; private set; }
        public Task<Monitoring.Domain.Sessions.SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Reader must not execute before the environment guard.");
        }
    }
}

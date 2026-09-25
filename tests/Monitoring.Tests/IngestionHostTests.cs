using System.Net;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Domain.Ingestion;
using Monitoring.Host.Ingestion;

namespace Monitoring.Tests;

public sealed class IngestionHostTests
{
    private const string Route = "/api/v1/ingestion/batches";
    private const string ValidBatch = "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[{\"eventId\":\"event-1\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":{\"value\":7}}]}";

    [Fact]
    public async Task InvalidBodyReturnsBadRequestBeforeCallingWriter()
    {
        using var factory = new IngestionHostFactory(new TrustedSensorIdentity("site-1", "sensor-1"));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, Json("{"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Writer.CallCount);
    }

    [Fact]
    public void HostIdentityProviderIgnoresClientHeadersAndClaims()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Site-Id"] = "site-1";
        context.Request.Headers["X-Sensor-Id"] = "sensor-1";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("site_id", "site-1"), new Claim("sensor_id", "sensor-1")],
            "untrusted"));

        Assert.Null(new HostContextTrustedSensorIdentityProvider().Resolve(context));
    }

    [Fact]
    public async Task MissingTrustedIdentityReturnsUnauthorizedAndIgnoresClientHeadersAndClaims()
    {
        using var factory = new IngestionHostFactory(identity: null, configureIdentity: false);
        using var client = factory.CreateClient();
        var metrics = factory.Services.GetRequiredService<IngestionRejectionMetrics>();
        var measurements = new List<MetricMeasurement>();
        using var listener = ListenFor(metrics, measurements);
        client.DefaultRequestHeaders.Add("X-Site-Id", "site-1");
        client.DefaultRequestHeaders.Add("X-Sensor-Id", "sensor-1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "site_id=site-1;sensor_id=sensor-1");

        var response = await client.PostAsync(Route, Json(ValidBatch));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Writer.CallCount);
        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public async Task RejectedRequestsIncrementOnceWithTrustedIdentityAndExcludeInternalErrors()
    {
        var identity = new TrustedSensorIdentity("trusted-site", "trusted-sensor");
        using var factory = new IngestionHostFactory(identity);
        using var client = factory.CreateClient();
        var metrics = factory.Services.GetRequiredService<IngestionRejectionMetrics>();
        var measurements = new List<MetricMeasurement>();
        using var listener = ListenFor(metrics, measurements);
        var matchingBatch = ValidBatch
            .Replace("site-1", identity.SiteId, StringComparison.Ordinal)
            .Replace("sensor-1", identity.SensorId, StringComparison.Ordinal);

        using var badRequest = await client.PostAsync(Route, Json("{"));
        using var forbidden = await client.PostAsync(Route, Json(ValidBatch.Replace("site-1", "client-site", StringComparison.Ordinal)));
        factory.Writer.Result = IngestionWriteResult.Conflict;
        using var conflict = await client.PostAsync(Route, Json(matchingBatch));
        factory.Writer.Result = IngestionWriteResult.RateLimited;
        using var rateLimited = await client.PostAsync(Route, Json(matchingBatch));
        factory.Writer.Result = IngestionWriteResult.Failed;
        using var internalError = await client.PostAsync(Route, Json(matchingBatch));

        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimited.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, internalError.StatusCode);
        Assert.Equal(4, measurements.Count);
        Assert.All(measurements, measurement =>
        {
            Assert.Equal("monitoring.ingestion.rejections", measurement.Name);
            Assert.Equal(1, measurement.Value);
            Assert.Equal(
                new Dictionary<string, object?>
                {
                    ["site.id"] = identity.SiteId,
                    ["sensor.id"] = identity.SensorId
                },
                measurement.Tags.ToDictionary(tag => tag.Key, tag => tag.Value));
        });
    }

    [Fact]
    public async Task MismatchedTrustedIdentityReturnsForbiddenBeforeCallingWriter()
    {
        using var factory = new IngestionHostFactory(new TrustedSensorIdentity("other-site", "sensor-1"));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, Json(ValidBatch));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Writer.CallCount);
    }

    [Fact]
    public async Task OversizedBodyReturnsBadRequestBeforeCallingWriter()
    {
        using var factory = new IngestionHostFactory(new TrustedSensorIdentity("site-1", "sensor-1"));
        using var client = factory.CreateClient();
        var oversizedBody = new string('x', BatchContract.MaximumBodyBytes + 1);

        var response = await client.PostAsync(Route, Json(oversizedBody));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Writer.CallCount);
    }

    [Fact]
    public async Task AcceptedBatchReturnsEmptyOkOnlyAfterWriterAcceptsIt()
    {
        var identity = new TrustedSensorIdentity("site-1", "sensor-1");
        using var factory = new IngestionHostFactory(identity, IngestionWriteResult.Accepted);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, Json(ValidBatch));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body);
        Assert.Equal(1, factory.Writer.CallCount);
        Assert.Equal(identity, factory.Writer.LastIdentity);
        Assert.Equal("batch-1", factory.Writer.LastBatch?.BatchId);
    }

    [Fact]
    public async Task UnconfiguredWriterReturnsInternalServerErrorInsteadOfAcknowledging()
    {
        using var factory = new IngestionHostFactory(
            new TrustedSensorIdentity("site-1", "sensor-1"),
            configureWriter: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, Json(ValidBatch));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private static MeterListener ListenFor(IngestionRejectionMetrics metrics, ICollection<MetricMeasurement> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter))
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private sealed record MetricMeasurement(string Name, long Value, KeyValuePair<string, object?>[] Tags);

    private sealed class IngestionHostFactory : WebApplicationFactory<Program>
    {
        private readonly TrustedSensorIdentity? _identity;
        private readonly bool _configureIdentity;
        private readonly bool _configureWriter;

        public IngestionHostFactory(
            TrustedSensorIdentity? identity,
            IngestionWriteResult writerResult = IngestionWriteResult.Failed,
            bool configureIdentity = true,
            bool configureWriter = true)
        {
            _identity = identity;
            _configureIdentity = configureIdentity;
            _configureWriter = configureWriter;
            Writer = new FakeBatchWriter(writerResult);
        }

        public FakeBatchWriter Writer { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                if (_configureIdentity)
                {
                    services.RemoveAll<ITrustedSensorIdentityProvider>();
                    services.AddSingleton<ITrustedSensorIdentityProvider>(new FixedIdentityProvider(_identity));
                }

                if (_configureWriter)
                {
                    services.RemoveAll<IIngestionBatchWriter>();
                    services.AddSingleton<IIngestionBatchWriter>(Writer);
                }
            });
        }
    }

    private sealed class FixedIdentityProvider(TrustedSensorIdentity? identity) : ITrustedSensorIdentityProvider
    {
        public TrustedSensorIdentity? Resolve(HttpContext context) => identity;
    }

    private sealed class FakeBatchWriter(IngestionWriteResult result) : IIngestionBatchWriter
    {
        public IngestionWriteResult Result { get; set; } = result;

        public int CallCount { get; private set; }

        public TrustedSensorIdentity? LastIdentity { get; private set; }

        public IngestionBatch? LastBatch { get; private set; }

        public Task<IngestionWriteResult> WriteAsync(
            TrustedSensorIdentity identity,
            IngestionBatch batch,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastIdentity = identity;
            LastBatch = batch;
            return Task.FromResult(Result);
        }
    }
}

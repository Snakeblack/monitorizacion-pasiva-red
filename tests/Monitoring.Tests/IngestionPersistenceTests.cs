using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Domain.Ingestion;
using Monitoring.Host.Ingestion;
using Monitoring.Persistence;
using Monitoring.Persistence.Ingestion;
using Npgsql;

namespace Monitoring.Tests;

public sealed class IngestionPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Route = "/api/v1/ingestion/batches";
    private const string Reordered = "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[{\"eventId\":\"event-1\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":{\"right\":2,\"items\":[1,2],\"left\":1}}]}";
    private const string ReorderedArray = "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[{\"eventId\":\"event-1\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":{\"items\":[2,1],\"right\":2,\"left\":1}}]}";

    [Fact]
    public async Task AckFollowsCommitAndReorderedObjectResendIsIdempotent()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.DoesNotContain("Unconfigured", scope.ServiceProvider.GetRequiredService<IIngestionBatchWriter>().GetType().Name);
        }

        using var first = await client.PostAsync(Route, Json(Batch(Event("event-1", "{\"left\":1,\"items\":[1,2],\"right\":2}"))));
        using var resend = await client.PostAsync(Route, Json(Reordered));
        using var conflict = await client.PostAsync(Route, Json(ReorderedArray));

        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        Assert.Empty(await first.Content.ReadAsByteArrayAsync());
        Assert.Equal(1L, await InboxCountAsync(connection));
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        Assert.Empty(await resend.Content.ReadAsByteArrayAsync());
        Assert.Equal(1L, await InboxCountAsync(connection));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("{\"left\": 1, \"items\": [1, 2], \"right\": 2}", await StoredDataAsync(connection, "site-1", "event-1"));
    }

    [Fact]
    public async Task DifferentOccurredAtTextForSameInstantConflictsAndPreservesOriginalValue()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection);
        using var client = factory.CreateClient();
        const string originalTimestamp = "2026-09-24T12:30:00Z";
        const string equivalentTimestamp = "2026-09-24T12:30:00.000Z";

        using var first = await client.PostAsync(Route, Json(Batch(Event("event-1", originalTimestamp, "{\"value\":1}"))));
        using var resend = await client.PostAsync(Route, Json(Batch(Event("event-1", equivalentTimestamp, "{\"value\":1}"))));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, resend.StatusCode);
        Assert.Equal(1L, await InboxCountAsync(connection));
        Assert.Equal(originalTimestamp, await StoredOccurredAtAsync(connection, "site-1", "event-1"));
    }

    [Fact]
    public async Task LostResponseAfterCommitCanBeRetriedWithEmptyAckAndOneStoredRow()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection);
        using var handler = new LoseFirstResponseHandler(factory.Server.CreateHandler());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var payload = Batch(Event("event-1", "{\"value\":1}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync(Route, Json(payload)));
        Assert.Equal(1L, await InboxCountAsync(connection));

        using var retry = await client.PostAsync(Route, Json(payload));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Empty(await retry.Content.ReadAsByteArrayAsync());
        Assert.Equal(1L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task HttpMixedConflictDoesNotPersistNewRowsOrReplaceAcceptedContent()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection);
        using var client = factory.CreateClient();
        using var seed = await client.PostAsync(Route, Json(Batch(Event("event-1", "{\"value\":1}"))));

        using var conflict = await client.PostAsync(Route, Json(Batch(
            Event("event-1", "{\"value\":2}"),
            Event("event-2", "{\"value\":3}"))));

        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1L, await InboxCountAsync(connection));
        Assert.Equal("{\"value\": 1}", await StoredDataAsync(connection, "site-1", "event-1"));
    }

    [Fact]
    public async Task MixedBatchConflictDoesNotPersistNewEvents()
    {
        var connection = await CreateDatabaseAsync();
        await using (var seedDb = CreateContext(connection))
        {
            Assert.Equal(InboxWriteResult.Accepted, await new InboxWriter(seedDb)
                .WriteAsync("site-1", "sensor-1", Parse(Event("event-1", "{\"value\":1}")), default));
        }

        await using var db = CreateContext(connection);
        var writer = new InboxWriter(db);
        var result = await writer.WriteAsync("site-1", "sensor-1",
            Parse(Event("event-1", "{\"value\":2}"), Event("event-2", "{\"value\":3}")), default);

        Assert.Equal(InboxWriteResult.Conflict, result);
        Assert.Equal(1L, await InboxCountAsync(connection));
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-2", "sensor-1", Parse(Event("event-1", "{\"value\":1}"))));
        Assert.Equal(2L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task FailureBeforeCommitNeverAcknowledgesOrPersistsBatch()
    {
        var connection = await CreateDatabaseAsync();
        await using (var db = new NpgsqlConnection(connection))
        {
            await db.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE FUNCTION monitoring.reject_inbox() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.event_id = 'fail' THEN RAISE EXCEPTION 'injected'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_inbox BEFORE INSERT ON monitoring.ingestion_inbox FOR EACH ROW EXECUTE FUNCTION monitoring.reject_inbox();", db);
            await command.ExecuteNonQueryAsync();
        }

        using var factory = CreateHost(connection);
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;
        await Record.ExceptionAsync(async () => response = await client.PostAsync(Route, Json(Batch(Event("event-1", "{}"), Event("fail", "{}")))));
        Assert.False(response?.StatusCode == HttpStatusCode.OK);
        Assert.Equal(0L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsCreateOneAcceptance()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection);
        using var client = factory.CreateClient();
        var payload = Json(Batch(Event("event-1", "{\"value\":1}")));

        var responses = await Task.WhenAll(
            client.PostAsync(Route, payload),
            client.PostAsync(Route, Json(Batch(Event("event-1", "{\"value\":1}")))));
        using var first = responses[0];
        using var second = responses[1];

        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        Assert.True(second.StatusCode == HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        Assert.Equal(1L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task EnforcesFiveHundredNewEventsAndRejectsAnOverLimitBatchAtomically()
    {
        var connection = await CreateDatabaseAsync();
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 499)));

        var overLimit = await WriteAsync(connection, "site-1", "sensor-1", BatchOf(499, 2));
        Assert.Equal(InboxWriteResult.RateLimited, overLimit);
        Assert.Equal(499L, await InboxCountAsync(connection));

        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(499, 1)));
        Assert.Equal(InboxWriteResult.RateLimited, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(500, 1)));
        Assert.Equal(500L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task IdenticalResendsDoNotConsumeQuota()
    {
        var connection = await CreateDatabaseAsync();
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 500)));

        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 1)));
        Assert.Equal(500L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task QuotaIsIsolatedByOriginAndExpiresOutsideTheRollingWindow()
    {
        var connection = await CreateDatabaseAsync();
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 500)));

        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-2", BatchOf(0, 1)));
        await ExpireOriginEventsAsync(connection, "site-1", "sensor-1");
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(500, 1)));
        Assert.Equal(502L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task ConcurrentNewEventsCannotOvershootOriginQuota()
    {
        var connection = await CreateDatabaseAsync();
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 499)));

        var results = await Task.WhenAll(
            WriteAsync(connection, "site-1", "sensor-1", BatchOf(499, 1)),
            WriteAsync(connection, "site-1", "sensor-1", BatchOf(500, 1)));

        Assert.Single(results, result => result == InboxWriteResult.Accepted);
        Assert.Single(results, result => result == InboxWriteResult.RateLimited);
        Assert.Equal(500L, await InboxCountAsync(connection));
    }

    [Fact]
    public async Task HttpOverLimitResponseIs429AndDoesNotPersistNewRows()
    {
        var connection = await CreateDatabaseAsync();
        using var factory = CreateHost(connection, quota: 500);
        using var client = factory.CreateClient();

        using var fullQuota = await client.PostAsync(Route, Json(Batch(QuotaEvents(0, 500))));
        using var overLimit = await client.PostAsync(Route, Json(Batch(QuotaEvents(500, 1))));

        Assert.Equal(HttpStatusCode.OK, fullQuota.StatusCode);
        Assert.Empty(await fullQuota.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, overLimit.StatusCode);
        Assert.Equal(500L, await InboxCountAsync(connection));
    }

    private async Task<string> CreateDatabaseAsync()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var migration = await MigrationProcess.RunAsync(connection);
        Assert.True(migration.ExitCode == 0, migration.StandardError.Replace(connection, "<redacted>"));
        return connection;
    }

    private static WebApplicationFactory<Program> CreateHost(string connection, int? quota = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Monitoring", connection);
            if (quota is { } limit) builder.UseSetting("Ingestion:MaxNewEventsPerMinute", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSensorIdentityProvider>();
                services.AddSingleton<ITrustedSensorIdentityProvider>(new FixedIdentityProvider());
            });
        });

    private static MonitoringDbContext CreateContext(string connection) =>
        new(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connection).Options);

    // The historical quota tests pin the limit they were written for; the product default is 6000 (see the defaults test).
    private static readonly IngestionOptions FiveHundred = new() { MaxNewEventsPerMinute = 500 };

    private static async Task<InboxWriteResult> WriteAsync(string connection, string site, string sensor, IngestionBatch batch, IngestionOptions? options = null)
    {
        await using var db = CreateContext(connection);
        return await new InboxWriter(db, options ?? FiveHundred).WriteAsync(site, sensor, batch, default);
    }

    [Fact]
    public void TheDefaultQuotaIsSixThousandNewEventsPerMinuteAndMustBePositive()
    {
        Assert.Equal(6000, new IngestionOptions().MaxNewEventsPerMinute);
        foreach (var invalid in new[] { 0, -1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new InboxWriter(CreateContext("Host=127.0.0.1"), new IngestionOptions { MaxNewEventsPerMinute = invalid }));
    }

    [Fact]
    public async Task AConfiguredQuotaIsAppliedPerOriginAndAnOverLimitBatchLeavesNothingBehind()
    {
        var connection = await CreateDatabaseAsync();
        var five = new IngestionOptions { MaxNewEventsPerMinute = 5 };
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(0, 4), five));
        Assert.Equal(InboxWriteResult.RateLimited, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(4, 2), five));
        Assert.Equal(4L, await InboxCountAsync(connection));
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-2", BatchOf(0, 5), five));
        // A larger quota admits what a smaller one rejected, without any other change.
        Assert.Equal(InboxWriteResult.Accepted, await WriteAsync(connection, "site-1", "sensor-1", BatchOf(4, 2), new IngestionOptions { MaxNewEventsPerMinute = 6 }));
    }

    private static IngestionBatch BatchOf(int firstEvent, int count) =>
        Parse(QuotaEvents(firstEvent, count));

    private static string[] QuotaEvents(int firstEvent, int count) =>
        Enumerable.Range(firstEvent, count)
            .Select(index => Event($"quota-{index}", "{}"))
            .ToArray();

    private static async Task ExpireOriginEventsAsync(string connection, string site, string sensor)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE monitoring.ingestion_inbox SET accepted_at = clock_timestamp() - interval '61 seconds' WHERE site_id = @site AND sensor_id = @sensor",
            db);
        command.Parameters.AddWithValue("site", site);
        command.Parameters.AddWithValue("sensor", sensor);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> InboxCountAsync(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM monitoring.ingestion_inbox", db);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> StoredDataAsync(string connection, string site, string eventId)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT data::text FROM monitoring.ingestion_inbox WHERE site_id = @site AND event_id = @event", db);
        command.Parameters.AddWithValue("site", site);
        command.Parameters.AddWithValue("event", eventId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> StoredOccurredAtAsync(string connection, string site, string eventId)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT occurred_at_text FROM monitoring.ingestion_inbox WHERE site_id = @site AND event_id = @event", db);
        command.Parameters.AddWithValue("site", site);
        command.Parameters.AddWithValue("event", eventId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static IngestionBatch Parse(params string[] events)
    {
        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(Batch(events)), out var batch));
        return batch!;
    }

    private static string Batch(params string[] events) =>
        "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[" + string.Join(",", events) + "]}";

    private static string Event(string id, string data) =>
        "{\"eventId\":\"" + id + "\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":" + data + "}";

    private static string Event(string id, string occurredAt, string data) =>
        "{\"eventId\":\"" + id + "\",\"occurredAt\":\"" + occurredAt + "\",\"data\":" + data + "}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed class FixedIdentityProvider : ITrustedSensorIdentityProvider
    {
        public TrustedSensorIdentity? Resolve(HttpContext context) => new("site-1", "sensor-1");
    }

    private sealed class LoseFirstResponseHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private int requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                response.Dispose();
                throw new HttpRequestException("Simulated response loss after the server completed the request.");
            }

            return response;
        }
    }
}

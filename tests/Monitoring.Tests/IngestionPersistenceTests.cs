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

    private async Task<string> CreateDatabaseAsync()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var migration = await MigrationProcess.RunAsync(connection);
        Assert.True(migration.ExitCode == 0, migration.StandardError.Replace(connection, "<redacted>"));
        return connection;
    }

    private static WebApplicationFactory<Program> CreateHost(string connection) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Monitoring", connection);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSensorIdentityProvider>();
                services.AddSingleton<ITrustedSensorIdentityProvider>(new FixedIdentityProvider());
            });
        });

    private static MonitoringDbContext CreateContext(string connection) =>
        new(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connection).Options);

    private static async Task<InboxWriteResult> WriteAsync(string connection, string site, string sensor, IngestionBatch batch)
    {
        await using var db = CreateContext(connection);
        return await new InboxWriter(db).WriteAsync(site, sensor, batch, default);
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

    private static IngestionBatch Parse(params string[] events)
    {
        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(Batch(events)), out var batch));
        return batch!;
    }

    private static string Batch(params string[] events) =>
        "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[" + string.Join(",", events) + "]}";

    private static string Event(string id, string data) =>
        "{\"eventId\":\"" + id + "\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":" + data + "}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed class FixedIdentityProvider : ITrustedSensorIdentityProvider
    {
        public TrustedSensorIdentity? Resolve(HttpContext context) => new("site-1", "sensor-1");
    }
}

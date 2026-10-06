using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Domain.Sessions;
using Monitoring.Host.Sessions;
using Monitoring.Persistence.Search;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

// HTTP endpoint + sealed cursor + search adapter + real PostgreSQL authority, with the in-process Elasticsearch stand-in.
public sealed class SessionSearchVerticalTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task PagesThroughTheCursorHideSuppressedIdentitiesAndReleaseTheSnapshotAtTheEnd()
    {
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var n in Enumerable.Range(1, 4)) await SessionTestDatabase.AcceptAsync(connection, $"event-{n}", "site-a", "sensor-a");
        await SessionTestDatabase.ProjectAsync(connection);

        var elastic = new FakeElasticsearch();
        await using (var npgsql = new NpgsqlConnection(connection))
        {
            await npgsql.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT event_id,search_document_id FROM monitoring.session_identity ORDER BY event_id", npgsql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var eventId = reader.GetString(0);
                var n = int.Parse(eventId[^1..]);
                elastic.Add(FakeElasticsearch.MakeDoc(reader.GetGuid(1), "site-a", "sensor-a", eventId, now.AddHours(-1).AddSeconds(-n)));
            }
        }
        // The index still holds event-2 although the authority already suppressed it: the lagging-index case.
        await using (var db = SessionTestDatabase.Context(connection))
            Assert.Equal(SuppressionResult.Suppressed, await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site-a", "sensor-a", "event-2"), CancellationToken.None));

        using var factory = SessionHostTests.CreateHost(connection, new TrustedSessionReadContext("site-a", "sensor-a"), suppressWorker: true)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Search:Elasticsearch:Url", "http://elasticsearch.test:9200");
                builder.ConfigureTestServices(services =>
                {
                    // The periodic reconciliation would race the freshness assertions; it is exercised by its own tests.
                    foreach (var descriptor in services.Where(service => service.ImplementationType == typeof(ProjectionReconciliationWorker)).ToArray())
                        services.Remove(descriptor);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTime(now));
                    services.AddHttpClient<ElasticsearchSessionSearch>().ConfigurePrimaryHttpMessageHandler(() => elastic);
                });
            });
        using var client = factory.CreateClient();
        var window = $"from={Uri.EscapeDataString(now.AddHours(-12).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))}&to={Uri.EscapeDataString(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))}&pageSize=2";

        async Task<JsonElement> Get(string query)
        {
            using var response = await client.GetAsync("/api/v1/sessions?" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var first = await Get(window);
        Assert.Equal(["event-1", "event-3"], first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("eventId").GetString()));
        Assert.Equal("recovering", first.GetProperty("freshness").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("freshness").GetProperty("lagSeconds").ValueKind);
        var cursor = first.GetProperty("nextCursor").GetString()!;
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.search_snapshot_lease"));
        Assert.Equal(1, elastic.OpenPits);

        var second = await Get(window + "&cursor=" + Uri.EscapeDataString(cursor));
        Assert.Equal(["event-4"], second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("eventId").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.search_snapshot_lease"));
        Assert.Equal(0, elastic.OpenPits);

        // The sealed cursor is single-purpose: reusing it with other filters is rejected before any engine call.
        var calls = elastic.Calls.Count;
        using var reused = await client.GetAsync("/api/v1/sessions?" + window + "&protocol=UDP&cursor=" + Uri.EscapeDataString(cursor));
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(calls, elastic.Calls.Count);
    }
}

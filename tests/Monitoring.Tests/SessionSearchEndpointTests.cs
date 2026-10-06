using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Sessions;

namespace Monitoring.Tests;

public sealed class SessionSearchEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const string Day = "from=2026-10-05T12:00:00.000Z&to=2026-10-06T11:00:00Z";
    private const string Wide = "from=2026-09-20T00:00:00Z&to=2026-10-06T00:00:00Z";

    private sealed class FakeSearch(Func<SessionSearchRequest, CancellationToken, Task<SessionSearchPage>>? handler = null) : ISessionSearch
    {
        public List<SessionSearchRequest> Requests { get; } = [];
        public Task<SessionSearchPage> SearchAsync(SessionSearchRequest request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return (handler ?? ((_, _) => Task.FromResult(Page())))(request, cancellationToken);
        }
    }

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private static SessionSearchPage Page(string? next = null, SearchFreshness? freshness = null, params SessionSearchItem[] items) =>
        new(items, next, freshness ?? new SearchFreshness(FreshnessState.Current, Now, 2));

    private static WebApplicationFactory<Program> Host(FakeSearch search, TrustedSessionReadContext? context = null, string environment = "Testing",
        Action<SessionSearchOptions>? options = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSessionReadContextProvider>();
                services.AddSingleton<ITrustedSessionReadContextProvider>(new SessionHostTests.ReadProvider(context ?? new("site-a", "sensor-a")));
                services.RemoveAll<ISessionSearch>();
                services.AddSingleton<ISessionSearch>(search);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTime());
                var configured = new SessionSearchOptions();
                options?.Invoke(configured);
                services.RemoveAll<SessionSearchOptions>();
                services.AddSingleton(configured);
            });
        });

    private static async Task<HttpStatusCode> StatusAsync(FakeSearch search, string query, TrustedSessionReadContext? context = null)
    {
        using var factory = Host(search, context);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/sessions?" + query);
        return response.StatusCode;
    }

    [Theory]
    [InlineData("")]
    [InlineData("from=2026-10-05T12:00:00Z")]
    [InlineData("to=2026-10-06T11:00:00Z")]
    [InlineData("from=2026-10-05T12:00:00%2B00:00&to=2026-10-06T11:00:00Z")]
    [InlineData("from=2026-10-05T12:00:00.1234Z&to=2026-10-06T11:00:00Z")]
    [InlineData("from=2026-10-05&to=2026-10-06T11:00:00Z")]
    [InlineData("from=2026-10-06T11:00:00Z&to=2026-10-05T12:00:00Z")]
    [InlineData("from=2026-10-06T11:00:00Z&to=2026-10-06T11:00:00Z")]
    [InlineData("from=2026-10-06T11:00:00Z&to=2026-10-06T12:00:00.001Z")]
    [InlineData("from=2026-10-05T12:00:00Z&to=2026-10-07T12:00:00Z")]
    [InlineData("from=2026-09-01T00:00:00Z&to=2026-09-30T00:00:00Z")]
    [InlineData("from=2026-09-05T11:00:00Z&to=2026-10-06T11:00:01Z")]
    [InlineData(Day + "&protocol=ICMP")]
    [InlineData(Day + "&protocol=tcp")]
    [InlineData(Day + "&sourcePort=65536")]
    [InlineData(Day + "&destinationPort=-1")]
    [InlineData(Day + "&sourcePort=80.5")]
    [InlineData(Day + "&sourcePort=abc")]
    [InlineData(Day + "&sourceIp=999.1.1.1")]
    [InlineData(Day + "&sourceIp=1")]
    [InlineData(Day + "&destinationIp=fe80::1%25eth0")]
    [InlineData(Day + "&sourceIp=")]
    [InlineData(Day + "&pageSize=0")]
    [InlineData(Day + "&pageSize=101")]
    [InlineData(Day + "&pageSize=ten")]
    [InlineData(Day + "&q=free+text")]
    [InlineData(Day + "&SITEID=site-a")]
    [InlineData(Day + "&protocol=TCP&protocol=UDP")]
    [InlineData(Day + "&siteId=" + "x01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789")]
    [InlineData(Wide)]
    [InlineData(Wide + "&siteId=site-a&sensorId=sensor-a")]
    [InlineData(Wide + "&siteId=site-a&sensorId=sensor-a&protocol=TCP")]
    [InlineData(Wide + "&sensorId=sensor-a&sourceIp=192.0.2.1")]
    public async Task InvalidRequestsAreRejectedWith400BeforeAnySearch(string query)
    {
        var search = new FakeSearch();
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(search, query));
        Assert.Empty(search.Requests);
    }

    [Fact]
    public async Task ASelectiveThirtyDayWindowAndEveryFilterReachTheSearchNormalizedWithPaging()
    {
        var search = new FakeSearch();
        var query = "from=2026-09-07T12:00:00Z&to=2026-10-06T12:00:00Z&siteId=site-a&sensorId=sensor-a&sourceIp=2001:0DB8:0:0:0:0:0:1" +
            "&destinationIp=192.0.2.2&protocol=UDP&sourcePort=0&destinationPort=65535&pageSize=100&cursor=opaque";
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(search, query));
        var request = Assert.Single(search.Requests);
        Assert.Equal(DateTimeOffset.Parse("2026-09-07T12:00:00Z"), request.From);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T12:00:00Z"), request.To);
        Assert.Equal([new AuthorizedPair("site-a", "sensor-a")], request.Scope);
        Assert.Equal("2001:db8::1", request.SourceIp);
        Assert.Equal("192.0.2.2", request.DestinationIp);
        Assert.Equal("UDP", request.Protocol);
        Assert.Equal(0, request.SourcePort);
        Assert.Equal(65535, request.DestinationPort);
        Assert.Equal(100, request.PageSize);
        Assert.Equal("opaque", request.Cursor);
    }

    [Fact]
    public async Task ADefaultShortWindowUsesTheAuthorizedScopeAndPageSizeFifty()
    {
        var search = new FakeSearch();
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(search, Day));
        var request = Assert.Single(search.Requests);
        Assert.Equal([new AuthorizedPair("site-a", "sensor-a")], request.Scope);
        Assert.Equal(50, request.PageSize);
        Assert.Null(request.SourceIp);
        Assert.Null(request.Cursor);
    }

    [Theory]
    [InlineData("&siteId=site-b")]
    [InlineData("&sensorId=sensor-b")]
    [InlineData("&siteId=site-a&sensorId=sensor-b")]
    [InlineData("&siteId=site-b&sensorId=sensor-a")]
    public async Task ASelectorOutsideTheAuthorizedScopeIsForbiddenWithoutSearching(string selector)
    {
        var search = new FakeSearch();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(search, Day + selector));
        Assert.Empty(search.Requests);
    }

    [Fact]
    public async Task SelectorsOnlyNarrowTheAuthorizedSet()
    {
        var search = new FakeSearch();
        using var factory = Host(search);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/sessions?" + Day + "&siteId=site-a");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([new AuthorizedPair("site-a", "sensor-a")], Assert.Single(search.Requests).Scope);
    }

    [Fact]
    public async Task MissingIdentityIs401AndProductionEnvironmentsNeverServeTheDevelopmentContext()
    {
        var search = new FakeSearch();
        using (var anonymous = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSessionReadContextProvider>();
                services.AddSingleton<ITrustedSessionReadContextProvider>(new SessionHostTests.ReadProvider(null));
                services.RemoveAll<ISessionSearch>();
                services.AddSingleton<ISessionSearch>(search);
            });
        }))
        {
            using var response = await anonymous.CreateClient().GetAsync("/api/v1/sessions?" + Day);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var production = Host(search, environment: "Production");
        using var denied = await production.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Empty(search.Requests);
    }

    [Fact]
    public async Task TheResponseCarriesFlatItemsCursorAndFreshnessWithoutExtraFields()
    {
        var item = new SessionSearchItem("event-1", "site-a", "sensor-a", "192.0.2.1", "2001:db8::2", 1234, 443, "TCP",
            "2026-10-06T10:00:00.100Z", "2026-10-06T10:00:01.000Z", "capture", true, false);
        var synthetic = item with { EventId = "event-2", Provenance = "synthetic", Inferred = null, Partial = null };
        var search = new FakeSearch((_, _) => Task.FromResult(new SessionSearchPage([item, synthetic], "next",
            new SearchFreshness(FreshnessState.Lagging, Now, 125))));
        using var factory = Host(search);
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "freshness", "items", "nextCursor" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("next", root.GetProperty("nextCursor").GetString());
        var freshness = root.GetProperty("freshness");
        Assert.Equal("lagging", freshness.GetProperty("state").GetString());
        Assert.Equal("2026-10-06T12:00:00.000Z", freshness.GetProperty("measuredAt").GetString());
        Assert.Equal(125, freshness.GetProperty("lagSeconds").GetInt64());
        var first = root.GetProperty("items")[0];
        Assert.Equal(new[] { "destinationIp", "destinationPort", "endedAt", "eventId", "inferred", "partial", "protocol", "provenance",
            "sensorId", "siteId", "sourceIp", "sourcePort", "startedAt" }, first.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.True(first.GetProperty("inferred").GetBoolean());
        Assert.False(first.GetProperty("partial").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("items")[1].GetProperty("inferred").ValueKind);
    }

    [Fact]
    public async Task AnEmptyResultIs200WithUnknownLagNeverZero()
    {
        var search = new FakeSearch((_, _) => Task.FromResult(Page(null, new SearchFreshness(FreshnessState.Recovering, Now, null))));
        using var factory = Host(search);
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("nextCursor").ValueKind is JsonValueKind.Null);
        var freshness = document.RootElement.GetProperty("freshness");
        Assert.Equal("recovering", freshness.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, freshness.GetProperty("lagSeconds").ValueKind);
    }

    [Theory]
    [InlineData(SessionSearchFailure.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(SessionSearchFailure.Saturated, HttpStatusCode.TooManyRequests)]
    [InlineData(SessionSearchFailure.CursorExpired, HttpStatusCode.Gone)]
    [InlineData(SessionSearchFailure.CursorInvalid, HttpStatusCode.BadRequest)]
    [InlineData(SessionSearchFailure.ScopeChanged, HttpStatusCode.Forbidden)]
    public async Task SearchFailuresMapToDistinctStatusesWithoutLeakingDetails(SessionSearchFailure failure, HttpStatusCode expected)
    {
        var search = new FakeSearch((_, _) => throw new SessionSearchException(failure, "secret-index-detail"));
        using var factory = Host(search);
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnexpectedBackendErrorIs503NotAPartialPage()
    {
        var search = new FakeSearch((_, _) => throw new HttpRequestException("secret-host:9200"));
        using var factory = Host(search);
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASlowSearchIsCancelledAndAnswers504()
    {
        var cancelled = new TaskCompletionSource();
        var search = new FakeSearch(async (_, token) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            return Page();
        });
        using var factory = Host(search, options: options => options.Timeout = TimeSpan.FromMilliseconds(150));
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentSearchesAreBoundedAndTheExcessIs429()
    {
        var release = new TaskCompletionSource();
        var entered = new SemaphoreSlim(0);
        var search = new FakeSearch(async (_, _) => { entered.Release(); await release.Task; return Page(); });
        using var factory = Host(search, options: options => options.MaxConcurrentSearches = 2);
        using var client = factory.CreateClient();
        var first = client.GetAsync("/api/v1/sessions?" + Day);
        var second = client.GetAsync("/api/v1/sessions?" + Day);
        await entered.WaitAsync(TimeSpan.FromSeconds(5));
        await entered.WaitAsync(TimeSpan.FromSeconds(5));
        using (var excess = await client.GetAsync("/api/v1/sessions?" + Day))
            Assert.Equal(HttpStatusCode.TooManyRequests, excess.StatusCode);
        release.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second).StatusCode);
        // The slot is released after completion.
        using var after = await client.GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task WithoutASearchBackendTheEndpointAnswers503()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITrustedSessionReadContextProvider>();
                services.AddSingleton<ITrustedSessionReadContextProvider>(new SessionHostTests.ReadProvider(new("site-a", "sensor-a")));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTime());
            });
        });
        using var response = await factory.CreateClient().GetAsync("/api/v1/sessions?" + Day);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

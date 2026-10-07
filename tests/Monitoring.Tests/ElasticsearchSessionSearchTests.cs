using System.Net;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence.Search;

namespace Monitoring.Tests;

public sealed class ElasticsearchSessionSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = Now.AddHours(-12);
    private static readonly AuthorizedPair[] ScopeA = [new("site-a", "sensor-a")];

    private sealed class Visibility : ISessionVisibility
    {
        public HashSet<Guid> Hidden { get; } = [];
        public List<int> BlockSizes { get; } = [];
        public Task<IReadOnlySet<Guid>> VisibleAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            BlockSizes.Add(ids.Count);
            return Task.FromResult<IReadOnlySet<Guid>>(ids.Where(id => !Hidden.Contains(id)).ToHashSet());
        }
    }

    private sealed class Leases : ISnapshotLeases
    {
        public List<string> Log { get; } = [];
        public bool Saturated { get; set; }
        public bool Lost { get; set; }
        public Task<Guid> AcquireAsync(string subject, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (Saturated) throw new SessionSearchException(SessionSearchFailure.Saturated);
            Log.Add($"acquire {subject} {expiresAt:O}");
            return Task.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        }
        public Task<bool> RecordPitAsync(Guid leaseId, string pitId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Log.Add($"pit {pitId}");
            return Task.FromResult(!Lost);
        }
        public Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken) { Log.Add("release"); return Task.CompletedTask; }
    }

    private sealed class Status : IProjectionStatus
    {
        public Task<SearchFreshness> CurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SearchFreshness(FreshnessState.Lagging, Now, 42));
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed record Rig(ElasticsearchSessionSearch Search, FakeElasticsearch Elastic, Visibility Visibility, Leases Leases);

    private static Rig CreateRig(int blockSize = 100)
    {
        var elastic = new FakeElasticsearch();
        var visibility = new Visibility();
        var leases = new Leases();
        var http = new HttpClient(elastic) { BaseAddress = new Uri("http://elasticsearch.test:9200") };
        return new Rig(new ElasticsearchSessionSearch(http, visibility, leases, new Status(),
            new ElasticsearchOptions { BlockSize = blockSize }, new Clock()), elastic, visibility, leases);
    }

    private static FakeElasticsearch.Doc Doc(int n, string site = "site-a", string sensor = "sensor-a", int secondsAgo = 0, string protocol = "TCP",
        string sourceIp = "192.0.2.1", string destinationIp = "192.0.2.2", int sourcePort = 1234, int destinationPort = 443, string operation = "upsert") =>
        FakeElasticsearch.MakeDoc(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), site, sensor, $"event-{n:D3}", Now.AddHours(-1).AddSeconds(-secondsAgo),
            protocol, sourceIp, destinationIp, sourcePort, destinationPort, operation);

    private static SessionSearchRequest Request(int pageSize = 50, SessionSearchPosition? after = null, IReadOnlyList<AuthorizedPair>? scope = null,
        string? protocol = null, string? sourceIp = null, string? destinationIp = null, int? sourcePort = null, int? destinationPort = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(from ?? From, to ?? Now, scope ?? ScopeA, sourceIp, destinationIp, protocol, sourcePort, destinationPort, pageSize, after,
            "alice", Now.AddMinutes(10));

    [Fact]
    public async Task TheFirstPageOpensASnapshotUnderALeaseAndReturnsOrderedItemsWithAContinuation()
    {
        var rig = CreateRig();
        rig.Elastic.Add(Enumerable.Range(1, 5).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var page = await rig.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        Assert.Equal(["event-001", "event-002"], page.Items.Select(item => item.EventId));
        Assert.NotNull(page.Next);
        Assert.Equal(FreshnessState.Lagging, page.Freshness.State);
        Assert.StartsWith("acquire alice", rig.Leases.Log[0]);
        Assert.Contains("pit pit-1", rig.Leases.Log);
        Assert.Equal(1, rig.Elastic.OpenPits);
        Assert.DoesNotContain("DELETE /_pit", rig.Elastic.Calls);
        var item = page.Items[0];
        Assert.Equal(("site-a", "sensor-a", "192.0.2.1", "192.0.2.2", 1234, 443, "TCP", "synthetic"),
            (item.SiteId, item.SensorId, item.SourceIp, item.DestinationIp, item.SourcePort, item.DestinationPort, item.Protocol, item.Provenance));
        Assert.Equal(("2026-10-06T10:59:59.000Z", "2026-10-06T11:00:00.000Z"), (item.StartedAt, item.EndedAt));
    }

    [Fact]
    public async Task EveryPageOfOneSnapshotAppearsOnceWithTiesAndWithoutLaterInsertions()
    {
        var rig = CreateRig();
        // Ties: same instant, ordered by the complete document key.
        rig.Elastic.Add(Enumerable.Range(1, 7).Select(n => Doc(n, secondsAgo: n <= 4 ? 10 : n)).ToArray());
        var seen = new List<string>();
        var page = await rig.Search.SearchAsync(Request(pageSize: 3), CancellationToken.None);
        seen.AddRange(page.Items.Select(item => item.EventId));
        rig.Elastic.Add(Doc(99, secondsAgo: 0), Doc(98, secondsAgo: 11)); // arrives after the snapshot was taken
        while (page.Next is not null)
        {
            page = await rig.Search.SearchAsync(Request(pageSize: 3, after: page.Next), CancellationToken.None);
            seen.AddRange(page.Items.Select(item => item.EventId));
        }
        Assert.Equal(new[] { "event-005", "event-006", "event-007", "event-001", "event-002", "event-003", "event-004" }.Order(), seen.Order());
        Assert.Equal(7, seen.Distinct().Count());
        Assert.DoesNotContain("event-099", seen);
        Assert.DoesNotContain("event-098", seen);
        // The last page closes the snapshot and releases the lease.
        Assert.Contains("DELETE /_pit", rig.Elastic.Calls);
        Assert.Equal(0, rig.Elastic.OpenPits);
        Assert.Equal("release", rig.Leases.Log[^1]);
        Assert.Single(rig.Leases.Log, entry => entry.StartsWith("acquire", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APageThatExactlyExhaustsTheResultsHasNoContinuation()
    {
        var rig = CreateRig();
        rig.Elastic.Add(Enumerable.Range(1, 4).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var page = await rig.Search.SearchAsync(Request(pageSize: 4), CancellationToken.None);
        Assert.Equal(4, page.Items.Count);
        Assert.Null(page.Next);
        Assert.Equal(0, rig.Elastic.OpenPits);
    }

    [Fact]
    public async Task ScopeFiltersAndTheHalfOpenRangeAreAppliedInsideTheEngineQuery()
    {
        var rig = CreateRig();
        rig.Elastic.Add(
            Doc(1), Doc(2, site: "site-b"), Doc(3, sensor: "sensor-b"), Doc(4, protocol: "UDP"), Doc(5, sourceIp: "192.0.2.9"),
            Doc(6, destinationIp: "192.0.2.3"), Doc(7, sourcePort: 80), Doc(8, destinationPort: 8443), Doc(9, operation: "delete"),
            FakeElasticsearch.MakeDoc(new Guid(10, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "site-a", "sensor-a", "at-from", From),
            FakeElasticsearch.MakeDoc(new Guid(11, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "site-a", "sensor-a", "at-to", Now),
            FakeElasticsearch.MakeDoc(new Guid(12, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "site-a", "sensor-a", "before-from", From.AddMilliseconds(-1)));
        var scoped = await rig.Search.SearchAsync(Request(), CancellationToken.None);
        Assert.Equal(["at-from", "event-001", "event-004", "event-005", "event-006", "event-007", "event-008"], scoped.Items.Select(item => item.EventId).Order());
        var filtered = await rig.Search.SearchAsync(Request(protocol: "TCP", sourceIp: "192.0.2.1", destinationIp: "192.0.2.2", sourcePort: 1234, destinationPort: 443),
            CancellationToken.None);
        Assert.Equal(["at-from", "event-001"], filtered.Items.Select(item => item.EventId).Order());
        // Two authorized pairs: the union, never more.
        var both = await rig.Search.SearchAsync(Request(scope: [new("site-a", "sensor-a"), new("site-b", "sensor-a")], protocol: "TCP",
            sourceIp: "192.0.2.1", destinationIp: "192.0.2.2", sourcePort: 1234, destinationPort: 443), CancellationToken.None);
        Assert.Equal(["at-from", "event-001", "event-002"], both.Items.Select(item => item.EventId).Order());
    }

    [Fact]
    public async Task SuppressedIdentitiesAreSkippedAcrossBlocksWithoutShorteningThePage()
    {
        var rig = CreateRig(blockSize: 4);
        rig.Elastic.Add(Enumerable.Range(1, 12).Select(n => Doc(n, secondsAgo: n)).ToArray());
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) rig.Visibility.Hidden.Add(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));
        var page = await rig.Search.SearchAsync(Request(pageSize: 3), CancellationToken.None);
        Assert.Equal(["event-008", "event-009", "event-010"], page.Items.Select(item => item.EventId));
        Assert.NotNull(page.Next); // event-011 is still visible
        Assert.All(rig.Visibility.BlockSizes, size => Assert.True(size <= 4));
        var last = await rig.Search.SearchAsync(Request(pageSize: 3, after: page.Next), CancellationToken.None);
        Assert.Equal(["event-011", "event-012"], last.Items.Select(item => item.EventId));
        Assert.Null(last.Next);
    }

    [Fact]
    public async Task WhenEveryRemainingHitIsSuppressedThePageEndsWithoutAContinuation()
    {
        var rig = CreateRig(blockSize: 2);
        rig.Elastic.Add(Enumerable.Range(1, 6).Select(n => Doc(n, secondsAgo: n)).ToArray());
        foreach (var n in new[] { 3, 4, 5, 6 }) rig.Visibility.Hidden.Add(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));
        var page = await rig.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        Assert.Equal(["event-001", "event-002"], page.Items.Select(item => item.EventId));
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task ARotatedPitIdIsCarriedInTheContinuationAndRecordedOnTheLease()
    {
        var rig = CreateRig();
        rig.Elastic.RotatePitIdOnSearch = true;
        rig.Elastic.Add(Enumerable.Range(1, 4).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var first = await rig.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        Assert.EndsWith("pit-1'", first.Next!.SnapshotId);
        Assert.Contains("pit pit-1'", rig.Leases.Log);
        var second = await rig.Search.SearchAsync(Request(pageSize: 2, after: first.Next), CancellationToken.None);
        Assert.Equal(["event-003", "event-004"], second.Items.Select(item => item.EventId));
    }

    [Fact]
    public async Task ALostSnapshotIsGoneAndAnExpiredLeaseIsRejectedWithoutServingData()
    {
        var rig = CreateRig();
        rig.Elastic.Add(Enumerable.Range(1, 4).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var first = await rig.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        var forgotten = first.Next! with { SnapshotId = first.Next.SnapshotId[..37] + "pit-gone" };
        var gone = await Assert.ThrowsAsync<SessionSearchException>(() => rig.Search.SearchAsync(Request(pageSize: 2, after: forgotten), CancellationToken.None));
        Assert.Equal(SessionSearchFailure.CursorExpired, gone.Failure);
        Assert.Equal("release", rig.Leases.Log[^1]);
        var other = CreateRig();
        other.Elastic.Add(Enumerable.Range(1, 4).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var begun = await other.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        other.Leases.Lost = true;
        var expired = await Assert.ThrowsAsync<SessionSearchException>(() => other.Search.SearchAsync(Request(pageSize: 2, after: begun.Next), CancellationToken.None));
        Assert.Equal(SessionSearchFailure.CursorExpired, expired.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-lease.pit")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData(".pit-1")]
    public async Task AMalformedSnapshotIdentifierIsAnInvalidCursor(string snapshot)
    {
        var rig = CreateRig();
        var exception = await Assert.ThrowsAsync<SessionSearchException>(() => rig.Search.SearchAsync(
            Request(after: new SessionSearchPosition(snapshot, "2026-10-06T10:00:00.000Z", "key")), CancellationToken.None));
        Assert.Equal(SessionSearchFailure.CursorInvalid, exception.Failure);
        Assert.Empty(rig.Elastic.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, SessionSearchFailure.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, SessionSearchFailure.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, SessionSearchFailure.Saturated)]
    public async Task EngineFailuresMapToDistinctFailuresAndAFailedFirstPageReleasesItsLease(HttpStatusCode status, SessionSearchFailure expected)
    {
        var rig = CreateRig();
        rig.Elastic.FailWith = status;
        var exception = await Assert.ThrowsAsync<SessionSearchException>(() => rig.Search.SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(expected, exception.Failure);
        Assert.DoesNotContain("secret", exception.Message ?? "");
        Assert.Equal("release", rig.Leases.Log[^1]);
    }

    [Fact]
    public async Task AnEngineOutageOnALaterPageKeepsTheSnapshotSoTheClientCanRetry()
    {
        var rig = CreateRig();
        rig.Elastic.Add(Enumerable.Range(1, 4).Select(n => Doc(n, secondsAgo: n)).ToArray());
        var first = await rig.Search.SearchAsync(Request(pageSize: 2), CancellationToken.None);
        rig.Elastic.FailWith = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<SessionSearchException>(() => rig.Search.SearchAsync(Request(pageSize: 2, after: first.Next), CancellationToken.None));
        Assert.DoesNotContain("release", rig.Leases.Log);
        rig.Elastic.FailWith = null;
        var retried = await rig.Search.SearchAsync(Request(pageSize: 2, after: first.Next), CancellationToken.None);
        Assert.Equal(["event-003", "event-004"], retried.Items.Select(item => item.EventId));
    }

    [Fact]
    public async Task SaturatedLeasesRejectTheFirstPageBeforeTouchingTheEngine()
    {
        var rig = CreateRig();
        rig.Leases.Saturated = true;
        var exception = await Assert.ThrowsAsync<SessionSearchException>(() => rig.Search.SearchAsync(Request(), CancellationToken.None));
        Assert.Equal(SessionSearchFailure.Saturated, exception.Failure);
        Assert.Empty(rig.Elastic.Calls);
    }

    [Fact]
    public async Task CancellationPropagatesToTheEngineCall()
    {
        var rig = CreateRig();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Search.SearchAsync(Request(), cancelled.Token));
        Assert.Equal("release", rig.Leases.Log[^1]);
    }

    [Fact]
    public async Task TheQueryNeverRequestsTotalsFreeTextOrMoreThanOneBlock()
    {
        var rig = CreateRig(blockSize: 25);
        rig.Elastic.Add(Doc(1));
        await rig.Search.SearchAsync(Request(pageSize: 100), CancellationToken.None);
        var body = rig.Elastic.SearchBodies.Single();
        Assert.False(body["track_total_hits"]!.GetValue<bool>());
        Assert.Equal(25, body["size"]!.GetValue<int>());
        Assert.DoesNotContain("query_string", body.ToJsonString());
        Assert.DoesNotContain("\"match\"", body.ToJsonString());
    }
}

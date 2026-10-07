using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Persistence.Search;

public sealed class ElasticsearchOptions
{
    // Read alias, never a physical index: a rebuild switches the alias atomically.
    public string Index { get; set; } = "sessions-read";
    // The cursor deadline (10 minutes, absolute) is enforced by the host; the PIT only has to outlive one page request.
    public string KeepAlive { get; set; } = "2m";
    public int BlockSize { get; set; } = 100;
}

// Lists sessions from the search projection inside a point-in-time snapshot. The authorized scope and every filter are part
// of the engine query; each block of hits is then checked against the authority so suppressed identities are never shown.
public sealed class ElasticsearchSessionSearch(HttpClient http, ISessionVisibility visibility, ISnapshotLeases leases,
    IProjectionStatus status, ElasticsearchOptions options, TimeProvider time) : ISessionSearch
{
    private static readonly string[] SourceFields =
    [
        "eventId", "siteId", "sensorId", "sourceIp", "destinationIp", "sourcePort", "destinationPort", "protocol",
        "startedAt", "endedAt", "provenance", "inferred", "partial", "documentKey"
    ];

    private sealed record Hit(Guid Id, long StartedAtMillis, string DocumentKey, SessionSearchItem Item);

    public async Task<SessionSearchPage> SearchAsync(SessionSearchRequest request, CancellationToken cancellationToken)
    {
        Guid lease;
        string pit;
        var firstPage = request.After is null;
        if (firstPage)
        {
            lease = await leases.AcquireAsync(request.Subject, request.SnapshotExpiresAt, time.GetUtcNow(), cancellationToken);
            pit = string.Empty;
        }
        else if (!TryParseSnapshot(request.After!.SnapshotId, out lease, out pit))
        {
            throw new SessionSearchException(SessionSearchFailure.CursorInvalid);
        }

        var keepSnapshot = !firstPage;
        try
        {
            if (firstPage) pit = await OpenPitAsync(cancellationToken);
            // Every page re-checks the lease, so a released or expired snapshot is never served; later pages also audit PIT changes.
            if (!await leases.RecordPitAsync(lease, pit, time.GetUtcNow(), cancellationToken))
                throw new SessionSearchException(SessionSearchFailure.CursorExpired);
            var (items, next, latestPit) = await ReadPageAsync(request, lease, pit, cancellationToken);
            keepSnapshot = next is not null; // exhausted: nothing can resume this snapshot
            var freshness = await status.CurrentAsync(cancellationToken);
            return new SessionSearchPage(items, next is null ? null : next with { SnapshotId = SnapshotId(lease, latestPit) }, freshness);
        }
        catch (SessionSearchException exception) when (exception.Failure == SessionSearchFailure.CursorExpired)
        {
            keepSnapshot = false;
            throw;
        }
        catch
        {
            // A failed first page never issued a cursor, so its snapshot would be orphaned; later pages stay resumable.
            if (firstPage) keepSnapshot = false;
            throw;
        }
        finally
        {
            if (!keepSnapshot) await CloseAsync(lease, pit);
        }
    }

    private async Task<(IReadOnlyList<SessionSearchItem> Items, SessionSearchPosition? Next, string Pit)> ReadPageAsync(
        SessionSearchRequest request, Guid lease, string pit, CancellationToken cancellationToken)
    {
        var items = new List<SessionSearchItem>(request.PageSize);
        Hit? lastTaken = null;
        var more = false;
        JsonArray? searchAfter = request.After is null ? null
            : [DateTimeOffset.Parse(request.After.StartedAt, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds(), request.After.DocumentKey];
        while (!more)
        {
            var (hits, newPit, raw) = await SearchBlockAsync(request, pit, searchAfter, cancellationToken);
            if (newPit != pit)
            {
                pit = newPit;
                if (!await leases.RecordPitAsync(lease, pit, time.GetUtcNow(), cancellationToken))
                    throw new SessionSearchException(SessionSearchFailure.CursorExpired);
            }
            if (raw == 0) break;
            var visible = hits.Count == 0 ? new HashSet<Guid>() : await visibility.VisibleAsync(hits.Select(hit => hit.Id).ToArray(), cancellationToken);
            foreach (var hit in hits)
            {
                if (!visible.Contains(hit.Id)) continue;
                if (items.Count < request.PageSize)
                {
                    items.Add(hit.Item);
                    lastTaken = hit;
                }
                else
                {
                    more = true; // one visible look-ahead hit proves a continuation exists
                    break;
                }
            }
            if (raw < options.BlockSize) break;
            // Advance over every examined hit, visible or not, so suppressed ones are never fetched again.
            // A full block of unreadable documents cannot be advanced over: fail visibly instead of ending the page silently.
            var tail = hits.Count > 0 ? hits[^1] : throw new SessionSearchException(SessionSearchFailure.Unavailable);
            searchAfter = [tail.StartedAtMillis, tail.DocumentKey];
        }
        var next = more && lastTaken is not null
            ? new SessionSearchPosition(string.Empty, lastTaken.Item.StartedAt, lastTaken.DocumentKey) : null;
        return (items, next, pit);
    }

    private async Task<(List<Hit> Hits, string Pit, int Raw)> SearchBlockAsync(SessionSearchRequest request, string pit,
        JsonArray? searchAfter, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["size"] = options.BlockSize,
            ["track_total_hits"] = false,
            ["pit"] = new JsonObject { ["id"] = pit, ["keep_alive"] = options.KeepAlive },
            ["query"] = BuildQuery(request),
            ["sort"] = new JsonArray(new JsonObject { ["startedAt"] = new JsonObject { ["order"] = "desc" } },
                new JsonObject { ["documentKey"] = new JsonObject { ["order"] = "asc" } }),
            ["_source"] = new JsonArray(SourceFields.Select(field => (JsonNode)field).ToArray())
        };
        if (searchAfter is not null) body["search_after"] = searchAfter.DeepClone();
        var response = await SendAsync(HttpMethod.Post, "_search", body, cancellationToken);
        var hits = new List<Hit>();
        var array = response["hits"]?["hits"]?.AsArray() ?? [];
        foreach (var node in array)
        {
            if (Parse(node) is { } hit) hits.Add(hit);
        }
        return (hits, response["pit_id"]?.GetValue<string>() ?? pit, array.Count);
    }

    private static JsonObject BuildQuery(SessionSearchRequest request)
    {
        var filters = new JsonArray
        {
            new JsonObject { ["range"] = new JsonObject { ["startedAt"] = new JsonObject
            {
                ["gte"] = Instant(request.From), ["lt"] = Instant(request.To), ["format"] = "strict_date_time"
            } } },
            Term("operation", "upsert")
        };
        // Authorization is part of the query, so unauthorized sessions are never retrieved and then filtered.
        var scope = new JsonArray();
        foreach (var pair in request.Scope)
            scope.Add(new JsonObject { ["bool"] = new JsonObject { ["filter"] = new JsonArray(Term("siteId", pair.SiteId), Term("sensorId", pair.SensorId)) } });
        filters.Add(new JsonObject { ["bool"] = new JsonObject { ["should"] = scope, ["minimum_should_match"] = 1 } });
        if (request.SourceIp is not null) filters.Add(Term("sourceIp", request.SourceIp));
        if (request.DestinationIp is not null) filters.Add(Term("destinationIp", request.DestinationIp));
        if (request.Protocol is not null) filters.Add(Term("protocol", request.Protocol));
        if (request.SourcePort is { } sourcePort) filters.Add(Term("sourcePort", sourcePort));
        if (request.DestinationPort is { } destinationPort) filters.Add(Term("destinationPort", destinationPort));
        return new JsonObject { ["bool"] = new JsonObject { ["filter"] = filters } };
    }

    private static JsonObject Term(string field, JsonNode value) => new() { ["term"] = new JsonObject { [field] = value } };
    private static string Instant(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // A document that does not match the contract is skipped rather than failing the whole page.
    private static Hit? Parse(JsonNode? node)
    {
        try
        {
            if (!Guid.TryParse(node?["_id"]?.GetValue<string>(), out var id) || node["_source"] is not JsonObject source
                || node["sort"] is not JsonArray { Count: >= 2 } sort) return null; // a PIT search appends its own _shard_doc tiebreaker
            var item = new SessionSearchItem(Text(source, "eventId"), Text(source, "siteId"), Text(source, "sensorId"), Text(source, "sourceIp"),
                Text(source, "destinationIp"), source["sourcePort"]!.GetValue<int>(), source["destinationPort"]!.GetValue<int>(),
                Text(source, "protocol"), Text(source, "startedAt"), Text(source, "endedAt"), Text(source, "provenance"),
                source["inferred"]?.GetValue<bool>(), source["partial"]?.GetValue<bool>());
            return new Hit(id, sort[0]!.GetValue<long>(), sort[1]!.GetValue<string>(), item);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or NullReferenceException)
        {
            return null;
        }
    }

    private static string Text(JsonObject source, string field) =>
        source[field]?.GetValue<string>() ?? throw new InvalidOperationException(field);

    private async Task<string> OpenPitAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, $"{Uri.EscapeDataString(options.Index)}/_pit?keep_alive={options.KeepAlive}", null, cancellationToken);
        return response["id"]?.GetValue<string>() ?? throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    private async Task CloseAsync(Guid lease, string pit)
    {
        // Best effort and never cancelled by the request: an orphaned snapshot only costs memory until its keep-alive ends.
        try
        {
            if (pit.Length > 0) await SendAsync(HttpMethod.Delete, "_pit", new JsonObject { ["id"] = pit }, CancellationToken.None);
        }
        catch (SessionSearchException) { }
        await leases.ReleaseAsync(lease, CancellationToken.None);
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, path);
        if (body is not null) message.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SessionSearchException(SessionSearchFailure.Unavailable);
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && path == "_search")
            {
                // A missing search context means the snapshot expired or was closed; any other 404 is an unavailable dependency.
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new SessionSearchException(text.Contains("search_context_missing", StringComparison.Ordinal)
                    ? SessionSearchFailure.CursorExpired : SessionSearchFailure.Unavailable);
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new SessionSearchException(SessionSearchFailure.Saturated);
            if (!response.IsSuccessStatusCode) throw new SessionSearchException(SessionSearchFailure.Unavailable);
            try
            {
                return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject()
                    ?? throw new SessionSearchException(SessionSearchFailure.Unavailable);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new SessionSearchException(SessionSearchFailure.Unavailable);
            }
        }
    }

    // The snapshot id handed to the host is "<lease>.<pit>": the lease is stable, the PIT id may rotate between pages.
    private static string SnapshotId(Guid lease, string pit) => $"{lease:D}.{pit}";

    private static bool TryParseSnapshot(string snapshot, out Guid lease, out string pit)
    {
        lease = default;
        pit = string.Empty;
        var split = snapshot.IndexOf('.', StringComparison.Ordinal);
        if (split != 36 || split == snapshot.Length - 1 || !Guid.TryParseExact(snapshot[..split], "D", out lease)) return false;
        pit = snapshot[(split + 1)..];
        return true;
    }
}

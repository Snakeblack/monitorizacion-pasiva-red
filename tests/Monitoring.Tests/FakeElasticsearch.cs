using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Monitoring.Tests;

// In-process stand-in for the Elasticsearch HTTP API subset the search adapter uses (PIT open/close, _search with
// pit/sort/search_after, bool/term/range queries). It evaluates the query the adapter sends, so a query that does not
// filter by scope or range fails here. It is NOT a substitute for the real engine, which this session could not run.
internal sealed class FakeElasticsearch : HttpMessageHandler
{
    internal sealed record Doc(string Id, JsonObject Source);

    private readonly object _gate = new();
    private readonly List<Doc> _docs = [];
    private readonly Dictionary<string, List<Doc>> _pits = [];
    private int _pitCounter;

    internal List<string> Calls { get; } = [];
    internal List<JsonObject> SearchBodies { get; } = [];
    internal bool RotatePitIdOnSearch { get; set; }
    internal HttpStatusCode? FailWith { get; set; }
    internal string? FailBody { get; set; }
    internal int OpenPits => _pits.Count;

    internal static Doc MakeDoc(Guid id, string site, string sensor, string eventId, DateTimeOffset startedAt, string protocol = "TCP",
        string sourceIp = "192.0.2.1", string destinationIp = "192.0.2.2", int sourcePort = 1234, int destinationPort = 443,
        string operation = "upsert", string provenance = "synthetic", bool? inferred = null, bool? partial = null) => new(id.ToString(), new JsonObject
        {
            ["schemaVersion"] = 2, ["operation"] = operation, ["documentKey"] = $"{site}.{sensor}.{eventId}", ["searchDocumentId"] = id.ToString(),
            ["revision"] = 1, ["siteId"] = site, ["sensorId"] = sensor, ["eventId"] = eventId,
            ["startedAt"] = startedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["endedAt"] = startedAt.AddSeconds(1).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["sourceIp"] = sourceIp, ["destinationIp"] = destinationIp, ["sourcePort"] = sourcePort, ["destinationPort"] = destinationPort,
            ["protocol"] = protocol, ["provenance"] = provenance, ["inferred"] = inferred, ["partial"] = partial
        });

    internal void Add(params Doc[] docs) { lock (_gate) _docs.AddRange(docs); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            Calls.Add($"{request.Method} {path}");
            if (FailWith is { } status) return Reply(status, FailBody ?? "{\"error\":{\"type\":\"unavailable\"}}");
            if (request.Method == HttpMethod.Post && path.EndsWith("/_pit", StringComparison.Ordinal))
            {
                var id = $"pit-{++_pitCounter}";
                _pits[id] = [.. _docs];
                return Reply(HttpStatusCode.OK, new JsonObject { ["id"] = id }.ToJsonString());
            }
            if (request.Method == HttpMethod.Delete && path == "/_pit")
            {
                var id = JsonNode.Parse(body!)!["id"]!.GetValue<string>();
                return Reply(_pits.Remove(id) ? HttpStatusCode.OK : HttpStatusCode.NotFound, "{\"succeeded\":true}");
            }
            if (request.Method == HttpMethod.Post && path == "/_search") return Search(JsonNode.Parse(body!)!.AsObject());
        }
        return Reply(HttpStatusCode.BadRequest, "{\"error\":{\"type\":\"unsupported\"}}");
    }

    private HttpResponseMessage Search(JsonObject request)
    {
        SearchBodies.Add((JsonObject)request.DeepClone());
        var pit = request["pit"]?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("The adapter must search inside a PIT.");
        if (!_pits.TryGetValue(pit, out var snapshot))
            return Reply(HttpStatusCode.NotFound, "{\"error\":{\"type\":\"search_context_missing_exception\"}}");
        if (request["track_total_hits"]?.GetValue<bool>() != false) throw new InvalidOperationException("Total hits must not be tracked.");
        var matches = snapshot.Where(doc => Matches(request["query"]!, doc.Source)).ToList();
        matches.Sort((left, right) =>
        {
            var byStart = Millis(right.Source).CompareTo(Millis(left.Source));            // startedAt desc
            return byStart != 0 ? byStart : string.CompareOrdinal(Key(left.Source), Key(right.Source)); // documentKey asc
        });
        if (request["search_after"] is JsonArray after)
        {
            var (afterMillis, afterKey) = (after[0]!.GetValue<long>(), after[1]!.GetValue<string>());
            matches = matches.Where(doc => Millis(doc.Source) < afterMillis || (Millis(doc.Source) == afterMillis && string.CompareOrdinal(Key(doc.Source), afterKey) > 0)).ToList();
        }
        var size = request["size"]!.GetValue<int>();
        var hits = new JsonArray();
        foreach (var doc in matches.Take(size))
            hits.Add(new JsonObject { ["_id"] = doc.Id, ["_source"] = doc.Source.DeepClone(), ["sort"] = new JsonArray(Millis(doc.Source), Key(doc.Source)) });
        var newPit = pit;
        if (RotatePitIdOnSearch) { newPit = pit + "'"; _pits[newPit] = _pits[pit]; _pits.Remove(pit); }
        return Reply(HttpStatusCode.OK, new JsonObject { ["pit_id"] = newPit, ["hits"] = new JsonObject { ["hits"] = hits } }.ToJsonString());
    }

    private static long Millis(JsonObject source) => DateTimeOffset.Parse(source["startedAt"]!.GetValue<string>()).ToUnixTimeMilliseconds();
    private static string Key(JsonObject source) => source["documentKey"]!.GetValue<string>();

    private static bool Matches(JsonNode query, JsonObject source)
    {
        var clause = query.AsObject();
        if (clause["bool"] is JsonObject boolean)
        {
            var filters = boolean["filter"]?.AsArray() ?? [];
            if (!filters.All(filter => Matches(filter!, source))) return false;
            if (boolean["should"] is JsonArray should)
            {
                if (boolean["minimum_should_match"]?.GetValue<int>() != 1) throw new InvalidOperationException("should clauses need minimum_should_match 1");
                if (!should.Any(item => Matches(item!, source))) return false;
            }
            return true;
        }
        if (clause["term"] is JsonObject term)
        {
            var (field, expected) = term.Single();
            var value = expected is JsonObject wrapped ? wrapped["value"] : expected;
            return source[field]?.ToJsonString() == value!.ToJsonString();
        }
        if (clause["range"] is JsonObject range)
        {
            var (field, bounds) = range.Single();
            if (field != "startedAt") throw new InvalidOperationException("only startedAt ranges are supported");
            var start = Millis(source);
            var gte = bounds!["gte"]; var lt = bounds["lt"];
            if (bounds["gt"] is not null || bounds["lte"] is not null) throw new InvalidOperationException("the range is [from,to)");
            return (gte is null || start >= DateTimeOffset.Parse(gte.GetValue<string>()).ToUnixTimeMilliseconds())
                && (lt is null || start < DateTimeOffset.Parse(lt.GetValue<string>()).ToUnixTimeMilliseconds());
        }
        throw new InvalidOperationException($"Unsupported query clause: {clause.ToJsonString()}");
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

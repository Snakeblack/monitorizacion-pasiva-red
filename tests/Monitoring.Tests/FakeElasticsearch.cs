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
    private readonly Dictionary<string, List<Doc>> _indices = [];
    private readonly Dictionary<string, string> _aliases = new() { ["sessions-read"] = DefaultIndex };
    internal const string DefaultIndex = "sessions-v2-000001";
    internal List<string> Created { get; } = [];
    internal bool FailAliasSwitch { get; set; }
    private readonly Dictionary<string, List<Doc>> _pits = [];
    private int _pitCounter;

    internal List<string> Calls { get; } = [];
    internal List<JsonObject> SearchBodies { get; } = [];
    internal bool RotatePitIdOnSearch { get; set; }
    internal HttpStatusCode? FailWith { get; set; }
    internal string? FailBody { get; set; }
    internal int OpenPits => _pits.Count;
    internal List<int> MgetSizes { get; } = [];
    internal bool FailMget { get; set; }

    // Documents of a physical index; the default one is the serving index backing the read alias.
    internal List<Doc> Store(string name) => name == DefaultIndex ? _docs : (_indices.TryGetValue(name, out var docs) ? docs : (_indices[name] = []));
    internal void Put(string index, Doc doc, bool onlyIfNewer = true)
    {
        lock (_gate)
        {
            var store = Store(index);
            var existing = store.FirstOrDefault(item => item.Id == doc.Id);
            if (existing is not null)
            {
                if (onlyIfNewer && existing.Source["revision"]!.GetValue<long>() >= doc.Source["revision"]!.GetValue<long>()) return;
                store.Remove(existing);
            }
            store.Add(doc);
        }
    }
    internal bool IndexExists(string name) { lock (_gate) return name == DefaultIndex || _indices.ContainsKey(name); }
    internal IReadOnlyList<string> AliasTargets(string alias) { lock (_gate) return _aliases.TryGetValue(alias, out var target) ? [target] : []; }
    private string Resolve(string name) => _aliases.TryGetValue(name, out var target) ? target : name;

    internal void Remove(Guid id) { lock (_gate) _docs.RemoveAll(doc => doc.Id == id.ToString()); }
    internal void Replace(Doc doc) { lock (_gate) { _docs.RemoveAll(existing => existing.Id == doc.Id); _docs.Add(doc); } }

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
                _pits[id] = [.. Store(Resolve(path.Split('/')[1]))];
                return Reply(HttpStatusCode.OK, new JsonObject { ["id"] = id }.ToJsonString());
            }
            if (request.Method == HttpMethod.Delete && path == "/_pit")
            {
                var id = JsonNode.Parse(body!)!["id"]!.GetValue<string>();
                return Reply(_pits.Remove(id) ? HttpStatusCode.OK : HttpStatusCode.NotFound, "{\"succeeded\":true}");
            }
            if (request.Method == HttpMethod.Post && path == "/_search") return Search(JsonNode.Parse(body!)!.AsObject());
            if (request.Method == HttpMethod.Post && path.EndsWith("/_refresh", StringComparison.Ordinal)) return Reply(HttpStatusCode.OK, "{\"_shards\":{\"failed\":0}}");
            if (request.Method == HttpMethod.Post && path.EndsWith("/_mget", StringComparison.Ordinal))
            {
                if (FailMget) return Reply(HttpStatusCode.ServiceUnavailable, "{}");
                var ids = JsonNode.Parse(body!)!["ids"]!.AsArray().Select(id => id!.GetValue<string>()).ToList();
                MgetSizes.Add(ids.Count);
                var store = Store(Resolve(path.Split('/')[1]));
                var docs = new JsonArray();
                foreach (var id in ids)
                {
                    var found = store.FirstOrDefault(doc => doc.Id == id);
                    docs.Add(found is null ? new JsonObject { ["_id"] = id, ["found"] = false }
                        : new JsonObject { ["_id"] = id, ["found"] = true, ["_source"] = found.Source.DeepClone() });
                }
                return Reply(HttpStatusCode.OK, new JsonObject { ["docs"] = docs }.ToJsonString());
            }
        }
        return Admin(request, path, body);
    }

    private HttpResponseMessage Admin(HttpRequestMessage request, string path, string? body)
    {
        var segments = path.Trim('/').Split('/');
        if (request.Method == HttpMethod.Get && segments is [var index, "_count"])
            return Reply(HttpStatusCode.OK, new JsonObject { ["count"] = Store(Resolve(index)).Count }.ToJsonString());
        if (request.Method == HttpMethod.Put && segments.Length == 1)
        {
            if (IndexExists(segments[0])) return Reply(HttpStatusCode.BadRequest, "{\"error\":{\"type\":\"resource_already_exists_exception\"}}");
            Store(segments[0]);
            Created.Add(segments[0]);
            return Reply(HttpStatusCode.OK, "{\"acknowledged\":true}");
        }
        if (request.Method == HttpMethod.Delete && segments.Length == 1)
        {
            var existed = _indices.Remove(segments[0]);
            return Reply(existed ? HttpStatusCode.OK : HttpStatusCode.NotFound, "{\"acknowledged\":true}");
        }
        if (request.Method == HttpMethod.Get && segments is ["_alias", var alias])
            return _aliases.TryGetValue(alias, out var target)
                ? Reply(HttpStatusCode.OK, new JsonObject { [target] = new JsonObject { ["aliases"] = new JsonObject { [alias] = new JsonObject() } } }.ToJsonString())
                : Reply(HttpStatusCode.NotFound, "{}");
        if (request.Method == HttpMethod.Post && segments is ["_aliases"])
        {
            if (FailAliasSwitch) return Reply(HttpStatusCode.InternalServerError, "{}");
            var actions = JsonNode.Parse(body!)!["actions"]!.AsArray();
            var next = new Dictionary<string, string>(_aliases);
            foreach (var action in actions)
            {
                if (action!["remove"] is JsonObject remove) next.Remove(remove["alias"]!.GetValue<string>());
                if (action["add"] is JsonObject add)
                {
                    if (!IndexExists(add["index"]!.GetValue<string>())) return Reply(HttpStatusCode.NotFound, "{\"error\":{\"type\":\"index_not_found_exception\"}}");
                    next[add["alias"]!.GetValue<string>()] = add["index"]!.GetValue<string>();
                }
            }
            _aliases.Clear();
            foreach (var pair in next) _aliases[pair.Key] = pair.Value;
            return Reply(HttpStatusCode.OK, "{\"acknowledged\":true}");
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

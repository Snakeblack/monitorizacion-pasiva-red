using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Persistence.Search;

public sealed class ElasticsearchProjectionIndex(HttpClient http, ElasticsearchOptions options) : IProjectionIndex
{
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"{Uri.EscapeDataString(options.Index)}/_refresh", null, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IndexedDocument>> GetAsync(IReadOnlyCollection<Guid> searchDocumentIds, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["ids"] = new JsonArray(searchDocumentIds.Select(id => (JsonNode)id.ToString("D")).ToArray()) };
        using var response = await SendAsync($"{Uri.EscapeDataString(options.Index)}/_mget?_source=revision,operation", body, cancellationToken);
        var result = new Dictionary<Guid, IndexedDocument>();
        var parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (var doc in parsed?["docs"]?.AsArray() ?? [])
        {
            if (doc?["found"]?.GetValue<bool>() != true || !Guid.TryParse(doc["_id"]?.GetValue<string>(), out var id)) continue;
            if (doc["_source"]?["revision"] is { } revision && doc["_source"]?["operation"] is { } operation)
                result[id] = new IndexedDocument(revision.GetValue<long>(), operation.GetValue<string>());
        }
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, JsonObject? body, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path);
            if (body is not null) message.Content = JsonContent.Create(body);
            var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new SessionSearchException(SessionSearchFailure.Unavailable);
            }
            return response;
        }
        catch (HttpRequestException)
        {
            throw new SessionSearchException(SessionSearchFailure.Unavailable);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions.Search;
using Npgsql;

namespace Monitoring.Persistence.Search;

public interface ISearchIndexAdmin
{
    Task CreateIndexAsync(string index, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> AliasTargetsAsync(string alias, CancellationToken cancellationToken);
    // One atomic alias update: the alias never points at no index nor at two of them.
    Task SwitchAliasAsync(string alias, IReadOnlyCollection<string> remove, string add, CancellationToken cancellationToken);
    Task DeleteIndexAsync(string index, CancellationToken cancellationToken);
}

// The sink that feeds one generation's index from that generation's topic.
public interface IGenerationSink
{
    Task StartAsync(GenerationInfo generation, CancellationToken cancellationToken);
    Task StopAsync(GenerationInfo generation, CancellationToken cancellationToken);
}

public sealed class ElasticsearchIndexAdmin(HttpClient http, string mappingJson) : ISearchIndexAdmin
{
    public async Task CreateIndexAsync(string index, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, Uri.EscapeDataString(index), JsonNode.Parse(mappingJson)!.AsObject(), cancellationToken);
        if (response.IsSuccessStatusCode) return;
        // A leftover index of an aborted attempt is reused only when it is already there.
        if (response.StatusCode == HttpStatusCode.BadRequest
            && (await response.Content.ReadAsStringAsync(cancellationToken)).Contains("resource_already_exists_exception", StringComparison.Ordinal)) return;
        throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    public async Task<IReadOnlyList<string>> AliasTargetsAsync(string alias, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"_alias/{Uri.EscapeDataString(alias)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        if (!response.IsSuccessStatusCode) throw new SessionSearchException(SessionSearchFailure.Unavailable);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject().Select(pair => pair.Key).Order().ToArray();
    }

    public async Task SwitchAliasAsync(string alias, IReadOnlyCollection<string> remove, string add, CancellationToken cancellationToken)
    {
        var actions = new JsonArray();
        foreach (var index in remove) actions.Add(new JsonObject { ["remove"] = new JsonObject { ["index"] = index, ["alias"] = alias } });
        actions.Add(new JsonObject { ["add"] = new JsonObject { ["index"] = add, ["alias"] = alias } });
        using var response = await SendAsync(HttpMethod.Post, "_aliases", new JsonObject { ["actions"] = actions }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    public async Task DeleteIndexAsync(string index, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, Uri.EscapeDataString(index), null, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound) throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(method, path);
            if (body is not null) message.Content = JsonContent.Create(body);
            return await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SessionSearchException(SessionSearchFailure.Unavailable);
        }
    }
}

// Kafka Connect REST: one Elasticsearch sink connector per generation, derived from the versioned sink configuration.
public sealed partial class ConnectGenerationSink(HttpClient http, string templateJson) : IGenerationSink
{
    public static string ConnectorName(int generation) => generation == 1 ? "monitoring-sink" : $"monitoring-sink-g{generation}";

    public async Task StartAsync(GenerationInfo generation, CancellationToken cancellationToken)
    {
        var config = JsonNode.Parse(templateJson)!.AsObject();
        config["topics"] = generation.Topic;
        config["transforms.index.regex"] = Regex.Escape(generation.Topic);
        config["transforms.index.replacement"] = generation.IndexName;
        config["errors.deadletterqueue.topic.name"] = generation.Generation == 1 ? "monitoring.sessions.dlq" : $"monitoring.sessions.dlq.g{generation.Generation}";
        using var response = await SendAsync(HttpMethod.Put, $"connectors/{ConnectorName(generation.Generation)}/config", config, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    public async Task StopAsync(GenerationInfo generation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"connectors/{ConnectorName(generation.Generation)}", null, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound) throw new SessionSearchException(SessionSearchFailure.Unavailable);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(method, path);
            if (body is not null) message.Content = JsonContent.Create(body);
            return await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SessionSearchException(SessionSearchFailure.Unavailable);
        }
    }
}

public enum RebuildOutcome { Switched, Aborted, TimedOut }

public sealed record RebuildResult(RebuildOutcome Outcome, int Generation, string? Reason);

public sealed class RebuildOptions
{
    public string Alias { get; set; } = "sessions-read";
    // Writers are paused only inside this budget; beyond it the switch is abandoned and the previous index keeps serving.
    public TimeSpan FinalPhaseBudget { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}

// Builds a new index generation next to the serving one and switches to it without losing a change or resurrecting a
// suppressed session. The serving index is never modified; any failure before the alias switch leaves it untouched.
public sealed class SearchRebuildCoordinator(MonitoringDbContext dbContext, SearchGenerationStore store, ISearchIndexAdmin admin,
    IGenerationSink sink, ProjectionReconciler reconciler, RebuildOptions options)
{
    public async Task<RebuildResult> RunAsync(CancellationToken cancellationToken)
    {
        var generation = await store.BeginAsync(cancellationToken);
        try
        {
            await admin.CreateIndexAsync(generation.IndexName, cancellationToken);
            await sink.StartAsync(generation, cancellationToken);
            await store.CopyAllAsync(generation, cancellationToken);
            await store.CatchUpAsync(generation, cancellationToken);
            await store.MarkReadyAsync(generation.Generation, cancellationToken);
        }
        catch (SearchGenerationException exception) when (exception.Code == "snapshot-deadline")
        {
            await DiscardAsync(generation);
            return new RebuildResult(RebuildOutcome.Aborted, generation.Generation, exception.Code);
        }
        catch
        {
            await DiscardAsync(generation);
            throw;
        }
        return await SwitchAsync(generation, cancellationToken);
    }

    // Final phase for a generation that is ready; also the retry entry point after a timed-out attempt.
    public async Task<RebuildResult> SwitchAsync(GenerationInfo generation, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using var lockConnection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        await lockConnection.OpenAsync(cancellationToken);
        var locked = false;
        try
        {
            // Every session and retention writer takes this lock shared; holding it exclusively pauses them all.
            await using (var timeout = new NpgsqlCommand($"SET lock_timeout = {(int)options.FinalPhaseBudget.TotalMilliseconds}", lockConnection))
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            try
            {
                await using var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@lock)", lockConnection);
                acquire.Parameters.AddWithValue("lock", Sessions.OutboxStore.PublicationLock);
                await acquire.ExecuteNonQueryAsync(cancellationToken);
                locked = true;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                return new RebuildResult(RebuildOutcome.TimedOut, generation.Generation, "lock-timeout");
            }

            await store.CatchUpAsync(generation, cancellationToken);
            while (true)
            {
                var verification = await reconciler.VerifyGenerationAsync(generation.Topic, generation.IndexName, cancellationToken);
                if (verification.Converged) break;
                if (clock.Elapsed >= options.FinalPhaseBudget)
                    return new RebuildResult(RebuildOutcome.TimedOut, generation.Generation, "sink-not-caught-up");
                await Task.Delay(options.PollInterval, cancellationToken);
            }

            var serving = await admin.AliasTargetsAsync(options.Alias, cancellationToken);
            await admin.SwitchAliasAsync(options.Alias, serving, generation.IndexName, cancellationToken);
            try
            {
                await store.ActivateAsync(generation.Generation, cancellationToken);
            }
            catch
            {
                // The authority did not switch writers: put the alias back so reads and writes stay on the same generation.
                if (serving.Count > 0) await admin.SwitchAliasAsync(options.Alias, [generation.IndexName], serving[0], CancellationToken.None);
                throw;
            }
            return new RebuildResult(RebuildOutcome.Switched, generation.Generation, null);
        }
        finally
        {
            if (locked)
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock)", lockConnection);
                release.Parameters.AddWithValue("lock", Sessions.OutboxStore.PublicationLock);
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    // Stops the sink and drops the index of a generation that was retired by a successful switch (after the rollback window).
    public async Task DecommissionAsync(GenerationInfo retired, CancellationToken cancellationToken)
    {
        await sink.StopAsync(retired, cancellationToken);
        await admin.DeleteIndexAsync(retired.IndexName, cancellationToken);
    }

    private async Task DiscardAsync(GenerationInfo generation)
    {
        // Best effort and never cancelled: leftover infrastructure of an aborted generation is only waste.
        try { await sink.StopAsync(generation, CancellationToken.None); } catch (SessionSearchException) { }
        try { await admin.DeleteIndexAsync(generation.IndexName, CancellationToken.None); } catch (SessionSearchException) { }
        await store.AbortAsync(generation.Generation, CancellationToken.None);
    }
}

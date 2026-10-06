using Microsoft.EntityFrameworkCore;
using Monitoring.Persistence;
using Monitoring.Persistence.Search;

namespace Monitoring.Host.Sessions;

// `--rebuild-search`: builds a new index generation and switches to it. Exit codes: 0 switched, 2 not switched (aborted or
// timed out; the previous index keeps serving and a retry is safe), 1 failure or invalid configuration. Output never
// contains connection strings, hosts, keys or identifiers.
public static class SearchRebuildCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Monitoring");
        var searchUrl = configuration["Search:Elasticsearch:Url"];
        var connectUrl = configuration["Search:Connect:Url"];
        var mappingPath = configuration["Search:Rebuild:MappingPath"];
        var sinkPath = configuration["Search:Rebuild:SinkTemplatePath"];
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(searchUrl) || string.IsNullOrWhiteSpace(connectUrl)
            || mappingPath is null || sinkPath is null || !File.Exists(mappingPath) || !File.Exists(sinkPath))
        {
            await error.WriteLineAsync("Search rebuild failed: ConnectionStrings:Monitoring, Search:Elasticsearch:Url, Search:Connect:Url, Search:Rebuild:MappingPath and Search:Rebuild:SinkTemplatePath are required and must exist.");
            return 1;
        }
        try
        {
            var options = new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connectionString).Options;
            await using var db = new MonitoringDbContext(options);
            await using var storeDb = new MonitoringDbContext(options);
            await using var reconcilerDb = new MonitoringDbContext(options);
            var elastic = new HttpClient { BaseAddress = new Uri(searchUrl), Timeout = TimeSpan.FromSeconds(30) };
            if (configuration["Search:Elasticsearch:ApiKey"] is { Length: > 0 } apiKey)
                elastic.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", apiKey);
            using var connect = new HttpClient { BaseAddress = new Uri(connectUrl), Timeout = TimeSpan.FromSeconds(30) };
            var searchOptions = configuration.GetSection("Search:Elasticsearch").Get<ElasticsearchOptions>() ?? new ElasticsearchOptions();
            var rebuildOptions = configuration.GetSection("Search:Rebuild").Get<RebuildOptions>() ?? new RebuildOptions();
            rebuildOptions.Alias = searchOptions.Index;
            var coordinator = new SearchRebuildCoordinator(db,
                new SearchGenerationStore(storeDb, configuration.GetSection("Search:Generations").Get<GenerationOptions>() ?? new GenerationOptions()),
                new ElasticsearchIndexAdmin(elastic, await File.ReadAllTextAsync(mappingPath, cancellationToken)),
                new ConnectGenerationSink(connect, await File.ReadAllTextAsync(sinkPath, cancellationToken)),
                new ProjectionReconciler(reconcilerDb, new ElasticsearchProjectionIndex(elastic, searchOptions),
                    new ProjectionOptions { Grace = TimeSpan.Zero, BlockSize = 100 }),
                rebuildOptions);
            var result = await coordinator.RunAsync(cancellationToken);
            await output.WriteLineAsync($"Search rebuild generation {result.Generation}: {result.Outcome}{(result.Reason is null ? "" : $" ({result.Reason})")}.");
            return result.Outcome == RebuildOutcome.Switched ? 0 : 2;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Search rebuild failed ({exception.GetType().Name}); the previous index keeps serving.");
            return 1;
        }
    }
}

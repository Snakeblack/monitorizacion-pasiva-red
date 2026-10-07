using Microsoft.EntityFrameworkCore;
using Monitoring.Persistence;
using Monitoring.Persistence.Ingestion;

namespace Monitoring.Host.Sessions;

// `--quarantine list|summary|discard|replace`: the audited operational access to the ingestion quarantine. Exit codes:
// 0 done, 2 not applicable (already resolved, unknown, invalid replacement), 1 invalid usage or failure. Output carries
// identities, causes and states only, never event payloads, connection strings or hosts.
public static class QuarantineCommand
{
    private const string Usage = "Usage: --quarantine list [--state S] [--limit N] | summary | discard <site> <sensor> <event> --actor A --reason R | replace <site> <sensor> <event> <replacement> --actor A --reason R";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Monitoring");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync("Quarantine command failed: ConnectionStrings:Monitoring is required.");
            return 1;
        }
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length) return await UsageAsync(error);
                options[args[index]] = args[++index];
            }
            else positional.Add(args[index]);
        }
        try
        {
            await using var db = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connectionString).Options);
            var service = new QuarantineService(db);
            switch (positional.FirstOrDefault())
            {
                case "list" when positional.Count == 1:
                    var limit = options.TryGetValue("--limit", out var limitText) && int.TryParse(limitText, out var parsed) ? parsed : 100;
                    foreach (var entry in await service.ListAsync(options.GetValueOrDefault("--state"), limit, cancellationToken))
                        await output.WriteLineAsync($"{entry.SiteId}/{entry.SensorId}/{entry.EventId} cause={entry.Cause} state={entry.State} attempts={entry.Attempts} quarantinedAt={entry.QuarantinedAt:O}"
                            + (entry.ReplacedByEventId is null ? "" : $" replacedBy={entry.ReplacedByEventId}"));
                    return 0;
                case "summary" when positional.Count == 1:
                    var summary = await service.SummaryAsync(cancellationToken);
                    await output.WriteLineAsync($"accepted={summary.Accepted} processed={summary.Processed} quarantined={summary.Quarantined} pending={summary.Pending} "
                        + $"unresolved={summary.UnresolvedByCause.Values.Sum()} replaced={summary.Replaced} discarded={summary.Discarded} "
                        + $"oldestUnresolvedSeconds={(summary.OldestUnresolvedAgeSeconds is { } age ? ((long)age).ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}");
                    foreach (var (cause, count) in summary.UnresolvedByCause.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        await output.WriteLineAsync($"unresolved cause={cause} count={count}");
                    return 0;
                case "discard" when positional.Count == 4:
                    return await Report(await service.DiscardAsync(positional[1], positional[2], positional[3], Required(options, "--actor"), Required(options, "--reason"), cancellationToken), output);
                case "replace" when positional.Count == 5:
                    return await Report(await service.ReplaceAsync(positional[1], positional[2], positional[3], positional[4], Required(options, "--actor"), Required(options, "--reason"), cancellationToken), output);
                default:
                    return await UsageAsync(error);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            await error.WriteLineAsync(exception is ArgumentException { ParamName: "actor" or "reason" or "state" } argument ? argument.Message : Usage);
            return 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Quarantine command failed ({exception.GetType().Name}).");
            return 1;
        }
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"{name} is required.", name.TrimStart('-'));

    private static async Task<int> Report(ResolutionResult result, TextWriter output)
    {
        await output.WriteLineAsync(result switch
        {
            ResolutionResult.Resolved => "resolved",
            ResolutionResult.AlreadyResolved => "already-resolved",
            ResolutionResult.NotFound => "not-quarantined",
            _ => "invalid-replacement"
        });
        return result == ResolutionResult.Resolved ? 0 : 2;
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 1;
    }
}

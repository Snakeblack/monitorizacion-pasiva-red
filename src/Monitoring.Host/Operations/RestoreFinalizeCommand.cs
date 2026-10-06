using Microsoft.EntityFrameworkCore;
using Monitoring.Host.Sessions;
using Monitoring.Persistence;
using Monitoring.Persistence.Retention;

namespace Monitoring.Host.Operations;

// `--restore-finalize`: the mandatory step after restoring a backup or promoting a node. A restore brings back history that retention
// had already removed (and loses the replication slot and any index state newer than the backup), so before ingestion or the
// connector are reopened this re-applies retention to completion and, when a search index is configured, rebuilds it from the
// authority, which also discards any document the restored authority no longer holds. Exit codes: 0 done, 1 refused or failed (a
// partial outcome is stated in the output; both steps are idempotent and safe to repeat). Output never contains connection strings,
// hosts or identifiers.
public static class RestoreFinalizeCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Monitoring");
        var section = configuration.GetSection("Retention");
        if (string.IsNullOrWhiteSpace(connectionString) || !section.Exists())
        {
            await error.WriteLineAsync("Restore finalize refused: ConnectionStrings:Monitoring and an explicit Retention configuration are required.");
            return 1;
        }
        RetentionReport report;
        try
        {
            var options = section.Get<RetentionOptions>() ?? new RetentionOptions();
            options.Validate();
            await using var db = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connectionString).Options);
            report = await new RetentionService(db, options).RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Restore finalize failed in retention ({exception.GetType().Name}); nothing is served as finalized. Fix the cause and run it again.");
            return 1;
        }
        await output.WriteLineAsync($"Retention completed: expiredSessions={report.ExpiredSessions} deletedObservations={report.DeletedObservations} "
            + $"deletedInbox={report.DeletedInbox} deletedOutbox={report.DeletedOutbox} purgedTombstones={report.PurgedTombstones} outboxSkipped={report.OutboxSkipped}");
        if (string.IsNullOrWhiteSpace(configuration["Search:Elasticsearch:Url"]))
        {
            await output.WriteLineAsync("No search index configured: nothing to rebuild.");
            return 0;
        }
        var rebuild = await SearchRebuildCommand.RunAsync(configuration, output, error, cancellationToken);
        if (rebuild != 0) await error.WriteLineAsync("Restore finalize incomplete: the search rebuild did not switch; the retention step is done and the command can be repeated.");
        return rebuild == 0 ? 0 : 1;
    }
}

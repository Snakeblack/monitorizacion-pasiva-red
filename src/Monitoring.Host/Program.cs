using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Monitoring.Host.Ingestion;
using Monitoring.Persistence;
using Monitoring.Persistence.Ingestion;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Host.Operations;
using Monitoring.Host.Security;
using Monitoring.Host.Sessions;
using Monitoring.Host.Inventory;
using Monitoring.Persistence.Inventory;
using Monitoring.Persistence.Retention;
using Monitoring.Persistence.Search;
using Monitoring.Persistence.Sessions;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    var connectionString = builder.Configuration.GetConnectionString("Monitoring");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Database migration failed: ConnectionStrings:Monitoring is required.");
        Environment.ExitCode = 1;
        return;
    }

    try
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options;
        await using var dbContext = new MonitoringDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        // Serialize the complete migration + resumable historical backfill, not just schema DDL.
        await using var migrationLock = new NpgsqlCommand("SELECT pg_advisory_lock(7182041000)",
            (NpgsqlConnection)dbContext.Database.GetDbConnection());
        await migrationLock.ExecuteNonQueryAsync();
        await dbContext.Database.MigrateAsync();
        await new CanonicalSessionInitializer(dbContext).RunAsync(CancellationToken.None);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Database migration failed ({exception.GetType().Name}); check PostgreSQL availability and the configured connection string.");
        Environment.ExitCode = 1;
    }

    return;
}

if (args.Length > 0 && args[0] == "--quarantine")
{
    Environment.ExitCode = await QuarantineCommand.RunAsync(args[1..], builder.Configuration, Console.Out, Console.Error, CancellationToken.None);
    return;
}

if (args.Contains("--project-pending", StringComparer.Ordinal))
{
    // One projection pass over everything pending, for operations and recovery rehearsals; the worker does the same continuously.
    var projectionConnection = builder.Configuration.GetConnectionString("Monitoring");
    if (string.IsNullOrWhiteSpace(projectionConnection))
    {
        Console.Error.WriteLine("Projection failed: ConnectionStrings:Monitoring is required.");
        Environment.ExitCode = 1;
        return;
    }
    await using var projectionDb = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(projectionConnection).Options);
    await new SessionProjector(projectionDb).RunPassAsync(CancellationToken.None);
    return;
}

if (args.Contains("--restore-finalize", StringComparer.Ordinal))
{
    Environment.ExitCode = await RestoreFinalizeCommand.RunAsync(builder.Configuration, Console.Out, Console.Error, CancellationToken.None);
    return;
}

if (args.Length > 0 && args[0] == "--probes")
{
    Environment.ExitCode = await ProbesCommand.RunAsync(args[1..], builder.Configuration, Console.Out, Console.Error, CancellationToken.None);
    return;
}

if (args.Contains("--rebuild-search", StringComparer.Ordinal))
{
    Environment.ExitCode = await SearchRebuildCommand.RunAsync(builder.Configuration, Console.Out, Console.Error, CancellationToken.None);
    return;
}

builder.Services.TryAddSingleton<ITrustedSensorIdentityProvider, HostContextTrustedSensorIdentityProvider>();
builder.Services.TryAddSingleton<IngestionRejectionMetrics>();
builder.Services.TryAddSingleton<ITrustedSessionReadContextProvider, HostContextTrustedSessionReadContextProvider>();
// Human identity: the explicit development read mode only in Development/Testing, otherwise validated OIDC; an insecure or
// incomplete configuration throws here and the host does not start.
builder.Services.AddMonitoringIdentity(builder.Configuration, builder.Environment);
builder.Services.TryAddSingleton<IAccessAudit, LoggingAccessAudit>();
builder.Services.AddScoped<AccessGate>();
builder.Services.TryAddSingleton<InventoryCursor>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.TryAddSingleton(new SessionSearchOptions());
builder.Services.TryAddSingleton<SessionSearchGate>();
// Cursors are sealed with Data Protection; every instance must share a persisted key ring or its cursors are rejected as malformed.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Monitoring");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}
builder.Services.TryAddSingleton<SessionCursor>();
builder.Services.TryAddSingleton<ISessionSearch, UnconfiguredSessionSearch>();
var monitoringConnection = builder.Configuration.GetConnectionString("Monitoring");
if (string.IsNullOrWhiteSpace(monitoringConnection))
{
    builder.Services.TryAddSingleton<IIngestionBatchWriter, UnconfiguredIngestionBatchWriter>();
    builder.Services.TryAddSingleton<ISessionReader, UnconfiguredSessionReader>();
}
else
{
    builder.Services.AddDbContext<MonitoringDbContext>(options => options.UseNpgsql(monitoringConnection));
    // Retention is mandatory outside Development/Testing: keeping traffic data indefinitely is not a default this product accepts.
    var retentionSection = builder.Configuration.GetSection("Retention");
    var localEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
    if (!retentionSection.Exists() && !localEnvironment)
        throw new InvalidOperationException("Retention must be configured (Retention:SessionRetention, OutboxRetention, TombstoneMargin).");
    var retention = retentionSection.Exists() ? retentionSection.Get<RetentionOptions>() ?? new RetentionOptions() : null;
    retention?.Validate();
    var ingestionOptions = builder.Configuration.GetSection("Ingestion").Get<IngestionOptions>() ?? new IngestionOptions();
    if (retention is not null) ingestionOptions.EventRetention = retention.SessionRetention;
    builder.Services.TryAddSingleton(ingestionOptions);
    builder.Services.TryAddScoped<InboxWriter>();
    builder.Services.TryAddScoped<IIngestionBatchWriter, PersistentIngestionBatchWriter>();
    builder.Services.TryAddScoped<SessionReader>();
    builder.Services.TryAddScoped<ISessionReader, PersistentSessionReader>();
    if (retention is not null)
    {
        builder.Services.TryAddSingleton(retention);
        builder.Services.TryAddScoped(provider => new SessionProjector(provider.GetRequiredService<MonitoringDbContext>(), retention));
        builder.Services.AddHostedService<RetentionWorker>();
    }
    else
    {
        builder.Services.TryAddScoped<SessionProjector>();
    }
    builder.Services.AddHostedService<SessionProjectionWorker>();
    builder.Services.AddHostedService<QuarantineMetrics>();
    builder.Services.AddHostedService<PipelineMetrics>();
    builder.Services.AddScoped<IAccessAudit, PostgresAccessAudit>();
    builder.Services.AddScoped<InventoryService>();
    builder.Services.AddScoped<IProbeRegistry, ProbeRegistry>();
    builder.Services.TryAddScoped<ISessionVisibility, PostgresSessionVisibility>();
    builder.Services.TryAddSingleton(builder.Configuration.GetSection("Search:Leases").Get<SnapshotLeaseOptions>() ?? new SnapshotLeaseOptions());
    builder.Services.TryAddScoped<ISnapshotLeases, PostgresSnapshotLeases>();
    if (builder.Configuration["Search:Elasticsearch:Url"] is { Length: > 0 } searchUrl)
    {
        var searchOptions = builder.Configuration.GetSection("Search:Elasticsearch").Get<ElasticsearchOptions>() ?? new ElasticsearchOptions();
        builder.Services.TryAddSingleton(searchOptions);
        // The 10 s search deadline lives in the request token, so the client itself never imposes a shorter or longer one.
        builder.Services.AddHttpClient<ElasticsearchSessionSearch>(client =>
        {
            client.BaseAddress = new Uri(searchUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            if (builder.Configuration["Search:Elasticsearch:ApiKey"] is { Length: > 0 } apiKey)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", apiKey);
            }
        });
        builder.Services.AddTransient<ISessionSearch>(provider => provider.GetRequiredService<ElasticsearchSessionSearch>());
        // Freshness is derived from recorded verifications of the index against the authority.
        var projectionOptions = builder.Configuration.GetSection("Search:Projection").Get<ProjectionOptions>() ?? new ProjectionOptions();
        builder.Services.TryAddSingleton(projectionOptions);
        builder.Services.AddHttpClient<IProjectionIndex, ElasticsearchProjectionIndex>(client =>
        {
            client.BaseAddress = new Uri(searchUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            if (builder.Configuration["Search:Elasticsearch:ApiKey"] is { Length: > 0 } apiKey)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", apiKey);
            }
        });
        builder.Services.AddScoped<IProjectionStatus, PostgresProjectionStatus>();
        builder.Services.AddScoped<ProjectionReconciler>();
        builder.Services.AddHostedService<ProjectionReconciliationWorker>();
    }
    builder.Services.TryAddScoped<IProjectionStatus, UnknownProjectionStatus>();
}

var probeCertificates = builder.AddProbeCertificateAuthentication();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseMiddleware<TrustedSessionReadScopeMiddleware>();
}

if (probeCertificates)
{
    app.UseMiddleware<ProbeCertificateMiddleware>();
}

app.MapGet("/health/live", () => Results.Ok());
app.MapBatchEndpoint();
app.MapSessionSearchEndpoint();
app.MapInventoryEndpoints();
app.MapSessionEndpoint();

app.Run();

public partial class Program;

internal sealed class PersistentIngestionBatchWriter(InboxWriter inboxWriter) : IIngestionBatchWriter
{
    public async Task<IngestionWriteResult> WriteAsync(
        TrustedSensorIdentity identity,
        Monitoring.Domain.Ingestion.IngestionBatch batch,
        CancellationToken cancellationToken)
    {
        var result = await inboxWriter.WriteAsync(identity.SiteId, identity.SensorId, batch, cancellationToken);
        return result switch
        {
            InboxWriteResult.Accepted => IngestionWriteResult.Accepted,
            InboxWriteResult.Conflict => IngestionWriteResult.Conflict,
            InboxWriteResult.RateLimited => IngestionWriteResult.RateLimited,
            _ => IngestionWriteResult.Failed
        };
    }
}

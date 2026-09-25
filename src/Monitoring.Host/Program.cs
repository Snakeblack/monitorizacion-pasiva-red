using Microsoft.EntityFrameworkCore;
using Monitoring.Host.Ingestion;
using Monitoring.Persistence;
using Monitoring.Persistence.Ingestion;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        await dbContext.Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Database migration failed ({exception.GetType().Name}); check PostgreSQL availability and the configured connection string.");
        Environment.ExitCode = 1;
    }

    return;
}

builder.Services.TryAddSingleton<ITrustedSensorIdentityProvider, HostContextTrustedSensorIdentityProvider>();
builder.Services.TryAddSingleton<IngestionRejectionMetrics>();
var monitoringConnection = builder.Configuration.GetConnectionString("Monitoring");
if (string.IsNullOrWhiteSpace(monitoringConnection))
{
    builder.Services.TryAddSingleton<IIngestionBatchWriter, UnconfiguredIngestionBatchWriter>();
}
else
{
    builder.Services.AddDbContext<MonitoringDbContext>(options => options.UseNpgsql(monitoringConnection));
    builder.Services.TryAddScoped<InboxWriter>();
    builder.Services.TryAddScoped<IIngestionBatchWriter, PersistentIngestionBatchWriter>();
}

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok());
app.MapBatchEndpoint();

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

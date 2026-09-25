using Microsoft.EntityFrameworkCore;
using Monitoring.Host.Ingestion;
using Monitoring.Persistence;
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
builder.Services.TryAddSingleton<IIngestionBatchWriter, UnconfiguredIngestionBatchWriter>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok());
app.MapBatchEndpoint();

app.Run();

public partial class Program;

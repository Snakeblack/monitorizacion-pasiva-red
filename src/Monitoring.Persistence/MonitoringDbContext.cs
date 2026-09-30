using Microsoft.EntityFrameworkCore;
using Monitoring.Persistence.Ingestion;
using Monitoring.Persistence.Sessions;

namespace Monitoring.Persistence;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : DbContext(options)
{
    internal DbSet<IngestionOriginEntity> IngestionOrigins => Set<IngestionOriginEntity>();

    internal DbSet<IngestionInboxEntity> IngestionInbox => Set<IngestionInboxEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new IngestionOriginEntityConfiguration());
        modelBuilder.ApplyConfiguration(new IngestionInboxEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SessionProjectionEntityConfiguration());
    }
}

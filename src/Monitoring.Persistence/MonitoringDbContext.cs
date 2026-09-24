using Microsoft.EntityFrameworkCore;

namespace Monitoring.Persistence;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : DbContext(options);

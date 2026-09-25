using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Monitoring.Persistence.Ingestion;

internal sealed class IngestionOriginEntityConfiguration : IEntityTypeConfiguration<IngestionOriginEntity>
{
    public void Configure(EntityTypeBuilder<IngestionOriginEntity> builder)
    {
        builder.ToTable("ingestion_origin", "monitoring");
        builder.HasKey(origin => new { origin.SiteId, origin.SensorId })
            .HasName("PK_ingestion_origin");
        builder.Property(origin => origin.SiteId)
            .HasColumnName("site_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(origin => origin.SensorId)
            .HasColumnName("sensor_id")
            .HasMaxLength(128)
            .IsRequired();
    }
}

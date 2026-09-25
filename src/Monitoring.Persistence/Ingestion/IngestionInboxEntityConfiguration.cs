using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Monitoring.Persistence.Ingestion;

internal sealed class IngestionInboxEntityConfiguration : IEntityTypeConfiguration<IngestionInboxEntity>
{
    public void Configure(EntityTypeBuilder<IngestionInboxEntity> builder)
    {
        builder.ToTable("ingestion_inbox", "monitoring");
        builder.HasKey(item => new { item.SiteId, item.SensorId, item.EventId })
            .HasName("PK_ingestion_inbox");
        builder.Property(item => item.SiteId)
            .HasColumnName("site_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(item => item.SensorId)
            .HasColumnName("sensor_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(item => item.EventId)
            .HasColumnName("event_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(item => item.BatchId)
            .HasColumnName("batch_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(item => item.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();
        builder.Property(item => item.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();
        builder.Property(item => item.Data)
            .HasColumnName("data")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(item => item.AcceptedAt)
            .HasColumnName("accepted_at")
            .IsRequired();
        builder.HasIndex(item => new { item.SiteId, item.SensorId, item.AcceptedAt })
            .HasDatabaseName("IX_ingestion_inbox_site_id_sensor_id_accepted_at");
        builder.HasOne<IngestionOriginEntity>()
            .WithMany()
            .HasForeignKey(item => new { item.SiteId, item.SensorId })
            .HasConstraintName("FK_ingestion_inbox_ingestion_origin_site_id_sensor_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

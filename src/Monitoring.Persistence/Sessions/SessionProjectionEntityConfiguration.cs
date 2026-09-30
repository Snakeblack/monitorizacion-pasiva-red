using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Monitoring.Persistence.Ingestion;

namespace Monitoring.Persistence.Sessions;

internal sealed class SessionProjectionEntityConfiguration : IEntityTypeConfiguration<SessionProjectionEntity>
{
    public void Configure(EntityTypeBuilder<SessionProjectionEntity> builder)
    {
        builder.ToTable("session_projection", "monitoring");
        builder.HasKey(item => new { item.SiteId, item.SensorId, item.EventId }).HasName("PK_session_projection");
        builder.Property(item => item.SiteId).HasColumnName("site_id").HasMaxLength(128).IsRequired();
        builder.Property(item => item.SensorId).HasColumnName("sensor_id").HasMaxLength(128).IsRequired();
        builder.Property(item => item.EventId).HasColumnName("event_id").HasMaxLength(128).IsRequired();
        builder.Property(item => item.OccurredAtText).HasColumnName("occurred_at_text").HasColumnType("text").IsRequired();
        builder.Property(item => item.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        builder.HasOne<IngestionInboxEntity>().WithMany()
            .HasForeignKey(item => new { item.SiteId, item.SensorId, item.EventId })
            .HasConstraintName("FK_session_projection_ingestion_inbox").OnDelete(DeleteBehavior.Restrict);
    }
}

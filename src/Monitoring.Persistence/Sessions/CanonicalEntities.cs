using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Monitoring.Persistence.Sessions;

internal sealed class SessionIdentityEntity
{
    public required string SiteId { get; set; }
    public required string SensorId { get; set; }
    public required string EventId { get; set; }
    public required string DocumentKey { get; set; }
    public long Revision { get; set; }
    public required string State { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class SessionMetadataEntity
{
    public required string SiteId { get; set; }
    public required string SensorId { get; set; }
    public required string EventId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public required IPAddress SourceIp { get; set; }
    public required IPAddress DestinationIp { get; set; }
    public int SourcePort { get; set; }
    public int DestinationPort { get; set; }
    public required string Protocol { get; set; }
    public int? VlanId { get; set; }
    public required string Provenance { get; set; }
    public long Revision { get; set; }
    public bool? Inferred { get; set; }
    public bool? Partial { get; set; }
    public string? CloseReason { get; set; }
    public long? PacketCount { get; set; }
    public long? ByteCount { get; set; }
}

internal sealed class ProjectionOutboxEntity
{
    public Guid Id { get; set; }
    public required string AggregateId { get; set; }
    public required string AggregateType { get; set; }
    public required string TargetTopic { get; set; }
    public long Revision { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal static class CanonicalEntities
{
    internal static void Configure(ModelBuilder model)
    {
        var identity = model.Entity<SessionIdentityEntity>();
        identity.ToTable("session_identity", "monitoring");
        identity.HasKey(x => new { x.SiteId, x.SensorId, x.EventId });
        identity.Property(x => x.SiteId).HasColumnName("site_id").HasMaxLength(128);
        identity.Property(x => x.SensorId).HasColumnName("sensor_id").HasMaxLength(128);
        identity.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(128);
        identity.Property(x => x.DocumentKey).HasColumnName("document_key");
        identity.Property(x => x.Revision).HasColumnName("revision");
        identity.Property(x => x.State).HasColumnName("state");
        identity.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        var metadata = model.Entity<SessionMetadataEntity>();
        metadata.ToTable("session_metadata", "monitoring");
        metadata.HasKey(x => new { x.SiteId, x.SensorId, x.EventId });
        metadata.Property(x => x.SiteId).HasColumnName("site_id").HasMaxLength(128);
        metadata.Property(x => x.SensorId).HasColumnName("sensor_id").HasMaxLength(128);
        metadata.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(128);
        metadata.Property(x => x.StartedAt).HasColumnName("started_at");
        metadata.Property(x => x.EndedAt).HasColumnName("ended_at");
        metadata.Property(x => x.SourceIp).HasColumnName("source_ip").HasColumnType("inet");
        metadata.Property(x => x.DestinationIp).HasColumnName("destination_ip").HasColumnType("inet");
        metadata.Property(x => x.SourcePort).HasColumnName("source_port");
        metadata.Property(x => x.DestinationPort).HasColumnName("destination_port");
        metadata.Property(x => x.Protocol).HasColumnName("protocol");
        metadata.Property(x => x.VlanId).HasColumnName("vlan_id");
        metadata.Property(x => x.Provenance).HasColumnName("provenance");
        metadata.Property(x => x.Revision).HasColumnName("revision");
        metadata.Property(x => x.Inferred).HasColumnName("inferred");
        metadata.Property(x => x.Partial).HasColumnName("partial");
        metadata.Property(x => x.CloseReason).HasColumnName("close_reason");
        metadata.Property(x => x.PacketCount).HasColumnName("packet_count");
        metadata.Property(x => x.ByteCount).HasColumnName("byte_count");
        var outbox = model.Entity<ProjectionOutboxEntity>();
        outbox.ToTable("projection_outbox", "monitoring");
        outbox.HasKey(x => x.Id);
        outbox.Property(x => x.Id).HasColumnName("id");
        outbox.Property(x => x.AggregateId).HasColumnName("aggregateid");
        outbox.Property(x => x.AggregateType).HasColumnName("aggregatetype");
        outbox.Property(x => x.TargetTopic).HasColumnName("target_topic");
        outbox.Property(x => x.Revision).HasColumnName("revision");
        outbox.Property(x => x.SchemaVersion).HasColumnName("schema_version");
        outbox.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        outbox.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

namespace Monitoring.Persistence.Ingestion;

internal sealed class IngestionInboxEntity
{
    public string SiteId { get; set; } = string.Empty;

    public string SensorId { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string BatchId { get; set; } = string.Empty;

    public short SchemaVersion { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string OccurredAtText { get; set; } = string.Empty;

    public string Data { get; set; } = "{}";

    public DateTimeOffset AcceptedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

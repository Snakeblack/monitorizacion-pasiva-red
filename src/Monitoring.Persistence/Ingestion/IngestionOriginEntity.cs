namespace Monitoring.Persistence.Ingestion;

internal sealed class IngestionOriginEntity
{
    public string SiteId { get; set; } = string.Empty;

    public string SensorId { get; set; } = string.Empty;
}

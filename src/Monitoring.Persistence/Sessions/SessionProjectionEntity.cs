namespace Monitoring.Persistence.Sessions;

internal sealed class SessionProjectionEntity
{
    public string SiteId { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string OccurredAtText { get; set; } = string.Empty;
    public string Data { get; set; } = "{}";
}

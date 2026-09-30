using System.Text.Json;

namespace Monitoring.Domain.Sessions;

public sealed record SessionDetail(string EventId, string SiteId, string SensorId, string OccurredAt, JsonElement Data);

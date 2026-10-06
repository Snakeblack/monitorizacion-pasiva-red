using System.Text;

namespace Monitoring.Domain.Sessions;

public sealed record SessionIdentity(string SiteId, string SensorId, string EventId)
{
    public string DocumentKey => $"{Encode(SiteId)}.{Encode(SensorId)}.{Encode(EventId)}";

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

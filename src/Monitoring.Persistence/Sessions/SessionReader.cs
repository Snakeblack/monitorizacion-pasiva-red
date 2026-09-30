using Monitoring.Domain.Sessions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Monitoring.Persistence.Sessions;

public sealed class SessionReader(MonitoringDbContext dbContext)
{
    public async Task<SessionDetail?> FindAsync(string siteId, string sensorId, string eventId, CancellationToken cancellationToken)
    {
        var session = await dbContext.Set<SessionProjectionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.SiteId == siteId && item.SensorId == sensorId && item.EventId == eventId, cancellationToken);
        if (session is null)
        {
            return null;
        }
        using var document = JsonDocument.Parse(session.Data);
        return new SessionDetail(session.EventId, session.SiteId, session.SensorId, session.OccurredAtText, document.RootElement.Clone());
    }
}

using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Sessions.Search;
using Npgsql;

namespace Monitoring.Persistence.Search;

public sealed class PostgresSessionVisibility(MonitoringDbContext dbContext) : ISessionVisibility
{
    public async Task<IReadOnlySet<Guid>> VisibleAsync(IReadOnlyCollection<Guid> searchDocumentIds, CancellationToken cancellationToken)
    {
        if (searchDocumentIds.Count == 0) return new HashSet<Guid>();
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // One indexed lookup over the unique search identity; deleted identities are exactly the suppressed ones.
            await using var command = new NpgsqlCommand("""
                SELECT search_document_id FROM monitoring.session_identity WHERE search_document_id = ANY(@ids) AND state='active'
                """, connection);
            command.Parameters.AddWithValue("ids", searchDocumentIds.ToArray());
            var visible = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) visible.Add(reader.GetGuid(0));
            return visible;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }
}

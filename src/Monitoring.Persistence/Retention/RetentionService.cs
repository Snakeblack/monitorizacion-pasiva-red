using Microsoft.EntityFrameworkCore;
using Monitoring.Persistence.Operations;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Persistence.Retention;

public sealed class RetentionOptions
{
    // The product promise: traffic data older than this is gone from every store the product controls.
    public TimeSpan SessionRetention { get; set; } = TimeSpan.FromDays(30);
    // How long a publication row that a newer revision superseded is kept for the CDC consumer; must exceed the worst tolerated lag.
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(2);
    // A tombstone must outlive the last moment a replay of its event could still be accepted (event time + SessionRetention).
    public TimeSpan TombstoneMargin { get; set; } = TimeSpan.FromDays(1);
    public int BatchSize { get; set; } = 500;
    public TimeSpan RunInterval { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (SessionRetention < TimeSpan.FromDays(1)) throw new InvalidOperationException("Retention:SessionRetention must be at least one day.");
        if (OutboxRetention < TimeSpan.FromHours(1)) throw new InvalidOperationException("Retention:OutboxRetention must be at least one hour.");
        if (TombstoneMargin < TimeSpan.Zero) throw new InvalidOperationException("Retention:TombstoneMargin must not be negative.");
        if (BatchSize is < 1 or > 5000) throw new InvalidOperationException("Retention:BatchSize must be between 1 and 5000.");
        if (RunInterval < TimeSpan.FromSeconds(1)) throw new InvalidOperationException("Retention:RunInterval must be at least one second.");
    }
}

public sealed record RetentionReport(int ExpiredSessions, int DeletedObservations, int DeletedInbox, int DeletedOutbox, int PurgedTombstones, bool OutboxSkipped);

// Enforces the retention promise in the authority. Every step works in bounded batches, each in its own transaction under the shared
// publication lock (so a search rebuild's exclusive lock pauses it), and is idempotent. Order matters: sessions become tombstones
// first, observations go before the inbox events they reference, and tombstones are purged only after their window plus margin.
public sealed class RetentionService(MonitoringDbContext dbContext, RetentionOptions options)
{
    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    public async Task<RetentionReport> RunAsync(CancellationToken cancellationToken)
    {
        options.Validate();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var expired = await LoopAsync(ExpireSessionsSql, cancellationToken);
            var observations = await LoopAsync(DeleteObservationsSql, cancellationToken);
            await LoopAsync(DeleteAssociationsSql, cancellationToken);
            var inbox = await LoopAsync(DeleteInboxSql, cancellationToken);
            var slots = (await new PipelineSnapshotReader(dbContext).ReadAsync(cancellationToken)).Slots;
            var skipped = ConsumerLagBlocksPurge(slots);
            var outbox = skipped ? 0 : await LoopAsync(DeleteSupersededOutboxSql, cancellationToken);
            var tombstones = await PurgeTombstonesAsync(cancellationToken);
            return new RetentionReport(expired, observations, inbox, outbox, tombstones, skipped);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    // A logical slot (it reports a confirmed position) with no consumer means the CDC connector is down: publication rows it has not
    // read yet must stay.
    public static bool ConsumerLagBlocksPurge(IReadOnlyList<ReplicationSlotStatus> slots) => slots.Any(slot => slot.ConfirmLagBytes is not null && !slot.Active);

    private async Task<int> LoopAsync(string sql, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var count = await BatchAsync(sql, cancellationToken);
            total += count;
            if (count < options.BatchSize) return total;
        }
    }

    private async Task<int> BatchAsync(string sql, CancellationToken cancellationToken)
    {
        await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
        await OutboxStore.AcquirePublicationLockAsync(Connection, transaction, cancellationToken);
        await using var command = new NpgsqlCommand(sql, Connection, transaction);
        command.Parameters.AddWithValue("retention", options.SessionRetention);
        command.Parameters.AddWithValue("outbox", options.OutboxRetention);
        command.Parameters.AddWithValue("batch", options.BatchSize);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private async Task<int> PurgeTombstonesAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            await OutboxStore.AcquirePublicationLockAsync(Connection, transaction, cancellationToken);
            var count = 0;
            // The identities are locked, their publication rows removed, and only then the identities: no reader sees a half purge.
            await using (var command = new NpgsqlCommand("""
                CREATE TEMP TABLE doomed_tombstones ON COMMIT DROP AS
                SELECT document_key,site_id,sensor_id,event_id FROM monitoring.session_identity
                WHERE state='deleted' AND deleted_at < clock_timestamp() - (@retention + @margin)
                ORDER BY deleted_at LIMIT @batch FOR UPDATE SKIP LOCKED
                """, Connection, transaction))
            {
                command.Parameters.AddWithValue("retention", options.SessionRetention);
                command.Parameters.AddWithValue("margin", options.TombstoneMargin);
                command.Parameters.AddWithValue("batch", options.BatchSize);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var outbox = new NpgsqlCommand("DELETE FROM monitoring.projection_outbox o USING doomed_tombstones d WHERE o.aggregateid=d.document_key", Connection, transaction))
                await outbox.ExecuteNonQueryAsync(cancellationToken);
            await using (var identities = new NpgsqlCommand("""
                DELETE FROM monitoring.session_identity i USING doomed_tombstones d
                WHERE i.site_id=d.site_id AND i.sensor_id=d.sensor_id AND i.event_id=d.event_id
                """, Connection, transaction))
                count = await identities.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            total += count;
            if (count < options.BatchSize) return total;
        }
    }

    // Sessions whose last packet is older than the window: identity -> tombstone at the next revision, traffic data removed and the
    // minimal delete barrier published, exactly what a manual suppression does (SessionSuppressor), but set-based.
    private const string ExpireSessionsSql = """
        WITH expired AS (
          SELECT i.site_id,i.sensor_id,i.event_id FROM monitoring.session_identity i
          JOIN monitoring.session_metadata m ON m.site_id=i.site_id AND m.sensor_id=i.sensor_id AND m.event_id=i.event_id
          WHERE i.state='active' AND m.ended_at < clock_timestamp() - @retention
          ORDER BY m.ended_at,i.site_id,i.sensor_id,i.event_id LIMIT @batch FOR UPDATE OF i SKIP LOCKED),
        tombstoned AS (
          UPDATE monitoring.session_identity i SET state='deleted',revision=i.revision+1,deleted_at=clock_timestamp()
          FROM expired e WHERE i.site_id=e.site_id AND i.sensor_id=e.sensor_id AND i.event_id=e.event_id
          RETURNING i.site_id,i.sensor_id,i.event_id,i.document_key,i.revision,i.search_document_id),
        removed AS (
          DELETE FROM monitoring.session_projection p USING tombstoned t
          WHERE p.site_id=t.site_id AND p.sensor_id=t.sensor_id AND p.event_id=t.event_id RETURNING 1),
        barriers AS (
          INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
          SELECT gen_random_uuid(),t.document_key,'sessions',(SELECT target_topic FROM monitoring.search_generation WHERE state='active'),t.revision,2,
            jsonb_build_object('schemaVersion',2,'operation','delete','documentKey',t.document_key,'searchDocumentId',t.search_document_id,
              'revision',t.revision,'siteId',t.site_id,'sensorId',t.sensor_id,'eventId',t.event_id)
          FROM tombstoned t ON CONFLICT DO NOTHING RETURNING 1)
        SELECT (SELECT count(*) FROM tombstoned)
        """;

    private const string DeleteObservationsSql = """
        WITH doomed AS (SELECT site_id,sensor_id,event_id FROM monitoring.device_observation
                        WHERE observed_at < clock_timestamp() - @retention LIMIT @batch),
        gone AS (DELETE FROM monitoring.device_observation o USING doomed d
                 WHERE o.site_id=d.site_id AND o.sensor_id=d.sensor_id AND o.event_id=d.event_id RETURNING 1)
        SELECT count(*) FROM gone
        """;

    private const string DeleteAssociationsSql = """
        WITH doomed AS (SELECT candidate_id,ip FROM monitoring.device_ip_association WHERE last_seen < clock_timestamp() - @retention LIMIT @batch),
        gone AS (DELETE FROM monitoring.device_ip_association a USING doomed d WHERE a.candidate_id=d.candidate_id AND a.ip=d.ip RETURNING 1)
        SELECT count(*) FROM gone
        """;

    // Processed events past the window. Pending events are never touched; quarantined ones (and anything their audit trail or an
    // observation still references) stay until those references are resolved.
    private const string DeleteInboxSql = """
        WITH doomed AS (
          SELECT i.site_id,i.sensor_id,i.event_id FROM monitoring.ingestion_inbox i
          WHERE i.processed_at IS NOT NULL AND i.occurred_at < clock_timestamp() - @retention
            AND NOT EXISTS (SELECT 1 FROM monitoring.ingestion_quarantine q WHERE q.site_id=i.site_id AND q.sensor_id=i.sensor_id AND q.event_id=i.event_id)
            AND NOT EXISTS (SELECT 1 FROM monitoring.ingestion_quarantine_audit a WHERE a.site_id=i.site_id AND a.sensor_id=i.sensor_id AND a.event_id=i.event_id)
            AND NOT EXISTS (SELECT 1 FROM monitoring.device_observation o WHERE o.site_id=i.site_id AND o.sensor_id=i.sensor_id AND o.event_id=i.event_id)
          ORDER BY i.occurred_at LIMIT @batch FOR UPDATE OF i SKIP LOCKED),
        gone AS (DELETE FROM monitoring.ingestion_inbox i USING doomed d
                 WHERE i.site_id=d.site_id AND i.sensor_id=d.sensor_id AND i.event_id=d.event_id RETURNING 1)
        SELECT count(*) FROM gone
        """;

    // Only rows a newer revision of the same identity and topic supersedes; the latest row of every identity (what a rebuild copies
    // and what the reconciler verifies) always stays.
    private const string DeleteSupersededOutboxSql = """
        WITH doomed AS (
          SELECT o.id FROM monitoring.projection_outbox o
          WHERE o.created_at < clock_timestamp() - @outbox
            AND EXISTS (SELECT 1 FROM monitoring.projection_outbox n WHERE n.aggregateid=o.aggregateid AND n.target_topic=o.target_topic AND n.revision>o.revision)
          LIMIT @batch),
        gone AS (DELETE FROM monitoring.projection_outbox o USING doomed d WHERE o.id=d.id RETURNING 1)
        SELECT count(*) FROM gone
        """;
}

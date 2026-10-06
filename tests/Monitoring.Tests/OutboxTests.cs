using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Monitoring.Persistence;
using Monitoring.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Monitoring.Tests;

public sealed class OutboxTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ProjectionPublishesOneCanonicalRecordAcrossConcurrentWorkersAndReplay()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var site in new[] { "site-a", "site-b" })
            await SessionTestDatabase.AcceptAsync(connection, "event", site, "sensor", CanonicalSessionTests.Captured);
        await Task.WhenAll(SessionTestDatabase.ProjectAsync(connection), SessionTestDatabase.ProjectAsync(connection));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata WHERE source_ip='2001:db8::1'::inet AND provenance='capture' AND packet_count=12"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE revision=1 AND schema_version=2 AND target_topic='monitoring.sessions.v2' AND payload->>'operation'='upsert' AND payload->>'sourceIp'='2001:db8::1'"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'startedAt'='2026-09-29T12:00:00.100Z' AND payload->>'endedAt'='2026-09-29T12:00:01.000Z'"));
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at=NULL");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE revision=1 AND state='active'"));
    }

    [Fact]
    public async Task MarkingFailureRollsBackSessionMetadataIdentityAndOutboxThenRestartRecovers()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, "P0001");
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ProjectAsync(connection));
        // Independent connections observe the committed state after rollback.
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        await SessionTestDatabase.ExecuteAsync(connection, "DROP TRIGGER fail_mark ON monitoring.ingestion_inbox");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task SuppressedIdentityCannotBeRepublishedByAnOldPendingEvent()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        await SessionTestDatabase.ExecuteAsync(connection, """
            UPDATE monitoring.session_identity SET revision=2,state='deleted',deleted_at=now();
            DELETE FROM monitoring.session_projection;
            UPDATE monitoring.ingestion_inbox SET processed_at=NULL;
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SessionTestDatabase.ProjectAsync(connection));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE revision=2 AND state='deleted'"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE revision=1"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
    }

    [Fact]
    public async Task UpgradeBackfillsValidHistoricalSessionsOnceWithoutChangingFiveColumnProjection()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        await using (var db = SessionTestDatabase.Context(connection))
        {
            foreach (var migration in new Migration[] { new InitialSchema(), new DurableInbox(), new SessionProjection() })
                foreach (var sql in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
                    await SessionTestDatabase.ExecuteAsync(connection, sql.CommandText);
        }
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY,"ProductVersion" varchar(32) NOT NULL);
            INSERT INTO public."__EFMigrationsHistory" VALUES
              ('202609240001_InitialSchema','10.0.12'),('202609240002_DurableInbox','10.0.12'),('202609290003_SessionProjection','10.0.12');
            """);
        await SessionTestDatabase.AcceptAsync(connection, "historical");
        await SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.session_projection SELECT site_id,sensor_id,event_id,occurred_at_text,data FROM monitoring.ingestion_inbox;
            UPDATE monitoring.ingestion_inbox SET processed_at=now();
            """);
        Assert.Equal(0, (await MigrationProcess.RunAsync(connection)).ExitCode);
        Assert.Equal(0, (await MigrationProcess.RunAsync(connection)).ExitCode);
        Assert.Equal(5L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema='monitoring' AND table_name='session_projection'"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata WHERE provenance='synthetic' AND packet_count IS NULL AND inferred IS NULL"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'operation'='upsert'"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection s JOIN monitoring.ingestion_inbox i USING(site_id,sensor_id,event_id) WHERE s.data=i.data AND s.occurred_at_text=i.occurred_at_text"));
    }

    [Fact]
    public async Task MaximumLengthIdentifiersPublishACompactPersistentSearchIdentityBesideTheFullDocumentKey()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var (site, sensor, eventId) = (new string('s', 128), new string('n', 128), new string('e', 128));
        await SessionTestDatabase.AcceptAsync(connection, eventId, site, sensor);
        await SessionTestDatabase.ProjectAsync(connection);
        // Three 128-byte identifiers encode to 515 bytes: over the 512-byte Elasticsearch _id limit, so it cannot be the index key.
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE octet_length(document_key)=515"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.session_identity i JOIN monitoring.projection_outbox o ON o.aggregateid=i.document_key
            WHERE o.payload->>'searchDocumentId'=i.search_document_id::text AND octet_length(o.payload->>'searchDocumentId')=36
              AND o.payload->>'documentKey'=i.document_key AND o.payload->>'siteId'=i.site_id AND o.payload->>'eventId'=i.event_id
            """));
        var assigned = await SessionTestDatabase.TextAsync(connection, "SELECT search_document_id::text FROM monitoring.session_identity");
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at=NULL");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(assigned, await SessionTestDatabase.TextAsync(connection, "SELECT search_document_id::text FROM monitoring.session_identity"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task SearchIdentityIsUniquePerIdentityAndImmutable()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var site in new[] { "site-a", "site-b" })
            await SessionTestDatabase.AcceptAsync(connection, "event", site, "sensor");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(DISTINCT search_document_id) FROM monitoring.session_identity"));
        var tamper = await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection,
            "UPDATE monitoring.session_identity SET search_document_id=gen_random_uuid() WHERE site_id='site-a'"));
        Assert.Equal(PostgresErrorCodes.IntegrityConstraintViolation, tamper.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.session_identity(site_id,sensor_id,event_id,document_key,revision,state,search_document_id)
            SELECT 'site-c','sensor','event','c2l0ZS1j.c2Vuc29y.ZXZlbnQ',1,'active',search_document_id
            FROM monitoring.session_identity WHERE site_id='site-a'
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Fact]
    public async Task UpgradeFromLegacyOutboxAssignsStableSearchIdentitiesAndRepublishesOnlyOnTheNewTopic()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        await using (var db = SessionTestDatabase.Context(connection))
        {
            foreach (var migration in new Migration[] { new InitialSchema(), new DurableInbox(), new SessionProjection(), new CanonicalOutbox() })
                foreach (var sql in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
                    await SessionTestDatabase.ExecuteAsync(connection, sql.CommandText);
        }
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY,"ProductVersion" varchar(32) NOT NULL);
            INSERT INTO public."__EFMigrationsHistory" VALUES
              ('202609240001_InitialSchema','10.0.12'),('202609240002_DurableInbox','10.0.12'),
              ('202609290003_SessionProjection','10.0.12'),('202610050004_CanonicalOutbox','10.0.12');
            """);
        foreach (var eventId in new[] { "legacy-active", "legacy-deleted" })
            await SessionTestDatabase.AcceptAsync(connection, eventId);
        // State a deployment of migration 0004 leaves behind: v1 publication on the old topic, keyed by the full document key.
        await SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.session_projection SELECT site_id,sensor_id,event_id,occurred_at_text,data FROM monitoring.ingestion_inbox;
            UPDATE monitoring.ingestion_inbox SET processed_at=now();
            INSERT INTO monitoring.session_identity(site_id,sensor_id,event_id,document_key,revision,state,deleted_at) VALUES
              ('site','sensor','legacy-active','c2l0ZQ.c2Vuc29y.bGVnYWN5LWFjdGl2ZQ',1,'active',NULL),
              ('site','sensor','legacy-deleted','c2l0ZQ.c2Vuc29y.bGVnYWN5LWRlbGV0ZWQ',2,'deleted',now());
            INSERT INTO monitoring.session_metadata(site_id,sensor_id,event_id,started_at,ended_at,source_ip,destination_ip,source_port,
              destination_port,protocol,provenance,revision)
              VALUES ('site','sensor','legacy-active','2026-09-29T12:00:00Z','2026-09-29T12:00:00.123Z','192.0.2.1','2001:db8::2',0,65535,'TCP','synthetic',1);
            INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload) VALUES
              (gen_random_uuid(),'c2l0ZQ.c2Vuc29y.bGVnYWN5LWFjdGl2ZQ','sessions','monitoring.sessions.v1',1,1,'{"schemaVersion":1,"operation":"upsert"}'),
              (gen_random_uuid(),'c2l0ZQ.c2Vuc29y.bGVnYWN5LWRlbGV0ZWQ','sessions','monitoring.sessions.v1',2,1,'{"schemaVersion":1,"operation":"delete"}');
            DELETE FROM monitoring.session_projection WHERE event_id='legacy-deleted';
            """);
        Assert.Equal(0, (await MigrationProcess.RunAsync(connection)).ExitCode);
        var assigned = await SessionTestDatabase.TextAsync(connection,
            "SELECT string_agg(event_id||'='||search_document_id,',' ORDER BY event_id) FROM monitoring.session_identity");
        Assert.Equal(0, (await MigrationProcess.RunAsync(connection)).ExitCode);
        Assert.Equal(assigned, await SessionTestDatabase.TextAsync(connection,
            "SELECT string_agg(event_id||'='||search_document_id,',' ORDER BY event_id) FROM monitoring.session_identity"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(DISTINCT search_document_id) FROM monitoring.session_identity"));
        // Legacy history is append-only: untouched, never rewritten.
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.projection_outbox WHERE target_topic='monitoring.sessions.v1' AND schema_version=1 AND NOT payload ? 'searchDocumentId'"));
        // Each current identity is republished exactly once at its current revision for the v2 contract.
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.projection_outbox o JOIN monitoring.session_identity i ON i.document_key=o.aggregateid
            WHERE o.target_topic='monitoring.sessions.v2' AND o.schema_version=2 AND o.revision=1 AND i.state='active'
              AND o.payload->>'operation'='upsert' AND o.payload->>'searchDocumentId'=i.search_document_id::text
            """));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.projection_outbox o JOIN monitoring.session_identity i ON i.document_key=o.aggregateid
            WHERE o.target_topic='monitoring.sessions.v2' AND o.schema_version=2 AND o.revision=2 AND i.state='deleted'
              AND o.payload->>'operation'='delete' AND o.payload->>'searchDocumentId'=i.search_document_id::text
              AND NOT o.payload ? 'sourceIp' AND NOT o.payload ? 'startedAt'
            """));
        Assert.Equal(4L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }
}

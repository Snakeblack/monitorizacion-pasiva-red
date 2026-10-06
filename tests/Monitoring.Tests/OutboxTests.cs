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
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE revision=1 AND schema_version=1 AND payload->>'operation'='upsert' AND payload->>'sourceIp'='2001:db8::1'"));
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
}

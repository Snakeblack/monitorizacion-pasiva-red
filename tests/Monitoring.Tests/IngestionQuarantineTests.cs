using Monitoring.Domain.Sessions;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class IngestionQuarantineTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Incompatible = "{\"arbitrary\":true,\"secret\":\"payload-must-not-leak\"}";

    private async Task<string> DatabaseAsync() => await SessionTestDatabase.CreateAsync(postgres);

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task APermanentlyIncompatibleEventIsIsolatedBetweenValidOnesWithoutFalseProcessing()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "valid-1");
        await SessionTestDatabase.AcceptAsync(connection, "incompatible", json: Incompatible);
        await SessionTestDatabase.AcceptAsync(connection, "valid-2");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(1L, await Count(connection, """
            SELECT count(*) FROM monitoring.ingestion_quarantine q JOIN monitoring.ingestion_inbox i USING(site_id,sensor_id,event_id)
            WHERE q.event_id='incompatible' AND q.cause='contract-invalid' AND q.state='unresolved' AND q.attempts=1
              AND i.processed_at IS NULL AND i.quarantined_at IS NOT NULL
            """));
        // The state change is accounted exactly once, with a system actor and the minimal cause code.
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit WHERE event_id='incompatible' AND action='quarantined' AND actor='system:projector' AND cause='contract-invalid'"));
        // The reconcilable balance: every accepted event is processed, quarantined or still pending.
        Assert.Equal(3L, await Count(connection, """
            SELECT count(*) FILTER (WHERE processed_at IS NOT NULL) + count(*) FILTER (WHERE quarantined_at IS NOT NULL)
                 + count(*) FILTER (WHERE processed_at IS NULL AND quarantined_at IS NULL) FROM monitoring.ingestion_inbox
            """));
    }

    [Fact]
    public async Task RepeatedProjectionPassesNeitherReattemptNorRecountAQuarantinedEvent()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "incompatible", json: Incompatible);
        for (var pass = 0; pass < 3; pass++) await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await Count(connection, "SELECT attempts::bigint FROM monitoring.ingestion_quarantine"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit"));
    }

    [Fact]
    public async Task ASuppressedIdentityReplayedByAnOldEventIsQuarantinedAndTheWorkerKeepsGoing()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        await using (var db = SessionTestDatabase.Context(connection))
            await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "event"), CancellationToken.None);
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at=NULL");
        await SessionTestDatabase.AcceptAsync(connection, "later-valid");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection WHERE event_id='event'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE event_id='event' AND cause='identity-suppressed' AND state='unresolved'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection WHERE event_id='later-valid'"));
        // The suppression is untouched and no extra publication was made for the replayed identity.
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.session_identity WHERE event_id='event' AND revision=2 AND state='deleted'"));
        Assert.Equal(3L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task AProjectionThatConflictsWithItsAcceptedEventIsQuarantinedNotFatal()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "conflicting");
        await SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.session_projection(site_id,sensor_id,event_id,occurred_at_text,data)
            VALUES ('site','sensor','conflicting','2026-09-29T12:00:00.100Z','{"someone":"else"}')
            """);
        await SessionTestDatabase.AcceptAsync(connection, "valid");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE event_id='conflicting' AND cause='projection-conflict'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection WHERE event_id='conflicting' AND data='{\"someone\":\"else\"}'::jsonb"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'eventId'='valid'"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE payload->>'eventId'='conflicting'"));
    }

    [Fact]
    public async Task ATransientFailureNeverQuarantinesAndTheEventStaysPending()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, "P0001");
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ProjectAsync(connection));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL AND quarantined_at IS NULL"));
    }

    [Fact]
    public async Task TheOriginalEventIsImmutableAndAQuarantinedOneCannotBeDeleted()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "incompatible", json: Incompatible);
        await SessionTestDatabase.ProjectAsync(connection);
        foreach (var statement in new[]
        {
            "UPDATE monitoring.ingestion_inbox SET data='{}'::jsonb",
            "UPDATE monitoring.ingestion_inbox SET occurred_at_text='2020-01-01T00:00:00Z'",
            "UPDATE monitoring.ingestion_inbox SET batch_id='other'"
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, statement));
            Assert.Equal(PostgresErrorCodes.IntegrityConstraintViolation, failure.SqlState);
        }
        var delete = await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, "DELETE FROM monitoring.ingestion_inbox"));
        Assert.Contains(delete.SqlState, new[] { PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation });
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_inbox"));
    }

    [Fact]
    public async Task NoPayloadIsCopiedIntoTheQuarantineRecordOrItsAudit()
    {
        var connection = await DatabaseAsync();
        await SessionTestDatabase.AcceptAsync(connection, "incompatible", json: Incompatible);
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine q WHERE q::text LIKE '%payload-must-not-leak%'"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine_audit a WHERE a::text LIKE '%payload-must-not-leak%'"));
    }
}

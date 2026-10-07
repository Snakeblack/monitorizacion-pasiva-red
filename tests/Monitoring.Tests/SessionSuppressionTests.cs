using Monitoring.Domain.Sessions;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class SessionSuppressionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SessionIdentity Identity = new("site", "sensor", "event");

    private static async Task<SuppressionResult> SuppressAsync(string connection, SessionIdentity identity)
    {
        await using var db = SessionTestDatabase.Context(connection);
        return await new SessionSuppressor(db).SuppressAsync(identity, CancellationToken.None);
    }

    private async Task<string> ProjectedAsync()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        return connection;
    }

    [Fact]
    public async Task SuppressionRemovesTrafficDataAndPublishesAMinimalBarrierAtTheNextRevisionAtomically()
    {
        var connection = await ProjectedAsync();
        Assert.Equal(SuppressionResult.Suppressed, await SuppressAsync(connection, Identity));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.session_identity WHERE revision=2 AND state='deleted' AND deleted_at IS NOT NULL"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.projection_outbox o JOIN monitoring.session_identity i ON i.document_key=o.aggregateid
            WHERE o.revision=2 AND o.schema_version=2 AND o.target_topic='monitoring.sessions.v2' AND o.payload->>'operation'='delete'
              AND o.payload->>'searchDocumentId'=i.search_document_id::text AND (o.payload->>'revision')::bigint=2
              AND NOT o.payload ? 'sourceIp' AND NOT o.payload ? 'startedAt' AND NOT o.payload ? 'acceptedAt'
            """));
        // The earlier upsert stays as append-only history.
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task RepeatedOrUnknownSuppressionIsIdempotent()
    {
        var connection = await ProjectedAsync();
        Assert.Equal(SuppressionResult.NotFound, await SuppressAsync(connection, new SessionIdentity("site", "sensor", "unknown")));
        Assert.Equal(SuppressionResult.Suppressed, await SuppressAsync(connection, Identity));
        Assert.Equal(SuppressionResult.AlreadySuppressed, await SuppressAsync(connection, Identity));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE revision=2"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE event_id='unknown'"));
    }

    [Fact]
    public async Task BarrierFailureRollsBackIdentitySessionAndMetadata()
    {
        var connection = await ProjectedAsync();
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE FUNCTION monitoring.fail_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'sensitive-test-payload' USING ERRCODE = 'P0001'; END $$;
            CREATE TRIGGER fail_outbox BEFORE INSERT ON monitoring.projection_outbox
            FOR EACH ROW EXECUTE FUNCTION monitoring.fail_outbox();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => SuppressAsync(connection, Identity));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE revision=1 AND state='active' AND deleted_at IS NULL"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_metadata"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
    }

    [Fact]
    public async Task ConcurrentSuppressionsPublishOneBarrier()
    {
        var connection = await ProjectedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SuppressAsync(connection, Identity)));
        Assert.Equal(1, results.Count(result => result == SuppressionResult.Suppressed));
        Assert.Equal(3, results.Count(result => result == SuppressionResult.AlreadySuppressed));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox WHERE revision=2"));
    }

    [Fact]
    public async Task ASuppressedIdentityIsNotResurrectedByAReplayedAcceptedEvent()
    {
        var connection = await ProjectedAsync();
        Assert.Equal(SuppressionResult.Suppressed, await SuppressAsync(connection, Identity));
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at=NULL");
        // The replay is a permanent incompatibility: quarantined, never a crash, never a republication.
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE cause='identity-suppressed' AND state='unresolved'"));
    }
}

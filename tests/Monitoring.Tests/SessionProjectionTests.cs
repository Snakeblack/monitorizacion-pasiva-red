using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Ingestion;
using Monitoring.Persistence;
using Monitoring.Persistence.Ingestion;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class SessionProjectionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task InitialProjectionAndEqualReplayPreserveOneSessionPerOrigin()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var (site, sensor) in new[] { ("site-a", "sensor-a"), ("site-b", "sensor-a"), ("site-a", "sensor-b") })
        {
            await SessionTestDatabase.AcceptAsync(connection, "shared", site, sensor);
        }
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(3L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(3L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
        Assert.Equal(3L, await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM monitoring.session_projection s JOIN monitoring.ingestion_inbox i USING (site_id,sensor_id,event_id)
            WHERE s.data = i.data AND s.occurred_at_text = i.occurred_at_text
            """));
        // Simulate recovery/replay with an existing equal projection, never replacement.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at = NULL");
        await SessionTestDatabase.ProjectAsync(connection);
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(3L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(3L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task ConflictingProjectionRaisesInvariantWithoutOverwritingOrMarking()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ExecuteAsync(connection, """
            INSERT INTO monitoring.session_projection VALUES ('site','sensor','event','different','{"conflicting":true}')
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SessionTestDatabase.ProjectAsync(connection));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            "SELECT count(*) FROM monitoring.session_projection WHERE occurred_at_text='different' AND data='{\"conflicting\":true}'::jsonb"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
    }

    [Fact]
    public async Task MarkingFailureRollsBackInsertAndRestartRecoversPending()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, "P0001");
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ProjectAsync(connection));
        // Each query opens an independent connection after the failed transaction was disposed.
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
        await SessionTestDatabase.ExecuteAsync(connection, "DROP TRIGGER fail_mark ON monitoring.ingestion_inbox");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task MoreThanOnePageOfInvalidUnknownAndValidEventsDoesNotStarveLaterValidEvents()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        for (var index = 0; index < 105; index++)
        {
            await SessionTestDatabase.AcceptAsync(connection, $"invalid-{index:D3}", json: index % 2 == 0
                ? "{\"kind\":\"unknown\"}" : SyntheticSessionContractTests.ValidData.Replace("65535", "65536", StringComparison.Ordinal));
        }
        for (var index = 0; index < 103; index++)
        {
            await SessionTestDatabase.AcceptAsync(connection, $"valid-{index:D3}");
        }
        // Equal timestamps force PostgreSQL's complete composite ordering across page boundaries.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET accepted_at=TIMESTAMPTZ '2026-09-29 12:00:00Z'");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(103L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(105L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
    }

    [Fact]
    public async Task ConcurrentWorkersAndSkippedLockedEventRecoverOnNextPass()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "locked");
        for (var index = 0; index < 8; index++)
        {
            await SessionTestDatabase.AcceptAsync(connection, $"event-{index}");
        }
        await using var lockConnection = new NpgsqlConnection(connection);
        await lockConnection.OpenAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT 1 FROM monitoring.ingestion_inbox WHERE event_id='locked' FOR UPDATE", lockConnection, transaction))
        {
            await command.ExecuteScalarAsync();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Task.WhenAll(SessionTestDatabase.ProjectAsync(connection, timeout.Token), SessionTestDatabase.ProjectAsync(connection, timeout.Token));
        Assert.Equal(8L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        await transaction.CommitAsync();
        await SessionTestDatabase.ProjectAsync(connection, timeout.Token);
        Assert.Equal(9L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(9L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }
}

internal static class SessionTestDatabase
{
    internal static MonitoringDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connection).Options);

    internal static async Task<string> CreateAsync(PostgresFixture postgres)
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var migration = await MigrationProcess.RunAsync(connection);
        Assert.Equal(0, migration.ExitCode);
        return connection;
    }

    internal static async Task AcceptAsync(string connection, string eventId, string site = "site", string sensor = "sensor", string? json = null)
    {
        using var document = JsonDocument.Parse(json ?? SyntheticSessionContractTests.ValidData);
        await using var db = Context(connection);
        var batch = new IngestionBatch(1, "batch", site, sensor,
            [new IngestionEvent(eventId, "2026-09-29T12:00:00.100Z", document.RootElement.Clone())]);
        Assert.Equal(InboxWriteResult.Accepted, await new InboxWriter(db).WriteAsync(site, sensor, batch, CancellationToken.None));
    }

    internal static async Task ProjectAsync(string connection, CancellationToken cancellationToken = default)
    {
        await using var db = Context(connection);
        await new SessionProjector(db).RunPassAsync(cancellationToken);
    }

    internal static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static Task InstallFailureTriggerAsync(string connection, string sqlState) => ExecuteAsync(connection, $$"""
        CREATE FUNCTION monitoring.fail_mark() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'sensitive-test-payload' USING ERRCODE = '{{sqlState}}'; END $$;
        CREATE TRIGGER fail_mark BEFORE UPDATE OF processed_at ON monitoring.ingestion_inbox
        FOR EACH ROW EXECUTE FUNCTION monitoring.fail_mark();
        """);
}

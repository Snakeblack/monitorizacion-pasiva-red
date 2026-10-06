using Monitoring.Domain.Sessions;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence.Sessions;
using Monitoring.Persistence.Search;

namespace Monitoring.Tests;

public sealed class SearchAuthorityTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<string> ProjectedAsync(params string[] events)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        foreach (var eventId in events) await SessionTestDatabase.AcceptAsync(connection, eventId);
        await SessionTestDatabase.ProjectAsync(connection);
        return connection;
    }

    private static async Task<List<Guid>> IdsAsync(string connection)
    {
        await using var db = SessionTestDatabase.Context(connection);
        await using var npgsql = new Npgsql.NpgsqlConnection(connection);
        await npgsql.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT search_document_id FROM monitoring.session_identity ORDER BY event_id", npgsql);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    [Fact]
    public async Task OnlyActiveIdentitiesAreVisibleAndSuppressedOrUnknownOnesAreFiltered()
    {
        var connection = await ProjectedAsync("a", "b", "c");
        var ids = await IdsAsync(connection);
        await using (var db = SessionTestDatabase.Context(connection))
            Assert.Equal(SuppressionResult.Suppressed, await new SessionSuppressor(db).SuppressAsync(new SessionIdentity("site", "sensor", "b"), CancellationToken.None));
        await using var read = SessionTestDatabase.Context(connection);
        var visible = await new PostgresSessionVisibility(read).VisibleAsync([ids[0], ids[1], ids[2], Guid.NewGuid()], CancellationToken.None);
        Assert.Equal(new[] { ids[0], ids[2] }.Order(), visible.Order());
        Assert.Empty(await new PostgresSessionVisibility(read).VisibleAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task LeasesBoundSnapshotsPerSubjectAndGloballyAndFreeSlotsOnReleaseOrExpiry()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var options = new SnapshotLeaseOptions { MaxPerSubject = 2, MaxGlobal = 3 };
        async Task<Guid> Acquire(string subject, DateTimeOffset expires, DateTimeOffset? now = null)
        {
            await using var db = SessionTestDatabase.Context(connection);
            return await new PostgresSnapshotLeases(db, options).AcquireAsync(subject, expires, now ?? Now, CancellationToken.None);
        }
        var first = await Acquire("alice", Now.AddMinutes(10));
        await Acquire("alice", Now.AddMinutes(10));
        var saturated = await Assert.ThrowsAsync<SessionSearchException>(() => Acquire("alice", Now.AddMinutes(10)));
        Assert.Equal(SessionSearchFailure.Saturated, saturated.Failure);
        await Acquire("bob", Now.AddMinutes(10));
        Assert.Equal(SessionSearchFailure.Saturated, (await Assert.ThrowsAsync<SessionSearchException>(() => Acquire("carol", Now.AddMinutes(10)))).Failure);
        await using (var db = SessionTestDatabase.Context(connection))
            await new PostgresSnapshotLeases(db, options).ReleaseAsync(first, CancellationToken.None);
        await Acquire("carol", Now.AddMinutes(10));
        // Expired leases never count against the limits, even if nobody released them.
        await Acquire("dave", Now.AddMinutes(30), Now.AddMinutes(20));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection,
            $"SELECT count(*) FROM monitoring.search_snapshot_lease WHERE subject='dave'"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.search_snapshot_lease WHERE subject IN ('alice','bob','carol')"));
    }

    [Fact]
    public async Task ConcurrentAcquisitionsNeverExceedTheLimit()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var options = new SnapshotLeaseOptions { MaxPerSubject = 2, MaxGlobal = 20 };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = SessionTestDatabase.Context(connection);
            try { await new PostgresSnapshotLeases(db, options).AcquireAsync("alice", Now.AddMinutes(10), Now, CancellationToken.None); return true; }
            catch (SessionSearchException) { return false; }
        }));
        Assert.Equal(2, results.Count(granted => granted));
        Assert.Equal(2L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.search_snapshot_lease"));
    }

    [Fact]
    public async Task RecordingPitChangesIsAuditedAndRejectsLostOrExpiredLeases()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var options = new SnapshotLeaseOptions();
        Guid lease;
        await using (var db = SessionTestDatabase.Context(connection))
            lease = await new PostgresSnapshotLeases(db, options).AcquireAsync("alice", Now.AddMinutes(10), Now, CancellationToken.None);
        async Task<bool> Record(string pit, DateTimeOffset now, Guid? id = null)
        {
            await using var db = SessionTestDatabase.Context(connection);
            return await new PostgresSnapshotLeases(db, options).RecordPitAsync(id ?? lease, pit, now, CancellationToken.None);
        }
        Assert.True(await Record("pit-1", Now));
        Assert.True(await Record("pit-1", Now));
        Assert.True(await Record("pit-2", Now));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT pit_changes::bigint FROM monitoring.search_snapshot_lease"));
        // Only a digest of the PIT id is stored, never the id itself.
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.search_snapshot_lease WHERE octet_length(pit_hash)=32"));
        Assert.False(await Record("pit-3", Now.AddMinutes(10)));
        Assert.False(await Record("pit-3", Now, Guid.NewGuid()));
    }
}

using Monitoring.Domain.Inventory;
using Monitoring.Domain.Sessions.Search;
using Monitoring.Persistence.Inventory;
using Npgsql;

namespace Monitoring.Tests;

public sealed class InventoryServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly AuthorizedPair ScopeA = new("site", "sensor");
    private static readonly InventoryActor Admin = new("oidc|admin-1", [ScopeA, new("site", "sensor-2")]);
    private static readonly InventoryActor OnlyA = new("oidc|admin-2", [ScopeA]);

    // Candidates: X (site/sensor vlan 42), Y (site/sensor-2 vlan 42, other scope), Z (site/sensor, untagged).
    private async Task<(string Connection, Guid X, Guid Y, Guid Z)> SeedAsync()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "x", "site", "sensor", DeviceObservationContractTests.Observation("2026-10-06T10:00:00Z", "192.0.2.10"));
        await SessionTestDatabase.AcceptAsync(connection, "y", "site", "sensor-2", DeviceObservationContractTests.Observation("2026-10-06T10:01:00Z", "192.0.2.10"));
        await SessionTestDatabase.AcceptAsync(connection, "z", "site", "sensor", DeviceObservationContractTests.Observation("2026-10-06T10:02:00Z", "192.0.2.20", vlan: null));
        await SessionTestDatabase.ProjectAsync(connection);
        Guid Id(string sensor, bool untagged) => Guid.Parse(SessionTestDatabase.TextAsync(connection,
            $"SELECT candidate_id::text FROM monitoring.device_candidate WHERE sensor_id='{sensor}' AND vlan_id IS {(untagged ? "" : "NOT ")}NULL").Result);
        return (connection, Id("sensor", false), Id("sensor-2", false), Id("sensor", true));
    }

    private static async Task<T> WithAsync<T>(string connection, Func<InventoryService, Task<T>> action)
    {
        await using var db = SessionTestDatabase.Context(connection);
        return await action(new InventoryService(db));
    }

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    [Fact]
    public async Task ADeviceIsCreatedWithAStableIdRevisionOneScopeAndAnAuditRecord()
    {
        var (connection, _, _, _) = await SeedAsync();
        var created = await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Core switch", "Rack 3", ScopeA, CancellationToken.None));
        Assert.Equal(InventoryStatus.Ok, created.Status);
        var device = created.Value!;
        Assert.Equal(("Core switch", "Rack 3", 1L), (device.Name, device.Description, device.Revision));
        Assert.Equal([ScopeA], device.Scopes);
        Assert.Equal(1L, await Count(connection, $"""
            SELECT count(*) FROM monitoring.inventory_audit WHERE action='device-created' AND actor='oidc|admin-1'
              AND device_id='{device.DeviceId}' AND device_revision_after=1 AND device_revision_before IS NULL
            """));
    }

    [Theory]
    [InlineData("", "d")]
    [InlineData("   ", "d")]
    [InlineData("n", "x")]
    public async Task InvalidNamesAndDescriptionsAreRejectedWithoutChanges(string name, string description)
    {
        var (connection, _, _, _) = await SeedAsync();
        var tooLong = description == "x" ? new string('x', 1025) : description;
        var result = await WithAsync(connection, s => s.CreateDeviceAsync(Admin, name, tooLong, ScopeA, CancellationToken.None));
        Assert.Equal(InventoryStatus.Invalid, result.Status);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.inventory_audit"));
        // 128 characters and 1024 characters are the maximum accepted.
        Assert.Equal(InventoryStatus.Ok, (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, new string('n', 128), new string('d', 1024), ScopeA, CancellationToken.None))).Status);
        Assert.Equal(InventoryStatus.Invalid, (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, new string('n', 129), "d", ScopeA, CancellationToken.None))).Status);
    }

    [Fact]
    public async Task ACreationOutsideTheActorsScopesIsForbidden()
    {
        var (connection, _, _, _) = await SeedAsync();
        var result = await WithAsync(connection, s => s.CreateDeviceAsync(OnlyA, "Other", "d", new AuthorizedPair("site", "sensor-2"), CancellationToken.None));
        Assert.Equal(InventoryStatus.Forbidden, result.Status);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
    }

    [Fact]
    public async Task ACorrectionNeedsTheCurrentRevisionAndAStaleOneIsAConflictWithNoChange()
    {
        var (connection, _, _, _) = await SeedAsync();
        var device = (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Old", "d", ScopeA, CancellationToken.None))).Value!;
        var fixedUp = await WithAsync(connection, s => s.UpdateDeviceAsync(Admin, device.DeviceId, 1, "Corrected", "d2", CancellationToken.None));
        Assert.Equal((InventoryStatus.Ok, 2L, "Corrected"), (fixedUp.Status, fixedUp.Value!.Revision, fixedUp.Value.Name));
        var stale = await WithAsync(connection, s => s.UpdateDeviceAsync(Admin, device.DeviceId, 1, "Stale write", "d3", CancellationToken.None));
        Assert.Equal(InventoryStatus.Conflict, stale.Status);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device WHERE name='Corrected' AND revision=2"));
        Assert.Equal(InventoryStatus.NotFound, (await WithAsync(connection, s => s.UpdateDeviceAsync(Admin, Guid.NewGuid(), 1, "x", "y", CancellationToken.None))).Status);
        // The audit keeps only the minimal change: the field names and old/new values of what actually changed.
        Assert.Equal(1L, await Count(connection, $"""
            SELECT count(*) FROM monitoring.inventory_audit WHERE action='device-updated' AND device_id='{device.DeviceId}'
              AND device_revision_before=1 AND device_revision_after=2 AND changes->'name'->>'from'='Old' AND changes->'name'->>'to'='Corrected'
            """));
    }

    [Fact]
    public async Task ConfirmingACandidateCreatesTheDeviceLinksItAndKeepsEveryObservation()
    {
        var (connection, x, _, _) = await SeedAsync();
        var observationsBefore = await Count(connection, "SELECT count(*) FROM monitoring.device_observation");
        var result = await WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "Printer 2F", "Floor 2", CancellationToken.None));
        Assert.Equal(InventoryStatus.Ok, result.Status);
        var candidate = result.Value!;
        Assert.Equal(("confirmed", 2L), (candidate.State, candidate.Revision));
        Assert.NotNull(candidate.DeviceId);
        Assert.Equal(observationsBefore, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.device d JOIN monitoring.device_scope s USING(device_id) WHERE d.device_id='{candidate.DeviceId}' AND s.site_id='site' AND s.sensor_id='sensor' AND d.name='Printer 2F'"));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.inventory_audit WHERE action='candidate-confirmed' AND candidate_id='{x}' AND candidate_revision_before=1 AND candidate_revision_after=2"));
        // A second decision on the same (now stale) revision is a conflict.
        Assert.Equal(InventoryStatus.Conflict, (await WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "Again", "", CancellationToken.None))).Status);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
    }

    [Fact]
    public async Task MergingLinksACandidateToAnExistingDeviceValidatingEveryAffectedScope()
    {
        var (connection, x, y, _) = await SeedAsync();
        var device = (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Server", "d", ScopeA, CancellationToken.None))).Value!;
        // y lives in another scope the actor does not hold: nothing changes.
        var forbidden = await WithAsync(connection, s => s.MergeCandidateAsync(OnlyA, y, 1, device.DeviceId, 1, CancellationToken.None));
        Assert.Equal(InventoryStatus.Forbidden, forbidden.Status);
        // A device with a scope outside the actor's also blocks the merge (all affected scopes are checked).
        var wide = (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Wide", "d", new AuthorizedPair("site", "sensor-2"), CancellationToken.None))).Value!;
        Assert.Equal(InventoryStatus.Forbidden, (await WithAsync(connection, s => s.MergeCandidateAsync(OnlyA, x, 1, wide.DeviceId, 1, CancellationToken.None))).Status);
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE device_id IS NOT NULL"));

        var merged = await WithAsync(connection, s => s.MergeCandidateAsync(Admin, y, 1, device.DeviceId, 1, CancellationToken.None));
        Assert.Equal(InventoryStatus.Ok, merged.Status);
        Assert.Equal((device.DeviceId, "confirmed", 2L), (merged.Value!.DeviceId!.Value, merged.Value.State, merged.Value.Revision));
        // The device now spans both scopes and its revision advanced, so a decision made on the old revision conflicts.
        Assert.Equal(2L, await Count(connection, $"SELECT count(*) FROM monitoring.device_scope WHERE device_id='{device.DeviceId}'"));
        Assert.Equal(2L, await Count(connection, $"SELECT revision FROM monitoring.device WHERE device_id='{device.DeviceId}'"));
        Assert.Equal(InventoryStatus.Conflict, (await WithAsync(connection, s => s.MergeCandidateAsync(Admin, x, 1, device.DeviceId, 1, CancellationToken.None))).Status);
    }

    [Fact]
    public async Task ACandidateLinkedToAnotherDeviceIsAConflictWithNoPartialChange()
    {
        var (connection, x, _, _) = await SeedAsync();
        var first = (await WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "First", "", CancellationToken.None))).Value!;
        var second = (await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Second", "", ScopeA, CancellationToken.None))).Value!;
        var result = await WithAsync(connection, s => s.MergeCandidateAsync(Admin, x, first.Revision, second.DeviceId, second.Revision, CancellationToken.None));
        Assert.Equal(InventoryStatus.Conflict, result.Status);
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.device_candidate WHERE candidate_id='{x}' AND device_id='{first.DeviceId}' AND revision=2"));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.device WHERE device_id='{second.DeviceId}' AND revision=1"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.inventory_audit WHERE action='candidate-merged'"));
    }

    [Fact]
    public async Task ARejectionIsDurableAndOnlyALaterManualDecisionCanChangeIt()
    {
        var (connection, x, _, _) = await SeedAsync();
        Assert.Equal("rejected", (await WithAsync(connection, s => s.RejectCandidateAsync(Admin, x, 1, "randomized MAC", CancellationToken.None))).Value!.State);
        await SessionTestDatabase.AcceptAsync(connection, "x2", "site", "sensor", DeviceObservationContractTests.Observation("2026-10-06T11:00:00Z", "192.0.2.55"));
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.device_candidate WHERE candidate_id='{x}' AND state='rejected' AND revision=2"));
        // Rejecting again is a conflict on the stale revision; a deliberate later confirmation is allowed on the current one.
        Assert.Equal(InventoryStatus.Conflict, (await WithAsync(connection, s => s.RejectCandidateAsync(Admin, x, 1, "again", CancellationToken.None))).Status);
        var reconfirmed = await WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 2, "Actually a laptop", "", CancellationToken.None));
        Assert.Equal((InventoryStatus.Ok, "confirmed", 3L), (reconfirmed.Status, reconfirmed.Value!.State, reconfirmed.Value.Revision));
    }

    [Fact]
    public async Task OfTwoConcurrentDecisionsOnTheSameRevisionExactlyOneWins()
    {
        var (connection, x, _, _) = await SeedAsync();
        var results = await Task.WhenAll(
            WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "From A", "", CancellationToken.None)),
            WithAsync(connection, s => s.RejectCandidateAsync(Admin, x, 1, "From B", CancellationToken.None)),
            WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "From C", "", CancellationToken.None)));
        Assert.Equal(1, results.Count(r => r.Status == InventoryStatus.Ok));
        Assert.Equal(2, results.Count(r => r.Status == InventoryStatus.Conflict));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.inventory_audit WHERE action LIKE 'candidate-%'"));
        // No half-applied merge: a confirmed candidate has exactly one device and a rejected one has none.
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE (state='confirmed') <> (device_id IS NOT NULL)"));
        Assert.True(await Count(connection, "SELECT count(*) FROM monitoring.device") <= 1);
    }

    [Fact]
    public async Task AFailedAuditRecordRevertsTheWholeOperation()
    {
        var (connection, x, _, _) = await SeedAsync();
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE FUNCTION monitoring.refuse_inventory_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'audit store unavailable' USING ERRCODE='P0001'; END $$;
            CREATE TRIGGER refuse_inventory_audit BEFORE INSERT ON monitoring.inventory_audit
            FOR EACH ROW EXECUTE FUNCTION monitoring.refuse_inventory_audit();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => WithAsync(connection, s => s.ConfirmCandidateAsync(Admin, x, 1, "Device", "", CancellationToken.None)));
        await Assert.ThrowsAsync<PostgresException>(() => WithAsync(connection, s => s.CreateDeviceAsync(Admin, "Device", "", ScopeA, CancellationToken.None)));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device"));
        Assert.Equal(1L, await Count(connection, $"SELECT count(*) FROM monitoring.device_candidate WHERE candidate_id='{x}' AND state='candidate' AND revision=1 AND device_id IS NULL"));
    }

    [Fact]
    public async Task OutOfScopeCandidatesAreForbiddenForDecisionsAndInvisibleInListings()
    {
        var (connection, x, y, z) = await SeedAsync();
        Assert.Equal(InventoryStatus.Forbidden, (await WithAsync(connection, s => s.RejectCandidateAsync(OnlyA, y, 1, "no scope", CancellationToken.None))).Status);
        Assert.Equal(InventoryStatus.NotFound, (await WithAsync(connection, s => s.RejectCandidateAsync(OnlyA, Guid.NewGuid(), 1, "x", CancellationToken.None))).Status);
        var visible = await WithAsync(connection, s => s.ListCandidatesAsync(OnlyA.Scopes, null, 50, null, CancellationToken.None));
        Assert.Equal(new[] { x, z }.Order(), visible.Items.Select(candidate => candidate.CandidateId).Order());
        Assert.All(visible.Items, candidate => Assert.Equal("site/sensor", $"{candidate.SiteId}/{candidate.SensorId}"));
    }

    [Fact]
    public async Task ListingsArePagedByKeysetWithAMaximumOfOneHundredAndAnEmptyScopeSeesNothing()
    {
        var (connection, x, y, z) = await SeedAsync();
        var all = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await WithAsync(connection, s => s.ListCandidatesAsync(Admin.Scopes, null, 1, cursor, CancellationToken.None));
            Assert.True(page.Items.Count <= 1);
            all.AddRange(page.Items.Select(candidate => candidate.CandidateId));
            cursor = page.Next;
        } while (cursor is not null);
        Assert.Equal(new[] { x, y, z }.Order(), all.Order());
        foreach (var invalid in new[] { 0, -1, 101 })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WithAsync(connection, s => s.ListCandidatesAsync(Admin.Scopes, null, invalid, null, CancellationToken.None)));
        Assert.Empty((await WithAsync(connection, s => s.ListCandidatesAsync([], null, 10, null, CancellationToken.None))).Items);
        var byState = await WithAsync(connection, s => s.ListCandidatesAsync(Admin.Scopes, "rejected", 10, null, CancellationToken.None));
        Assert.Empty(byState.Items);
        // Candidates carry their temporary IP associations; observations list the facts without inventing an owner.
        var withIps = (await WithAsync(connection, s => s.ListCandidatesAsync(Admin.Scopes, "candidate", 10, null, CancellationToken.None))).Items.First(candidate => candidate.CandidateId == x);
        Assert.Equal(["192.0.2.10"], withIps.Associations.Select(association => association.Ip));
        var observations = await WithAsync(connection, s => s.ListObservationsAsync(OnlyA.Scopes, 50, null, CancellationToken.None));
        Assert.Equal(["x", "z"], observations.Items.Select(observation => observation.EventId).Order());
    }

    [Fact]
    public async Task ConfirmedDevicesAreReadableOnlyWithinTheCallersScopes()
    {
        var (connection, _, _, _) = await SeedAsync();
        await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "In A", "", ScopeA, CancellationToken.None));
        await WithAsync(connection, s => s.CreateDeviceAsync(Admin, "In B", "", new AuthorizedPair("site", "sensor-2"), CancellationToken.None));
        var inA = await WithAsync(connection, s => s.ListDevicesAsync([ScopeA], 50, null, CancellationToken.None));
        Assert.Equal(["In A"], inA.Items.Select(device => device.Name));
        var both = await WithAsync(connection, s => s.ListDevicesAsync(Admin.Scopes, 50, null, CancellationToken.None));
        Assert.Equal(["In A", "In B"], both.Items.Select(device => device.Name).Order());
        Assert.Empty((await WithAsync(connection, s => s.ListDevicesAsync([], 50, null, CancellationToken.None))).Items);
    }
}

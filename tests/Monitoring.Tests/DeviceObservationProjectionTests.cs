using Npgsql;

namespace Monitoring.Tests;

public sealed class DeviceObservationProjectionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<string> DatabaseAsync() => await SessionTestDatabase.CreateAsync(postgres);

    private static Task<long> Count(string connection, string sql) => SessionTestDatabase.ScalarAsync(connection, sql);

    private static Task AcceptObservation(string connection, string eventId, string json, string site = "site", string sensor = "sensor") =>
        SessionTestDatabase.AcceptAsync(connection, eventId, site, sensor, json);

    [Fact]
    public async Task ARepeatedObservationAndOneWithoutMacKeepOneObservationPerIdentityAndOnlyTheFirstMakesACandidate()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "obs-1", DeviceObservationContractTests.Observation());
        await AcceptObservation(connection, "obs-1", DeviceObservationContractTests.Observation()); // identical resend
        await AcceptObservation(connection, "obs-no-mac", DeviceObservationContractTests.Observation(mac: null, ip: "192.0.2.77"));
        await SessionTestDatabase.ProjectAsync(connection);
        // Replaying the worker over processed events changes nothing.
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.ingestion_inbox SET processed_at=NULL");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation WHERE mac IS NULL AND host(ip)='192.0.2.77'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE mac='aa:bb:cc:00:00:01' AND vlan_id=42 AND state='candidate' AND revision=1 AND device_id IS NULL"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_ip_association"));
        // Observations are not sessions: nothing is published for the search index.
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task AnIpChangeKeepsOneCandidateWithBothPeriodsAndAnotherScopeStaysSeparate()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "a1", DeviceObservationContractTests.Observation("2026-10-06T10:00:00Z", "192.0.2.10"));
        await AcceptObservation(connection, "a2", DeviceObservationContractTests.Observation("2026-10-06T10:05:00Z", "192.0.2.11"));
        await AcceptObservation(connection, "a3", DeviceObservationContractTests.Observation("2026-10-06T10:09:00Z", "192.0.2.10"));
        await AcceptObservation(connection, "other-vlan", DeviceObservationContractTests.Observation("2026-10-06T10:01:00Z", "192.0.2.10", vlan: "43"));
        await AcceptObservation(connection, "untagged", DeviceObservationContractTests.Observation("2026-10-06T10:02:00Z", "192.0.2.10", vlan: null));
        await AcceptObservation(connection, "other-sensor", DeviceObservationContractTests.Observation("2026-10-06T10:03:00Z", "192.0.2.10"), sensor: "sensor-2");
        await AcceptObservation(connection, "other-site", DeviceObservationContractTests.Observation("2026-10-06T10:04:00Z", "192.0.2.10"), site: "site-2");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(5L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate"));
        // The first candidate keeps both addresses with their own periods; nothing is merged by IP or MAC alone.
        Assert.Equal(1L, await Count(connection, """
            SELECT count(*) FROM monitoring.device_candidate WHERE site_id='site' AND sensor_id='sensor' AND vlan_id=42
              AND first_seen='2026-10-06T10:00:00Z' AND last_seen='2026-10-06T10:09:00Z'
            """));
        Assert.Equal(2L, await Count(connection, """
            SELECT count(*) FROM monitoring.device_ip_association a JOIN monitoring.device_candidate c USING(candidate_id)
            WHERE c.site_id='site' AND c.sensor_id='sensor' AND c.vlan_id=42
              AND ((host(a.ip)='192.0.2.10' AND a.first_seen='2026-10-06T10:00:00Z' AND a.last_seen='2026-10-06T10:09:00Z')
                OR (host(a.ip)='192.0.2.11' AND a.first_seen='2026-10-06T10:05:00Z' AND a.last_seen='2026-10-06T10:05:00Z'))
            """));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE vlan_id IS NULL"));
    }

    [Fact]
    public async Task OutOfOrderObservationsWidenThePeriodWithoutShrinkingIt()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "late", DeviceObservationContractTests.Observation("2026-10-06T12:00:00Z"));
        await SessionTestDatabase.ProjectAsync(connection);
        await AcceptObservation(connection, "early", DeviceObservationContractTests.Observation("2026-10-06T08:00:00Z"));
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE first_seen='2026-10-06T08:00:00Z' AND last_seen='2026-10-06T12:00:00Z'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_ip_association WHERE first_seen='2026-10-06T08:00:00Z' AND last_seen='2026-10-06T12:00:00Z'"));
    }

    [Fact]
    public async Task InvalidObservationsAreQuarantinedWithoutCreatingAnything()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "multicast", DeviceObservationContractTests.Observation(mac: "01:00:5e:00:00:01"));
        await AcceptObservation(connection, "extra-field", DeviceObservationContractTests.Observation().Replace("\"version\":1", "\"version\":1,\"hostname\":\"printer\""));
        await AcceptObservation(connection, "valid", DeviceObservationContractTests.Observation(ip: "192.0.2.50"));
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_quarantine WHERE cause='contract-invalid' AND state='unresolved'"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate"));
    }

    [Fact]
    public async Task ARejectedCandidateStaysRejectedWhenNewObservationsArrive()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "first", DeviceObservationContractTests.Observation("2026-10-06T10:00:00Z"));
        await SessionTestDatabase.ProjectAsync(connection);
        await SessionTestDatabase.ExecuteAsync(connection, "UPDATE monitoring.device_candidate SET state='rejected', revision=revision+1");
        await AcceptObservation(connection, "second", DeviceObservationContractTests.Observation("2026-10-06T11:00:00Z", "192.0.2.99"));
        await SessionTestDatabase.ProjectAsync(connection);
        // The decision and its revision are untouched; only observed facts advance.
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate WHERE state='rejected' AND revision=2 AND last_seen='2026-10-06T11:00:00Z'"));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.device_ip_association"));
        Assert.Equal(2L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
    }

    [Fact]
    public async Task AFailedMarkingRollsBackTheObservationAndItsEffects()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "obs", DeviceObservationContractTests.Observation());
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, "P0001");
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ProjectAsync(connection));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device_candidate"));
        Assert.Equal(0L, await Count(connection, "SELECT count(*) FROM monitoring.device_ip_association"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL AND quarantined_at IS NULL"));
    }

    [Fact]
    public async Task SessionsAndObservationsShareTheWorkerWithoutInterfering()
    {
        var connection = await DatabaseAsync();
        await AcceptObservation(connection, "obs", DeviceObservationContractTests.Observation());
        await SessionTestDatabase.AcceptAsync(connection, "session");
        await SessionTestDatabase.ProjectAsync(connection);
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.projection_outbox"));
        Assert.Equal(1L, await Count(connection, "SELECT count(*) FROM monitoring.device_observation"));
    }
}

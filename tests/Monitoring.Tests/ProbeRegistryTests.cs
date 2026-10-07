using Microsoft.Extensions.Configuration;
using Monitoring.Host.Security;
using Npgsql;

namespace Monitoring.Tests;

public sealed class ProbeRegistryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Issuer = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899";

    private static ProbeRegistry Registry(Monitoring.Persistence.MonitoringDbContext db) => new(db);

    [Fact]
    public async Task ARegisteredCertificateIsFoundWithItsBindingAndAnUnknownOneIsNot()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await using var db = SessionTestDatabase.Context(connection);
        Assert.Equal(ProbeRegistration.Registered, await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "ops", "initial", CancellationToken.None));
        Assert.Equal(new ProbeBinding("madrid", "sensor-1", true), await Registry(db).FindAsync(Issuer, "03E8", CancellationToken.None));
        Assert.Null(await Registry(db).FindAsync(Issuer, "03E9", CancellationToken.None));
        Assert.Null(await Registry(db).FindAsync(new string('A', 64), "03E8", CancellationToken.None));
    }

    [Fact]
    public async Task ASerialCanNeverBeRebornAsAnotherSensorAndRepeatingTheSameBindingChangesNothing()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await using var db = SessionTestDatabase.Context(connection);
        await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "ops", "initial", CancellationToken.None);
        Assert.Equal(ProbeRegistration.AlreadyRegistered, await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "ops", "again", CancellationToken.None));
        Assert.Equal(ProbeRegistration.Conflict, await Registry(db).RegisterAsync("barcelona", "sensor-7", Issuer, "03E8", "ops", "steal", CancellationToken.None));
        Assert.Equal(new ProbeBinding("madrid", "sensor-1", true), await Registry(db).FindAsync(Issuer, "03E8", CancellationToken.None));
    }

    [Fact]
    public async Task DisablingPersistsAcrossFreshConnectionsAndNothingReEnablesIt()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await using (var db = SessionTestDatabase.Context(connection))
        {
            await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "ops", "initial", CancellationToken.None);
            Assert.Equal(ProbeDisabling.Disabled, await Registry(db).DisableAsync(Issuer, "03E8", "ops", "key compromise", CancellationToken.None));
            Assert.Equal(ProbeDisabling.AlreadyDisabled, await Registry(db).DisableAsync(Issuer, "03E8", "ops", "again", CancellationToken.None));
            Assert.Equal(ProbeDisabling.NotFound, await Registry(db).DisableAsync(Issuer, "9999", "ops", "unknown", CancellationToken.None));
            Assert.Equal(ProbeRegistration.Conflict, await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "ops", "revive", CancellationToken.None));
        }
        // "Restart": a new context, a new connection.
        await using var restarted = SessionTestDatabase.Context(connection);
        Assert.Equal(new ProbeBinding("madrid", "sensor-1", false), await Registry(restarted).FindAsync(Issuer, "03E8", CancellationToken.None));
    }

    [Fact]
    public async Task EveryDecisionIsAuditedWithActorAndReasonAndTheTrailIsAppendOnly()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await using var db = SessionTestDatabase.Context(connection);
        await Registry(db).RegisterAsync("madrid", "sensor-1", Issuer, "03E8", "alice", "initial", CancellationToken.None);
        await Registry(db).DisableAsync(Issuer, "03E8", "bob", "key compromise", CancellationToken.None);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.probe_registry_audit WHERE action='registered' AND actor='alice' AND reason='initial'"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.probe_registry_audit WHERE action='disabled' AND actor='bob' AND reason='key compromise'"));
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, "DELETE FROM monitoring.probe_registry_audit"));
        await Assert.ThrowsAsync<PostgresException>(() => SessionTestDatabase.ExecuteAsync(connection, "DELETE FROM monitoring.probe_registry"));
    }

    [Theory]
    [InlineData("", "sensor", "03E8", "ops", "r")]
    [InlineData("site", "sensor", "nothex", "ops", "r")]
    [InlineData("site", "sensor", "03E8", "", "r")]
    [InlineData("site", "sensor", "03E8", "ops", " ")]
    public async Task InvalidInputIsRejectedBeforeTouchingTheDatabase(string site, string sensor, string serial, string actor, string reason)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await using var db = SessionTestDatabase.Context(connection);
        await Assert.ThrowsAsync<ArgumentException>(() => Registry(db).RegisterAsync(site, sensor, Issuer, serial, actor, reason, CancellationToken.None));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.probe_registry"));
    }
}

public sealed class ProbesCommandTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TestPki _pki = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("probes-").FullName;

    private string CertificateFile(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        var path = Path.Combine(_directory, $"{certificate.SerialNumber}.pem");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(string connection, params string[] args)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Monitoring"] = connection }).Build();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await ProbesCommand.RunAsync(args, configuration, output, error, CancellationToken.None);
        return (code, output.ToString().Trim(), error.ToString().Trim());
    }

    [Fact]
    public async Task RegisteringAndDisablingFromTheCertificateFileRoundTripsThroughTheValidatorsKeys()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var certificate = _pki.Issue("probe");
        var file = CertificateFile(certificate);
        Assert.Equal((0, "registered"), Trim(await RunAsync(connection, "register", "madrid", "sensor-1", "--certificate", file, "--actor", "ops", "--reason", "install")));
        await using var db = SessionTestDatabase.Context(connection);
        var binding = await new ProbeRegistry(db).FindAsync(ProbeCertificateValidator.IssuerKey(certificate), CrlParser.Normalize(certificate.SerialNumberBytes.Span), CancellationToken.None);
        Assert.Equal(new ProbeBinding("madrid", "sensor-1", true), binding);
        Assert.Equal((2, "already-registered"), Trim(await RunAsync(connection, "register", "madrid", "sensor-1", "--certificate", file, "--actor", "ops", "--reason", "again")));
        Assert.Equal((2, "conflict"), Trim(await RunAsync(connection, "register", "other", "sensor-9", "--certificate", file, "--actor", "ops", "--reason", "steal")));
        Assert.Equal((0, "disabled"), Trim(await RunAsync(connection, "disable", "--certificate", file, "--actor", "ops", "--reason", "retired")));
        Assert.Equal((2, "already-disabled"), Trim(await RunAsync(connection, "disable", "--certificate", file, "--actor", "ops", "--reason", "retired")));
    }

    [Fact]
    public async Task MissingActorReasonCertificateOrConnectionFailsWithoutLeakingAnything()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        var missing = await RunAsync(connection, "register", "madrid", "sensor-1", "--certificate", "/nonexistent.pem", "--actor", "ops", "--reason", "x");
        Assert.Equal(1, missing.Code);
        Assert.DoesNotContain(connection, missing.Error, StringComparison.Ordinal);
        Assert.Equal(1, (await RunAsync(connection, "register", "madrid", "sensor-1")).Code);
        Assert.Equal(1, (await RunAsync("", "disable")).Code);
    }

    private static (int, string) Trim((int Code, string Out, string Error) result) => (result.Code, result.Out);

    public void Dispose() { _pki.Dispose(); Directory.Delete(_directory, true); }
}

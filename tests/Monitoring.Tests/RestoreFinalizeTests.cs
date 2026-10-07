using Microsoft.Extensions.Configuration;
using Monitoring.Host.Operations;

namespace Monitoring.Tests;

// After restoring a backup, history older than the retention window is back (the restore predates its expiry). Nothing may serve or
// publish it: the finalizer re-applies retention to completion and, when a search index is configured, rebuilds it from the authority.
public sealed class RestoreFinalizeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<(int Code, string Out, string Error)> RunAsync(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RestoreFinalizeCommand.RunAsync(configuration, output, error, CancellationToken.None);
        return (code, output.ToString(), error.ToString());
    }

    private static Dictionary<string, string?> Settings(string connection, string? window = "3.00:00:00") => new()
    {
        ["ConnectionStrings:Monitoring"] = connection, ["Retention:SessionRetention"] = window
    };

    [Fact]
    public async Task ExpiredHistoryComingBackWithARestoreIsExpiredBeforeAnythingIsServedOrPublished()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        for (var index = 0; index < 5; index++) await SessionTestDatabase.AcceptAsync(connection, $"event-{index}");
        await SessionTestDatabase.AcceptAsync(connection, "obs", json: DeviceObservationContractTests.Observation("2026-09-29T10:00:00Z", "192.0.2.10"));
        await SessionTestDatabase.ProjectAsync(connection);
        var result = await RunAsync(Settings(connection));
        Assert.Equal(0, result.Code);
        Assert.Contains("expiredSessions=5", result.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, result.Out + result.Error, StringComparison.Ordinal);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(5L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted'"));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.device_observation"));
        // A second run (an operator repeating the step) is harmless.
        var again = await RunAsync(Settings(connection));
        Assert.Equal(0, again.Code);
        Assert.Contains("expiredSessions=0", again.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnExplicitRetentionWindowTheFinalizerRefusesInsteadOfGuessing()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        var result = await RunAsync(new Dictionary<string, string?> { ["ConnectionStrings:Monitoring"] = connection });
        Assert.Equal(1, result.Code);
        Assert.Contains("Retention", result.Error, StringComparison.Ordinal);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1, (await RunAsync(new Dictionary<string, string?> { ["Retention:SessionRetention"] = "3.00:00:00" })).Code);
    }

    [Fact]
    public async Task ARebuildThatCannotRunAfterRetentionIsReportedAndNeverLooksLikeSuccess()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        var settings = Settings(connection);
        settings["Search:Elasticsearch:Url"] = "http://127.0.0.1:1"; // configured but the rest of the rebuild settings are missing
        var result = await RunAsync(settings);
        Assert.Equal(1, result.Code);
        Assert.Contains("retention completed", result.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("search rebuild", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_identity WHERE state='deleted'"));
    }

    [Fact]
    public async Task AnInvalidWindowIsRejectedBeforeAnythingIsDeleted()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ProjectAsync(connection);
        var result = await RunAsync(Settings(connection, window: "0.00:10:00"));
        Assert.Equal(1, result.Code);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
    }
}

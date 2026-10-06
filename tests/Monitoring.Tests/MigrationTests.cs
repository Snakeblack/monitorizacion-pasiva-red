using Npgsql;

namespace Monitoring.Tests;

public sealed class MigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MigrateCreatesInboxSchemaAndCanBeRepeated()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        await AssertDatabaseIsEmptyAsync(connectionString);

        var firstRun = await MigrationProcess.RunAsync(connectionString);
        Assert.True(
            firstRun.ExitCode == 0,
            $"Migration process failed: {Redact(firstRun.StandardError, connectionString)} {Redact(firstRun.StandardOutput, connectionString)}");

        var secondRun = await MigrationProcess.RunAsync(connectionString);
        Assert.Equal(0, secondRun.ExitCode);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.True(await ExistsAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'monitoring')"));
        Assert.Equal(SessionTestDatabase.ExpectedMigrationCount(), await ScalarLongAsync(
            connection,
            "SELECT count(*) FROM public.\"__EFMigrationsHistory\""));
        Assert.Equal(18L, await ScalarLongAsync(
            connection,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'monitoring'"));
    }

    private static async Task AssertDatabaseIsEmptyAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.False(await ExistsAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'monitoring')"));
        Assert.False(await ExistsAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory')"));
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static string Redact(string output, string connectionString) =>
        output.Replace(connectionString, "<redacted-connection-string>", StringComparison.Ordinal);

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

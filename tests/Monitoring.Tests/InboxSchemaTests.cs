using Npgsql;

namespace Monitoring.Tests;

public sealed class InboxSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task IncrementalMigrationAddsInboxSchemaWithCompositeUniquenessAndTemporalIndex()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // S01 only creates the monitoring schema; this is its clean post-migration state.
        await InitializeS01BaselineAsync(connection);
        Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT count(*) FROM public.\"__EFMigrationsHistory\""));
        Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'monitoring'"));

        var migration = await MigrationProcess.RunAsync(connectionString);
        Assert.True(
            migration.ExitCode == 0,
            $"Migration process failed: {Redact(migration.StandardError, connectionString)} {Redact(migration.StandardOutput, connectionString)}");

        Assert.Equal(4L, await ScalarLongAsync(connection, "SELECT count(*) FROM public.\"__EFMigrationsHistory\""));
        Assert.Equal(
            new[] { "sensor_id", "site_id" },
            await ColumnNamesAsync(connection, "ingestion_origin"));
        Assert.Equal(
            new[] { "accepted_at", "batch_id", "data", "event_id", "occurred_at", "occurred_at_text", "processed_at", "schema_version", "sensor_id", "site_id" },
            await ColumnNamesAsync(connection, "ingestion_inbox"));

        Assert.Equal("jsonb", await ColumnTypeAsync(connection, "ingestion_inbox", "data"));
        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "ingestion_inbox", "occurred_at"));
        Assert.Equal("text", await ColumnTypeAsync(connection, "ingestion_inbox", "occurred_at_text"));
        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "ingestion_inbox", "accepted_at"));
        Assert.Equal(128, await CharacterLimitAsync(connection, "ingestion_inbox", "event_id"));

        await InsertOriginAsync(connection, "site-a", "sensor-a");
        await InsertOriginAsync(connection, "site-b", "sensor-a");
        await InsertInboxAsync(connection, "site-a", "sensor-a", "event-1");

        var duplicate = await Assert.ThrowsAsync<PostgresException>(
            () => InsertInboxAsync(connection, "site-a", "sensor-a", "event-1"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);

        await InsertInboxAsync(connection, "site-b", "sensor-a", "event-1");
        Assert.Equal(2L, await ScalarLongAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox"));

        var indexDefinition = await ScalarStringAsync(
            connection,
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'monitoring' AND tablename = 'ingestion_inbox' AND indexname = 'IX_ingestion_inbox_site_id_sensor_id_accepted_at'");
        Assert.Contains("(site_id, sensor_id, accepted_at)", indexDefinition, StringComparison.Ordinal);
    }

    private static async Task InitializeS01BaselineAsync(NpgsqlConnection connection)
    {
        const string sql = "CREATE SCHEMA monitoring; " +
            "CREATE TABLE public.\"__EFMigrationsHistory\" (\"MigrationId\" character varying(150) NOT NULL, \"ProductVersion\" character varying(32) NOT NULL, CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY (\"MigrationId\")); " +
            "INSERT INTO public.\"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@migration_id, @product_version)";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("migration_id", "202609240001_InitialSchema");
        command.Parameters.AddWithValue("product_version", "10.0.12");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ColumnNamesAsync(NpgsqlConnection connection, string tableName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'monitoring' AND table_name = @table_name ORDER BY column_name",
            connection);
        command.Parameters.AddWithValue("table_name", tableName);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static async Task<string> ColumnTypeAsync(NpgsqlConnection connection, string tableName, string columnName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns WHERE table_schema = 'monitoring' AND table_name = @table_name AND column_name = @column_name",
            connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("column_name", columnName);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> CharacterLimitAsync(NpgsqlConnection connection, string tableName, string columnName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_schema = 'monitoring' AND table_name = @table_name AND column_name = @column_name",
            connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("column_name", columnName);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task InsertOriginAsync(NpgsqlConnection connection, string siteId, string sensorId)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO monitoring.ingestion_origin (site_id, sensor_id) VALUES (@site_id, @sensor_id)",
            connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertInboxAsync(NpgsqlConnection connection, string siteId, string sensorId, string eventId)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO monitoring.ingestion_inbox (site_id, sensor_id, event_id, batch_id, schema_version, occurred_at, occurred_at_text, data, accepted_at) " +
            "VALUES (@site_id, @sensor_id, @event_id, 'batch-1', 1, TIMESTAMPTZ '2026-09-25 10:00:00+00', @occurred_at_text, CAST('{}' AS jsonb), TIMESTAMPTZ '2026-09-25 10:00:01+00')",
            connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sensor_id", sensorId);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("occurred_at_text", "2026-09-25T10:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarStringAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string Redact(string output, string connectionString) =>
        output.Replace(connectionString, "<redacted-connection-string>", StringComparison.Ordinal);
}

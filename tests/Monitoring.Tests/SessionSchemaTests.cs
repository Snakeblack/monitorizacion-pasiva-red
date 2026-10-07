using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Monitoring.Persistence;
using Monitoring.Persistence.Migrations;
using Npgsql;

namespace Monitoring.Tests;

public sealed class SessionSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task UpgradeS02PreservesPendingDataAndAddsCompositeProjectionAndPartialIndex()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(connectionString, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "public")).Options);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE SCHEMA monitoring;
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            INSERT INTO public."__EFMigrationsHistory" VALUES ('202609240001_InitialSchema','10.0.12'),('202609240002_DurableInbox','10.0.12');
            """);
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(new DurableInbox().UpOperations))
        {
            await ExecuteAsync(connection, command.CommandText);
        }
        await ExecuteAsync(connection, """
            INSERT INTO monitoring.ingestion_origin VALUES ('site','sensor');
            INSERT INTO monitoring.ingestion_inbox VALUES ('site','sensor','event','batch',1,now(),'2026-09-29T12:00:00Z','{"arbitrary":true}',now());
            """);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(SessionTestDatabase.ExpectedMigrationCount(), await ScalarAsync(connection, "SELECT count(*) FROM public.\"__EFMigrationsHistory\""));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE data = '{\"arbitrary\":true}'::jsonb AND processed_at IS NULL"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        await ExecuteAsync(connection, "INSERT INTO monitoring.session_projection VALUES ('site','sensor','event','2026-09-29T12:00:00Z','{}')");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            "INSERT INTO monitoring.session_projection VALUES ('site','sensor','event','different','{}')"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            "INSERT INTO monitoring.session_projection VALUES ('site','other-sensor','event','time','{}')"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            "DELETE FROM monitoring.ingestion_inbox WHERE event_id = 'event'"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, delete.SqlState);
        Assert.Equal(1L, await ScalarAsync(connection, """
            SELECT count(*) FROM pg_indexes WHERE schemaname = 'monitoring'
            AND tablename = 'ingestion_inbox' AND indexdef LIKE '%(accepted_at, site_id, sensor_id, event_id)%'
            AND indexdef LIKE '%WHERE (processed_at IS NULL)%'
            """));
        Assert.Equal(1L, await ScalarAsync(connection, """
            SELECT count(*) FROM pg_indexes WHERE schemaname = 'monitoring'
            AND indexname = 'IX_ingestion_inbox_site_id_sensor_id_accepted_at'
            """));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

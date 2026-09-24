using System.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Monitoring.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("monitoring")
        .WithUsername("monitoring")
        .WithPassword("local-test-password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"monitoring_{Guid.NewGuid():N}";
        var connectionBuilder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());

        await using (var connection = new NpgsqlConnection(connectionBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        connectionBuilder.Database = databaseName;
        return connectionBuilder.ConnectionString;
    }
}

public sealed record MigrationProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class MigrationProcess
{
    public static async Task<MigrationProcessResult> RunAsync(string connectionString)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add("--migrate");
        startInfo.Environment["ConnectionStrings__Monitoring"] = connectionString;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new MigrationProcessResult(-1, await outputTask, await errorTask);
        }

        return new MigrationProcessResult(process.ExitCode, await outputTask, await errorTask);
    }
}

using System.Net;
using System.Net.Sockets;

namespace Monitoring.Tests;

public sealed class MigrationFailureTests
{
    [Fact]
    public async Task MigrateReportsUnavailableDatabaseWithoutPrintingCredentials()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var secret = "migration-test-secret";
        var connectionString = $"Host=127.0.0.1;Port={port};Database=monitoring;Username=monitoring;Password={secret};Timeout=2;SSL Mode=Disable";
        var result = await MigrationProcess.RunAsync(connectionString);
        var output = $"{result.StandardOutput}\n{result.StandardError}";

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Database migration failed", output);
        Assert.DoesNotContain(secret, output);
    }
}

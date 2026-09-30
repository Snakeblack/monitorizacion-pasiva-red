using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Host.Ingestion;

namespace Monitoring.Tests;

internal static class SessionViewChainHost
{
    private const string SiteId = "site";
    private const string SensorId = "sensor";
    private const string EventId = "event";

    public static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--view-chain", StringComparer.Ordinal))
        {
            return 1;
        }

        var postgres = new PostgresFixture();
        await postgres.InitializeAsync();
        try
        {
            return await RunAsync(postgres);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"view-chain failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally
        {
            await postgres.DisposeAsync();
        }
    }

    private static async Task<int> RunAsync(PostgresFixture postgres)
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(HostProjectRoot());
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:Monitoring", connection);
                builder.UseSetting("TrustedSessionRead:SiteId", SiteId);
                builder.UseSetting("TrustedSessionRead:SensorId", SensorId);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITrustedSensorIdentityProvider>();
                    services.AddSingleton<ITrustedSensorIdentityProvider>(new ChainSensorIdentity());
                });
            });
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var batch = """
            {"schemaVersion":1,"batchId":"batch","siteId":"site","sensorId":"sensor","events":[
            {"eventId":"event","occurredAt":"2026-09-29T12:00:00.100Z","data":DATA}]}
            """.Replace("DATA", SyntheticSessionContractTests.ValidData, StringComparison.Ordinal);
        using var ack = await client.PostAsync(
            "/api/v1/ingestion/batches",
            new StringContent(batch, Encoding.UTF8, "application/json"));
        var ackBody = await ack.Content.ReadAsStringAsync();
        if (ack.StatusCode != HttpStatusCode.OK || ackBody.Length != 0)
        {
            await Console.Error.WriteLineAsync($"ACK {ack.StatusCode} body-length={ackBody.Length}");
            return 1;
        }

        try
        {
            await SessionWorkerTests.WaitAsync(async () =>
            {
                using var response = await client.GetAsync($"/api/v1/sessions/{EventId}");
                return response.StatusCode == HttpStatusCode.OK;
            });
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("view-chain GET 200 timed out");
            return 1;
        }

        var origin = client.BaseAddress?.ToString().TrimEnd('/') ?? "";
        await Console.Out.WriteLineAsync($"VIEW_CHAIN_READY {origin} {EventId}");
        await Console.Out.FlushAsync();
        await Console.In.ReadToEndAsync();
        return 0;
    }

    private static string HostProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Monitoring.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "Monitoring.Host");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Monitoring.slnx");
    }

    private sealed class ChainSensorIdentity : ITrustedSensorIdentityProvider
    {
        public TrustedSensorIdentity? Resolve(HttpContext context) => new(SiteId, SensorId);
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Monitoring.Tests;

// The product's meters leave the process only over OTLP and only when an endpoint is configured (ADR-021): with none, nothing is exported
// and no network call is attempted; with one, every Monitoring.* meter is registered with the exporter.
public sealed class TelemetryExportTests
{
    private static WebApplicationFactory<Program> Host(string? endpoint) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            ProbeTestTrust.For(builder, "Testing");
            if (endpoint is not null) builder.UseSetting("Telemetry:Otlp:Endpoint", endpoint);
        });

    [Fact]
    public void WithoutAnEndpointNoMeterProviderIsRegistered()
    {
        using var factory = Host(null);
        Assert.Null(factory.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void WithAnEndpointTheMeterProviderIsRegisteredAndStartsWithTheHost()
    {
        using var factory = Host("http://127.0.0.1:1/api/v1/otlp/v1/metrics");
        var provider = factory.Services.GetService<MeterProvider>();
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData("not a uri")]
    [InlineData("ftp://collector/otlp")]
    public void AnInvalidEndpointStopsTheHostInsteadOfSilentlyDroppingMetrics(string endpoint)
    {
        using var factory = Host(endpoint);
        Assert.ThrowsAny<Exception>(() => factory.Services);
    }
}

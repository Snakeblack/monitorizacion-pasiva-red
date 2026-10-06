using Monitoring.Probe;
namespace Monitoring.Probe.Tests;
public sealed class ConfigurationTests
{
    private static ProbeConfiguration Configuration => new(new("s", "p"), new("eth0"), new(), new(), "/data/probe.db", "https://private-api:8443",
        SustainedEventsPerSecond: 1, MeasuredBytesPerEventIncludingOverhead: 100);
    [Fact]
    public void LiveConfigurationNeedsMeasuredRateAndSizedQuotaAndTls()
    {
        Assert.Throws<ArgumentException>(() => (Configuration with { SustainedEventsPerSecond = null }).Validate());
        Assert.Throws<ArgumentException>(() => (Configuration with { Endpoint = "http://private-api" }).Validate());
        Assert.Throws<ArgumentException>(() => (Configuration with { Spool = new(MaximumDiskBytes: 1000000) }).Validate());
        Configuration.Validate();
    }
    [Fact]
    public void FixtureCanCaptureWithoutMakingProductionRateClaims()
    {
        (Configuration with { Capture = new("fixture", FixturePcap: "/fixtures/real.pcap"), Endpoint = null,
            SustainedEventsPerSecond = null, MeasuredBytesPerEventIncludingOverhead = null }).Validate();
        Assert.Throws<ArgumentException>(() => (Configuration with { Margin = .1m }).Validate());
    }
}

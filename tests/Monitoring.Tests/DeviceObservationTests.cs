using System.Text.Json;
using Monitoring.Domain.Inventory;

namespace Monitoring.Tests;

public sealed class DeviceObservationContractTests
{
    internal static string Observation(string observedAt = "2026-10-06T10:00:00.123Z", string ip = "192.0.2.10", string? mac = "aa:bb:cc:00:00:01", string? vlan = "42") =>
        $$"""{"kind":"device-observation","version":1,"observedAt":"{{observedAt}}","ip":"{{ip}}","mac":{{(mac is null ? "null" : $"\"{mac}\"")}},"vlanId":{{vlan ?? "null"}}}""";

    private static bool Parse(string json, out DeviceObservation? observation)
    {
        using var document = JsonDocument.Parse(json);
        return DeviceObservationContract.TryParse(document.RootElement, out observation);
    }

    [Fact]
    public void AValidObservationNormalizesMacIpAndTime()
    {
        Assert.True(Parse(Observation(ip: "2001:0DB8:0:0:0:0:0:1", mac: "AA-BB-CC-00-00-01"), out var observation));
        Assert.Equal(("aa:bb:cc:00:00:01", "2001:db8::1", 42), (observation!.Mac, observation.Ip.ToString(), observation.VlanId));
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T10:00:00.123Z"), observation.ObservedAt);
    }

    [Fact]
    public void AnObservationWithoutMacOrVlanIsValid()
    {
        Assert.True(Parse(Observation(mac: null, vlan: null), out var observation));
        Assert.Null(observation!.Mac);
        Assert.Null(observation.VlanId);
    }

    [Theory]
    [InlineData("2026-10-06T10:00:00+00:00", "192.0.2.10", "aa:bb:cc:00:00:01", "42")]   // not UTC Z
    [InlineData("2026-10-06T10:00:00.1234Z", "192.0.2.10", "aa:bb:cc:00:00:01", "42")]   // finer than milliseconds
    [InlineData("2026-10-06T10:00:00Z", "999.0.0.1", "aa:bb:cc:00:00:01", "42")]         // invalid IP
    [InlineData("2026-10-06T10:00:00Z", "10", "aa:bb:cc:00:00:01", "42")]                // legacy short form
    [InlineData("2026-10-06T10:00:00Z", "fe80::1%eth0", "aa:bb:cc:00:00:01", "42")]      // scoped address
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "aa:bb:cc:00:00", "42")]           // 40-bit
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "aa:bb:cc:00:00:01:02", "42")]     // 56-bit
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "ff:ff:ff:ff:ff:ff", "42")]        // broadcast
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "01:00:5e:00:00:01", "42")]        // multicast
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "00:00:00:00:00:00", "42")]        // not an address
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "zz:bb:cc:00:00:01", "42")]        // not hex
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "aa:bb:cc:00:00:01", "4095")]      // VLAN out of range
    [InlineData("2026-10-06T10:00:00Z", "192.0.2.10", "aa:bb:cc:00:00:01", "-1")]
    public void InvalidFieldsAreRejected(string observedAt, string ip, string mac, string vlan) =>
        Assert.False(Parse(Observation(observedAt, ip, mac, vlan), out _));

    [Theory]
    [InlineData("{\"kind\":\"device-observation\",\"version\":2,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":\"192.0.2.1\",\"mac\":null,\"vlanId\":null}")]
    [InlineData("{\"kind\":\"other\",\"version\":1,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":\"192.0.2.1\",\"mac\":null,\"vlanId\":null}")]
    [InlineData("{\"kind\":\"device-observation\",\"version\":1,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":\"192.0.2.1\",\"mac\":null}")]
    [InlineData("{\"kind\":\"device-observation\",\"version\":1,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":\"192.0.2.1\",\"mac\":null,\"vlanId\":null,\"hostname\":\"x\"}")]
    [InlineData("{\"kind\":\"device-observation\",\"version\":1,\"version\":1,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":\"192.0.2.1\",\"mac\":null,\"vlanId\":null}")]
    [InlineData("{\"kind\":\"device-observation\",\"version\":1,\"observedAt\":\"2026-10-06T10:00:00Z\",\"ip\":1,\"mac\":null,\"vlanId\":null}")]
    [InlineData("[]")]
    public void WrongShapesAreRejected(string json) => Assert.False(Parse(json, out _));
}

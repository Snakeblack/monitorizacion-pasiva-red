using System.Security.Cryptography.X509Certificates;
using Monitoring.Host.Security;

namespace Monitoring.Tests;

public sealed class CrlParserTests
{
    [Fact]
    public void ASignedCrlYieldsItsIssuerValidityWindowAndRevokedSerials()
    {
        using var pki = new TestPki();
        using var revoked = pki.Issue("probe-1");
        using var other = pki.Issue("probe-2");
        pki.Revoke(revoked);
        var thisUpdate = DateTimeOffset.UtcNow.AddMinutes(-2);
        var nextUpdate = DateTimeOffset.UtcNow.AddHours(2);
        var crl = pki.BuildCrl(thisUpdate, nextUpdate);
        Assert.True(CrlParser.TryParse(crl, pki.Ca, out var list, out var failure), failure);
        Assert.Equal(pki.Ca.SubjectName.Name, list!.Issuer);
        Assert.Equal(thisUpdate.ToUnixTimeSeconds(), list.ThisUpdate.ToUnixTimeSeconds());
        Assert.Equal(nextUpdate.ToUnixTimeSeconds(), list.NextUpdate!.Value.ToUnixTimeSeconds());
        Assert.Contains(Convert.ToHexString(revoked.SerialNumberBytes.ToArray()), list.RevokedSerials);
        Assert.DoesNotContain(Convert.ToHexString(other.SerialNumberBytes.ToArray()), list.RevokedSerials);
    }

    [Fact]
    public void ACrlWithNoRevocationsIsValidAndEmpty()
    {
        using var pki = new TestPki();
        Assert.True(CrlParser.TryParse(pki.BuildCrl(), pki.Ca, out var list, out _));
        Assert.Empty(list!.RevokedSerials);
    }

    [Fact]
    public void ACrlSignedByAnotherKeyIsRejectedSoItCanNeverRelaxRevocation()
    {
        using var pki = new TestPki();
        using var forged = TestPki.ForgedKey();
        Assert.False(CrlParser.TryParse(pki.BuildCrl(signWith: forged), pki.Ca, out _, out var failure));
        Assert.Equal("crl-signature-invalid", failure);
    }

    [Fact]
    public void ACrlFromAnotherIssuerIsRejected()
    {
        using var pki = new TestPki("CA one");
        using var other = new TestPki("CA two");
        Assert.False(CrlParser.TryParse(other.BuildCrl(), pki.Ca, out _, out var failure));
        Assert.Contains(failure, new[] { "crl-issuer-mismatch", "crl-signature-invalid" });
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF })]
    public void GarbageIsRejectedWithoutThrowing(byte[] data)
    {
        using var pki = new TestPki();
        Assert.False(CrlParser.TryParse(data, pki.Ca, out _, out var failure));
        Assert.Equal("crl-malformed", failure);
    }

    [Fact]
    public void ATruncatedOrTamperedCrlIsRejected()
    {
        using var pki = new TestPki();
        using var revoked = pki.Issue("probe-1");
        pki.Revoke(revoked);
        var crl = pki.BuildCrl();
        Assert.False(CrlParser.TryParse(crl[..^10], pki.Ca, out _, out _));
        var tampered = (byte[])crl.Clone();
        tampered[tampered.Length / 2] ^= 0x01;
        Assert.False(CrlParser.TryParse(tampered, pki.Ca, out _, out _));
    }
}

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Host.Security;

// A parsed and signature-verified certificate revocation list. RevokedSerials are upper-case hexadecimal without leading zero octets.
public sealed record CertificateRevocationList(string Issuer, DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate, IReadOnlySet<string> RevokedSerials);

// Reads an X.509 CRL (RFC 5280) and checks that the given CA issued and signed it. Anything it cannot fully verify is rejected with
// a stable failure code, so an unverifiable list can only close access, never relax it.
public static class CrlParser
{
    public static bool TryParse(ReadOnlySpan<byte> crl, X509Certificate2 issuer, out CertificateRevocationList? list, out string failure)
    {
        list = null;
        try
        {
            var outer = new AsnReader(crl.ToArray(), AsnEncodingRules.DER);
            var certificateList = outer.ReadSequence();
            outer.ThrowIfNotEmpty();
            var tbsEncoded = certificateList.PeekEncodedValue().ToArray();
            var tbs = certificateList.ReadSequence();
            var signatureAlgorithm = certificateList.ReadSequence();
            var algorithm = signatureAlgorithm.ReadObjectIdentifier();
            var signature = certificateList.ReadBitString(out var unusedBits);
            certificateList.ThrowIfNotEmpty();
            if (unusedBits != 0) { failure = "crl-malformed"; return false; }

            if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer)) tbs.ReadInteger(); // version
            tbs.ReadSequence(); // inner signature algorithm
            var issuerRaw = tbs.PeekEncodedValue().ToArray();
            tbs.ReadSequence();
            var thisUpdate = ReadTime(tbs);
            DateTimeOffset? nextUpdate = IsTime(tbs) ? ReadTime(tbs) : null;
            var revoked = new HashSet<string>(StringComparer.Ordinal);
            if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                var entries = tbs.ReadSequence();
                while (entries.HasData)
                {
                    var entry = entries.ReadSequence();
                    revoked.Add(Normalize(entry.ReadIntegerBytes().Span));
                }
            }
            if (!issuerRaw.AsSpan().SequenceEqual(issuer.SubjectName.RawData)) { failure = "crl-issuer-mismatch"; return false; }
            if (!VerifySignature(issuer, algorithm, tbsEncoded, signature, out failure)) return false;
            list = new CertificateRevocationList(new X500DistinguishedName(issuerRaw).Name, thisUpdate, nextUpdate, revoked);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is AsnContentException or CryptographicException or InvalidOperationException or ArgumentException or FormatException)
        {
            list = null;
            failure = "crl-malformed";
            return false;
        }
    }

    // Upper-case hexadecimal with the sign octet stripped, the same form used for certificate serial numbers.
    public static string Normalize(ReadOnlySpan<byte> serial)
    {
        var start = 0;
        while (start < serial.Length - 1 && serial[start] == 0) start++;
        return Convert.ToHexString(serial[start..]);
    }

    private static bool IsTime(AsnReader reader) => reader.HasData && (reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) || reader.PeekTag().HasSameClassAndValue(Asn1Tag.GeneralizedTime));

    private static DateTimeOffset ReadTime(AsnReader reader) => reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? reader.ReadUtcTime() : reader.ReadGeneralizedTime();

    private static bool VerifySignature(X509Certificate2 issuer, string algorithm, byte[] tbs, byte[] signature, out string failure)
    {
        failure = string.Empty;
        (HashAlgorithmName Hash, bool Rsa)? scheme = algorithm switch
        {
            "1.2.840.113549.1.1.11" => (HashAlgorithmName.SHA256, true),
            "1.2.840.113549.1.1.12" => (HashAlgorithmName.SHA384, true),
            "1.2.840.113549.1.1.13" => (HashAlgorithmName.SHA512, true),
            "1.2.840.10045.4.3.2" => (HashAlgorithmName.SHA256, false),
            "1.2.840.10045.4.3.3" => (HashAlgorithmName.SHA384, false),
            "1.2.840.10045.4.3.4" => (HashAlgorithmName.SHA512, false),
            _ => null
        };
        if (scheme is null) { failure = "crl-signature-unsupported"; return false; }
        bool valid;
        if (scheme.Value.Rsa)
        {
            using var rsa = issuer.GetRSAPublicKey();
            valid = rsa is not null && rsa.VerifyData(tbs, signature, scheme.Value.Hash, RSASignaturePadding.Pkcs1);
        }
        else
        {
            using var ecdsa = issuer.GetECDsaPublicKey();
            valid = ecdsa is not null && ecdsa.VerifyData(tbs, signature, scheme.Value.Hash, DSASignatureFormat.Rfc3279DerSequence);
        }
        if (!valid) failure = "crl-signature-invalid";
        return valid;
    }
}

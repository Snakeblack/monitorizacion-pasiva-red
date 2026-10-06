using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Host.Security;

public enum RevocationStatus { Good, Revoked, Unknown }

// Keeps the last verified CRL per issuing CA. Revocation is fail-closed: a certificate is only "Good" while a signed list that does not
// contain it is both recent (MaxRevocationAge since it was fetched) and unexpired (before its own NextUpdate).
public sealed class RevocationService(ICrlSource source, X509Certificate2Collection anchors, ProbeTrustOptions options, TimeProvider clock)
{
    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);

    private sealed record Snapshot(CertificateRevocationList List, DateTimeOffset FetchedAt);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var location in options.CrlLocations)
        {
            var content = await source.FetchAsync(location, cancellationToken).ConfigureAwait(false);
            if (content is null) continue;
            foreach (var anchor in anchors)
            {
                if (!CrlParser.TryParse(content, anchor, out var list, out _) || list is null) continue;
                Accept(ProbeCertificateValidator.IssuerKey(anchor.SubjectName), list);
                break;
            }
        }
    }

    private void Accept(string issuerKey, CertificateRevocationList list)
    {
        var now = clock.GetUtcNow();
        if (list.ThisUpdate > now + FutureSkew || (list.NextUpdate is { } next && next <= now)) return;
        _snapshots.AddOrUpdate(issuerKey, _ => new Snapshot(list, now), (_, current) =>
            list.ThisUpdate < current.List.ThisUpdate ? current : new Snapshot(list, now)); // an older list can never roll back newer revocations
    }

    public RevocationStatus Check(string issuerKey, string serialHex)
    {
        if (!_snapshots.TryGetValue(issuerKey, out var snapshot)) return RevocationStatus.Unknown;
        var now = clock.GetUtcNow();
        if (now - snapshot.FetchedAt >= options.MaxRevocationAge || (snapshot.List.NextUpdate is { } next && next <= now)) return RevocationStatus.Unknown;
        return snapshot.List.RevokedSerials.Contains(serialHex) ? RevocationStatus.Revoked : RevocationStatus.Good;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Host.Sessions;

internal enum CursorFailure { Malformed, Expired, Forbidden, Mismatch }

// Opaque, tamper-proof continuation token. It binds the caller, the effective authorized scope, the normalized
// filters, the page size, the snapshot position and an absolute deadline set on the first page and never extended.
internal sealed class SessionCursor(IDataProtectionProvider provider)
{
    private const int Version = 1;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = provider.CreateProtector("Monitoring.SessionSearch.Cursor.v1");

    private sealed record Payload(int V, string Subject, string Scope, string Query, int Page, string Snapshot, string StartedAt, string Key, long Expires);

    internal string Issue(string subject, IReadOnlyList<AuthorizedPair> authorized, string queryFingerprint, int pageSize,
        SessionSearchPosition position, DateTimeOffset expires) =>
        _protector.Protect(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(Version, subject, Hash(authorized),
            Hash(queryFingerprint), pageSize, position.SnapshotId, position.StartedAt, position.DocumentKey, expires.ToUnixTimeMilliseconds()))));

    internal bool TryOpen(string token, string subject, IReadOnlyList<AuthorizedPair> authorized, string queryFingerprint, int pageSize,
        DateTimeOffset now, out SessionSearchPosition? position, out DateTimeOffset expires, out CursorFailure failure)
    {
        position = null;
        expires = default;
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(_protector.Unprotect(token)));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            failure = CursorFailure.Malformed;
            return false;
        }
        if (payload is null || payload.V != Version || string.IsNullOrEmpty(payload.Snapshot) || string.IsNullOrEmpty(payload.Key))
        {
            failure = CursorFailure.Malformed;
            return false;
        }
        // Order matters: a different caller or scope is a permission failure and must not learn anything else about the cursor.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload.Subject), Encoding.UTF8.GetBytes(subject))
            || payload.Scope != Hash(authorized))
        {
            failure = CursorFailure.Forbidden;
            return false;
        }
        expires = DateTimeOffset.FromUnixTimeMilliseconds(payload.Expires);
        if (now >= expires)
        {
            failure = CursorFailure.Expired;
            return false;
        }
        if (payload.Query != Hash(queryFingerprint) || payload.Page != pageSize)
        {
            failure = CursorFailure.Mismatch;
            return false;
        }
        position = new SessionSearchPosition(payload.Snapshot, payload.StartedAt, payload.Key);
        failure = default;
        return true;
    }

    private static string Hash(IReadOnlyList<AuthorizedPair> authorized) =>
        Hash(string.Join('\n', authorized.OrderBy(pair => pair.SiteId, StringComparer.Ordinal).ThenBy(pair => pair.SensorId, StringComparer.Ordinal)
            .Select(pair => pair.SiteId + '\0' + pair.SensorId)));

    private static string Hash(string text) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

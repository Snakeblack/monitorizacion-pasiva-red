using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Host.Inventory;

internal enum InventoryCursorFailure { Malformed, Expired, Forbidden, Mismatch }

// Sealed continuation of an inventory listing: bound to the caller, the full authorized set, the listing and its normalized query,
// the page size and the keyset position, and valid for 10 minutes. The position itself is opaque to the client.
internal sealed class InventoryCursor(IDataProtectionProvider provider)
{
    private const int Version = 1;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = provider.CreateProtector("Monitoring.Inventory.Cursor.v1");

    private sealed record Payload(int V, string Subject, string Scope, string Query, int Page, string Position, long Expires);

    internal string Issue(string subject, IReadOnlyCollection<AuthorizedPair> authorized, string query, int pageSize, string position, DateTimeOffset now) =>
        _protector.Protect(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(Version, subject, Hash(authorized), Hash(query), pageSize,
            position, (now + Lifetime).ToUnixTimeMilliseconds()))));

    internal bool TryOpen(string token, string subject, IReadOnlyCollection<AuthorizedPair> authorized, string query, int pageSize, DateTimeOffset now,
        out string? position, out InventoryCursorFailure failure)
    {
        position = null;
        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(_protector.Unprotect(token))); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            failure = InventoryCursorFailure.Malformed;
            return false;
        }
        if (payload is null || payload.V != Version || string.IsNullOrEmpty(payload.Position)) { failure = InventoryCursorFailure.Malformed; return false; }
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload.Subject), Encoding.UTF8.GetBytes(subject)) || payload.Scope != Hash(authorized))
        {
            failure = InventoryCursorFailure.Forbidden;
            return false;
        }
        if (now >= DateTimeOffset.FromUnixTimeMilliseconds(payload.Expires)) { failure = InventoryCursorFailure.Expired; return false; }
        if (payload.Query != Hash(query) || payload.Page != pageSize) { failure = InventoryCursorFailure.Mismatch; return false; }
        position = payload.Position;
        failure = default;
        return true;
    }

    private static string Hash(IReadOnlyCollection<AuthorizedPair> authorized) =>
        Hash(string.Join('\n', authorized.OrderBy(pair => pair.SiteId, StringComparer.Ordinal).ThenBy(pair => pair.SensorId, StringComparer.Ordinal)
            .Select(pair => pair.SiteId + '\0' + pair.SensorId)));

    private static string Hash(string text) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

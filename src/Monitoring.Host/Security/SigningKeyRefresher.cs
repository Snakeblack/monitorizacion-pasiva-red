using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Monitoring.Host.Security;

// Re-reads the provider's signing keys when a token names a key the cache has not seen, which is what the first request after a rotation looks like
// (ADR-026). The configuration manager refreshes in the background and meanwhile keeps serving the old key set, so the first request would still be
// rejected; here that request waits, a few seconds at most, until the key appears.
//
// Bounded on purpose: the waiting is shared (concurrent requests ride on the same refresh) and a new refresh is not started within `floor` of the last
// one, so a stream of forged tokens naming unknown keys makes at most one request per floor wait, and the rest are rejected at once.
public sealed class SigningKeyRefresher(TimeSpan maximumWait)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private readonly object _gate = new();
    private Task<OpenIdConnectConfiguration?>? _current;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    // The key set after the refresh when it contains `keyId`; null when no refresh was started now or the key did not appear in time.
    public Task<OpenIdConnectConfiguration?> RefreshAsync(IConfigurationManager<OpenIdConnectConfiguration> manager, string? keyId, TimeSpan floor)
    {
        lock (_gate)
        {
            if (_current is { IsCompleted: false }) return _current;
            var now = DateTimeOffset.UtcNow;
            if (keyId is null || now - _last < floor) return Task.FromResult<OpenIdConnectConfiguration?>(null);
            _last = now;
            return _current = WaitForKeyAsync(manager, keyId);
        }
    }

    private async Task<OpenIdConnectConfiguration?> WaitForKeyAsync(IConfigurationManager<OpenIdConnectConfiguration> manager, string keyId)
    {
        using var timeout = new CancellationTokenSource(maximumWait);
        try
        {
            manager.RequestRefresh();
            while (true)
            {
                var configuration = await manager.GetConfigurationAsync(timeout.Token).ConfigureAwait(false);
                if (configuration.SigningKeys.Any(key => key.KeyId == keyId)) return configuration;
                await Task.Delay(PollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or HttpRequestException)
        {
            // Out of time, or the provider could not be reached: the keys already trusted stay as they were.
            return null;
        }
    }
}

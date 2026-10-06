using System.Net;
using System.Net.Http.Headers;

namespace Monitoring.Probe;

public sealed record DeliveryOptions(int MaximumAttemptsPerCycle = 5, TimeSpan? RequestTimeout = null);
public sealed class ProbeDelivery(HttpClient client, IProbeSpool spool, TimeProvider clock, DeliveryOptions options, Func<double>? random = null)
{
    private readonly SemaphoreSlim sender = new(1, 1);
    public async Task<bool> SendNextAsync(CancellationToken cancellationToken = default)
    {
        if (options.MaximumAttemptsPerCycle <= 0 || (options.RequestTimeout ?? TimeSpan.FromSeconds(10)) <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!await sender.WaitAsync(0, cancellationToken)) return false;
        try
        {
            var now = clock.GetUtcNow(); var batch = spool.Next(now); if (batch is null) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout ?? TimeSpan.FromSeconds(10));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingestion/batches") { Content = new ByteArrayContent(batch.Body) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    var first = new byte[1];
                    if (await stream.ReadAsync(first, timeout.Token) == 0) { spool.Acknowledge(batch); return true; }
                }
                var code = (int)response.StatusCode;
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - now : null);
                var delay = batch.Attempts % options.MaximumAttemptsPerCycle == options.MaximumAttemptsPerCycle - 1
                    ? TimeSpan.FromSeconds(60) : Backoff(batch.Attempts, (random ?? Random.Shared.NextDouble)(), retryAfter);
                spool.Fail(batch, clock.GetUtcNow() + delay, $"http-{code}", code is 400 or 409, code is 401 or 403);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { spool.Fail(batch, clock.GetUtcNow() + Backoff(batch.Attempts, (random ?? Random.Shared.NextDouble)()), "timeout"); }
            catch (HttpRequestException)
            { spool.Fail(batch, clock.GetUtcNow() + Backoff(batch.Attempts, (random ?? Random.Shared.NextDouble)()), "network"); }
            return true;
        }
        finally { sender.Release(); }
    }
    public static TimeSpan Backoff(int attempts, double jitter, TimeSpan? retryAfter = null)
    {
        if (attempts < 0 || double.IsNaN(jitter) || jitter is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        var seconds = Math.Pow(2, Math.Min(attempts, 6)) * (1 + .5 * jitter);
        if (retryAfter is { } after && after > TimeSpan.Zero) seconds = Math.Max(seconds, after.TotalSeconds);
        return TimeSpan.FromSeconds(Math.Min(60, seconds));
    }
}

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Repeats requests that failed on a server error or the rate limit — but only
/// when repeating is safe.
/// </summary>
/// <remarks>
/// <para>
/// The important part is <em>when it does not</em> retry. A 5xx can arrive after
/// the server has already applied the change: the response was lost, the order
/// was not. A second attempt would place it again. This handler therefore only
/// repeats methods that are inherently idempotent — <c>GET</c>, <c>PUT</c>,
/// <c>DELETE</c>, <c>HEAD</c>, <c>OPTIONS</c> — plus <c>POST</c> and
/// <c>PATCH</c> exactly when the call declared itself repeatable.
/// </para>
/// <para>
/// Network failures are not retried. On a dropped connection there is no way to
/// tell whether the request reached the server — the same reasoning as for the
/// 5xx, only without any evidence at all.
/// </para>
/// </remarks>
internal sealed class EmporixRetryHandler : DelegatingHandler
{
    private readonly EmporixOptions _options;
    private readonly ILogger<EmporixRetryHandler> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EmporixRetryHandler(
        IOptions<EmporixOptions> options,
        ILogger<EmporixRetryHandler> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _delay = delay ?? Task.Delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        int maxAttempts = _options.Retry.MaxAttempts;
        bool mayRetry = IsIdempotent(request)
            && await ReplayableContent.TryPrepareAsync(request, cancellationToken).ConfigureAwait(false);

        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!mayRetry || attempt >= maxAttempts || !IsRetryable(response.StatusCode))
            {
                return response;
            }

            TimeSpan wait = CalculateDelay(response, attempt);

            Log.RetryingRequest(
                _logger,
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                (int)response.StatusCode,
                attempt,
                wait.TotalMilliseconds);

            // The response is discarded; its body is never read.
            response.Dispose();

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static bool IsIdempotent(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Delete
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Options)
        {
            return true;
        }

        return request.Options.TryGetValue(EmporixRequestOptions.Idempotent, out bool idempotent)
            && idempotent;
    }

    /// <summary>
    /// Determines the wait: the server's own instruction takes precedence,
    /// otherwise an exponential backoff with jitter.
    /// </summary>
    private TimeSpan CalculateDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan maxBackoff = _options.Retry.MaxBackoff;

        if (ReadRetryAfter(response) is { } retryAfter)
        {
            // Capped: a server asking for an hour must not stall a call for an
            // hour. The uncapped value reaches the caller through
            // EmporixRateLimitException.RetryAfter.
            return retryAfter > maxBackoff ? maxBackoff : retryAfter;
        }

        // 1s, 2s, 4s … up to the ceiling, plus a little jitter so calls that
        // failed together do not return in lockstep.
        double exponential = Math.Min(
            1000d * Math.Pow(2, attempt - 1),
            maxBackoff.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(exponential + Random.Shared.Next(0, 100));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } header)
        {
            return null;
        }

        // The header comes in two shapes: a number of seconds, or a point in time.
        if (header.Delta is { } delta)
        {
            return delta >= TimeSpan.Zero ? delta : null;
        }

        if (header.Date is { } date)
        {
            TimeSpan until = date - DateTimeOffset.UtcNow;
            return until > TimeSpan.Zero ? until : TimeSpan.Zero;
        }

        return null;
    }
}

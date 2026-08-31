namespace Viu.Emporix;

/// <summary>
/// Supplies a fresh customer token when Emporix rejects the current one.
/// </summary>
/// <remarks>
/// Without a registered implementation the SDK forwards a 401 on a customer
/// token unchanged as an <see cref="EmporixAuthenticationException"/> — the
/// token belongs to the caller, and the SDK does not renew it uninvited.
/// Register an implementation in the DI container to change that.
/// </remarks>
public interface ICustomerTokenRefresher
{
    /// <summary>
    /// Returns a fresh customer token, or <see langword="null"/> when no renewal
    /// is possible.
    /// </summary>
    /// <param name="expiredToken">The token Emporix rejected.</param>
    /// <param name="cancellationToken">Cancels the renewal.</param>
    /// <returns>
    /// The new token, or <see langword="null"/>. On <see langword="null"/> the
    /// original 401 is passed to the caller.
    /// </returns>
    /// <remarks>
    /// The SDK never calls this concurrently for the same wave of requests:
    /// Emporix rotates the refresh token on every renewal, so two parallel
    /// renewals would invalidate each other.
    /// </remarks>
    ValueTask<string?> RefreshAsync(string expiredToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs at most one renewal per rejected token and shares its result with all
/// waiting callers.
/// </summary>
/// <remarks>
/// The reason is correctness, not thrift: Emporix rotates the refresh token on
/// every renewal. If two ran at once, the second would work with an
/// already-consumed refresh token and invalidate the first renewal.
/// </remarks>
internal sealed class CustomerTokenRefreshCoordinator : IDisposable
{
    private readonly ICustomerTokenRefresher? _refresher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _lastRefreshedFrom;
    private string? _lastResult;
    private bool _disposed;

    /// <param name="refresher">
    /// The renewal, or <see langword="null"/> when none is configured. Without
    /// one this coordinator simply reports that nothing can be done — so the
    /// registration need not distinguish two cases.
    /// </param>
    public CustomerTokenRefreshCoordinator(ICustomerTokenRefresher? refresher)
        => _refresher = refresher;

    /// <summary>Whether a renewal is configured at all.</summary>
    public bool IsEnabled => _refresher is not null;

    /// <summary>
    /// Renews <paramref name="expiredToken"/>, or returns the result of a
    /// renewal of the same token that just completed.
    /// </summary>
    public async ValueTask<string?> RefreshAsync(string expiredToken, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_refresher is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Anyone arriving with the same rejected token during a renewal gets
            // its result. Otherwise every waiting request would trigger another
            // renewal and invalidate the previous one.
            if (_lastResult is not null
                && string.Equals(_lastRefreshedFrom, expiredToken, StringComparison.Ordinal))
            {
                return _lastResult;
            }

            string? refreshed = await _refresher.RefreshAsync(expiredToken, cancellationToken)
                .ConfigureAwait(false);

            _lastRefreshedFrom = expiredToken;
            _lastResult = refreshed;

            return refreshed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}

namespace Viu.Emporix;

/// <summary>
/// Makes sure a request body can be sent a second time.
/// </summary>
/// <remarks>
/// A body that has been sent cannot be read again. Both handlers that may repeat
/// a request — retry after a server error, and re-authentication after a 401 —
/// need the same precaution, which is why it lives here.
/// </remarks>
internal static class ReplayableContent
{
    /// <summary>
    /// Upper bound up to which a request body is buffered.
    /// </summary>
    /// <remarks>
    /// JSON bodies sit far below this. A large media upload is not buffered and
    /// therefore not repeated — holding it entirely in memory for an unlikely
    /// case would be the worse trade.
    /// </remarks>
    public const long MaxBufferedBytes = 1024 * 1024;

    /// <summary>
    /// Ensures <paramref name="request"/> can be sent again, and reports whether
    /// that succeeded.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the request may be repeated.
    /// </returns>
    public static async ValueTask<bool> TryPrepareAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return true;
        }

        // Only buffer at a known and small length. At an unknown length, reading
        // would consume a stream that then could not be sent — the attempt to
        // enable a repeat would destroy the request.
        if (request.Content.Headers.ContentLength is not long length || length > MaxBufferedBytes)
        {
            return false;
        }

        // Calling this more than once does no harm: after the first time the
        // body sits in memory and the call is a no-op.
        await request.Content.LoadIntoBufferAsync(MaxBufferedBytes, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}

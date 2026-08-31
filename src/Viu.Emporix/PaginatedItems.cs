using System.Runtime.CompilerServices;

namespace Viu.Emporix;

/// <summary>
/// One page of a result list together with the information Emporix supplies in
/// the response headers.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class PaginatedItems<T>
{
    internal PaginatedItems(
        IReadOnlyList<T> items,
        int pageNumber,
        int pageSize,
        bool hasNextPage,
        int? totalCount = null,
        string? nextCursor = null,
        string? previousCursor = null)
    {
        Items = items;
        PageNumber = pageNumber;
        PageSize = pageSize;
        HasNextPage = hasNextPage;
        TotalCount = totalCount;
        NextCursor = nextCursor;
        PreviousCursor = previousCursor;
    }

    /// <summary>The items on this page. Empty when there are none.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>The number of this page. Emporix counts from 1.</summary>
    public int PageNumber { get; }

    /// <summary>The requested page size.</summary>
    public int PageSize { get; }

    /// <summary>
    /// Whether another page exists.
    /// </summary>
    /// <remarks>
    /// Emporix does not answer this directly; the value is derived from whatever
    /// the response offers, in this order: a cursor for the next page, otherwise
    /// the total count, otherwise a full page. The last step is wrong in one
    /// case: if the final page holds exactly <see cref="PageSize"/> items, it
    /// reports another page, which then comes back empty.
    /// </remarks>
    public bool HasNextPage { get; }

    /// <summary>
    /// The total number of matches, when the call asked for it and the endpoint
    /// supplies it.
    /// </summary>
    /// <remarks>
    /// Only determined on explicit request: Emporix needs a second query for it,
    /// and that should not be incurred by every list.
    /// </remarks>
    public int? TotalCount { get; }

    /// <summary>
    /// The cursor for the next page, where the endpoint supplies one.
    /// </summary>
    /// <remarks>
    /// Very few endpoints work with cursors. Its absence means «this endpoint
    /// has no cursors» — not «last page».
    /// </remarks>
    public string? NextCursor { get; }

    /// <summary>The cursor for the previous page. Same caveat as <see cref="NextCursor"/>.</summary>
    public string? PreviousCursor { get; }
}

/// <summary>Helpers for page-by-page result lists.</summary>
public static class PaginatedItems
{
    /// <summary>
    /// Walks every item across all pages, fetching each page only when it is needed.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="fetchPage">Fetches the page with the given number.</param>
    /// <param name="startPage">The first page. Emporix counts from 1.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fetchPage"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startPage"/> is less than 1.</exception>
    /// <remarks>
    /// Termination follows <see cref="PaginatedItems{T}.HasNextPage"/>. Where
    /// that value is guessed from a full page, one extra empty request may
    /// occur — which then ends the walk.
    /// </remarks>
    public static IAsyncEnumerable<T> EnumerateAllAsync<T>(
        Func<int, CancellationToken, Task<PaginatedItems<T>>> fetchPage,
        int startPage = 1,
        CancellationToken cancellationToken = default)
    {
        // The checks sit outside the iterator on purpose: the body of an
        // iterator method only starts running on the first step, so a bad
        // argument would surface far away from where it came from.
        ArgumentNullException.ThrowIfNull(fetchPage);
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 1);

        return Iterate(cancellationToken);

        async IAsyncEnumerable<T> Iterate(
            [EnumeratorCancellation] CancellationToken enumeratorToken)
        {
            // Two possible sources: the token passed here and one from
            // WithCancellation on the walk. Both should take effect.
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumeratorToken);

            for (int page = startPage; ; page++)
            {
                PaginatedItems<T> current = await fetchPage(page, linked.Token).ConfigureAwait(false);

                foreach (T item in current.Items)
                {
                    yield return item;
                }

                // Stopping on an empty page guards against an endpoint that
                // stubbornly keeps reporting more.
                if (!current.HasNextPage || current.Items.Count == 0)
                {
                    yield break;
                }
            }
        }
    }
}

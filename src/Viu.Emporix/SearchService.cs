using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.SearchServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Search over a tenant's custom entities: the search itself, saved searches,
/// and the indexes it runs on.
/// </summary>
/// <remarks>
/// <para>
/// A preview service at Emporix, introduced on 2026-09-28 and still changing
/// from week to week. Everything that belongs to one custom schema type is
/// reached through <c>ForType</c>; the lists here span every type.
/// </para>
/// <para>
/// An index builds asynchronously. Creating, changing or deleting one answers
/// with a job id, which <see cref="GetJobAsync"/> reads; wrap it in
/// <see cref="EmporixPolling.WaitForAsync"/> to wait for the job to finish.
/// </para>
/// </remarks>
public sealed class SearchService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SearchService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/search/{_tenant}";

    /// <summary>Lists the saved searches of every type.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SavedQuery>> ListQueriesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/search/queries",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSavedQuery,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the search indexes of every type.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<SearchIndex>> ListIndexesAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/search/indexes",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSearchIndex,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Lists the jobs that build and delete indexes.</summary>
    /// <param name="query">An Emporix <c>q</c> filter, or nothing.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Jobs cannot be deleted; the list keeps every one.</remarks>
    public async Task<PaginatedItems<IndexJob>> ListJobsAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/jobs",
                Auth = Defaults.Service(auth),
                Query = Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListIndexJob,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one index job.</summary>
    /// <param name="jobId">The job id an index write answered with.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IndexJob?> GetJobAsync(
        string jobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/jobs/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.IndexJob,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The page parameters and an optional <c>q</c> filter every list here takes.</summary>
    internal static List<KeyValuePair<string, string?>> Paging(string? query, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (query is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("q", query));
        }

        return parameters;
    }
}

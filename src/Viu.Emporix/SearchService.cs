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
/// reached through <see cref="ForType"/>; the lists here span every type.
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

    /// <summary>Search, saved searches and indexes of one custom schema type.</summary>
    /// <param name="type">The custom schema type, such as <c>PET</c>.</param>
    public SearchTypeOperations ForType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new SearchTypeOperations(_http, $"/search/{_tenant}/search/{Uri.EscapeDataString(type)}");
    }

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

/// <summary>Search, saved searches and indexes of one custom schema type.</summary>
/// <remarks>Reached through <see cref="SearchService.ForType"/>.</remarks>
public sealed class SearchTypeOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal SearchTypeOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Searches the documents of this type.</summary>
    /// <param name="request">The index, and the queries, the filters or both.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="sort">A sort expression such as <c>name:ASC</c>, or nothing for relevance.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// A filter's <c>Value</c> is generated as <c>object</c> and goes out only as a
    /// <see cref="string"/>, <see cref="int"/>, <see cref="long"/>, <see cref="double"/>,
    /// <see cref="decimal"/>, <see cref="bool"/>, <see cref="DateTimeOffset"/>, an array
    /// of any of these but <see cref="bool"/>, or a <see cref="System.Text.Json.JsonElement"/>.
    /// Any other type throws <see cref="NotSupportedException"/> before the request leaves.
    /// </para>
    /// <para>
    /// Field paths are the stored ones — <c>name.en</c>, not <c>name</c> — and a
    /// filtered field must be indexed with <c>Exact</c>. Sent as a <c>POST</c>
    /// because the criteria are a body, but declared repeatable: it only reads.
    /// </para>
    /// </remarks>
    public async Task<PaginatedItems<SearchHit>> SearchAsync(
        SearchRequest request,
        int pageNumber = 1,
        int pageSize = 60,
        string? sort = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<KeyValuePair<string, string?>> query = SearchService.Paging(null, pageNumber, pageSize);

        if (sort is { Length: > 0 })
        {
            query.Add(new KeyValuePair<string, string?>("sort", sort));
        }

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = query,
                Content = EmporixJsonContent.Create(request, SearchJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            SearchJsonContext.Default.ListSearchHit,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a saved search with a text of its own.</summary>
    /// <param name="savedSearchId">The saved search.</param>
    /// <param name="text">The text applied to every search term in it; not blank.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The saved search supplies the index, the queries and the filters.</remarks>
    public async Task<PaginatedItems<SearchHit>> RunSavedSearchAsync(
        string savedSearchId,
        string text,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(null, pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SavedSearchCall { SearchQueryId = savedSearchId, Query = text },
                    SearchJsonContext.Default.SavedSearchCall),
                Idempotent = true,
            },
            SearchJsonContext.Default.ListSearchHit,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists this type's saved searches.</summary>
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
                Path = $"{_basePath}/queries",
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSavedQuery,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one saved search.</summary>
    /// <param name="savedSearchId">The saved search id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The request's <c>index</c> comes back as <c>IndexId</c>, and a list of queries as one <c>or</c> group.</remarks>
    public async Task<SavedQuery?> GetQueryAsync(
        string savedSearchId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.SavedQuery,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or replaces a saved search.</summary>
    /// <param name="savedSearchId">The id: 1 to 66 letters, digits, underscores or hyphens.</param>
    /// <param name="savedSearch">The definition; its index must exist.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The id when Emporix created the saved search; <see langword="null"/> when it
    /// replaced one, which it answers with <c>204</c>.
    /// </returns>
    /// <remarks>
    /// Replacing needs <c>Metadata.Version</c> set to the stored version: without one
    /// Emporix answers <c>400</c>, with a stale one <c>409</c>.
    /// </remarks>
    public async Task<SavedQueryId?> UpsertQueryAsync(
        string savedSearchId,
        SavedQueryRequest savedSearch,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);
        ArgumentNullException.ThrowIfNull(savedSearch);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(savedSearch, SearchJsonContext.Default.SavedQueryRequest),
            },
            SearchJsonContext.Default.SavedQueryId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a saved search.</summary>
    /// <param name="savedSearchId">The saved search id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteQueryAsync(
        string savedSearchId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedSearchId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/queries/{Uri.EscapeDataString(savedSearchId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists this type's search indexes.</summary>
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
                Path = $"{_basePath}/indexes",
                Auth = Defaults.Service(auth),
                Query = SearchService.Paging(query, pageNumber, pageSize),
            },
            SearchJsonContext.Default.ListSearchIndex,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Reads one search index.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<SearchIndex?> GetIndexAsync(
        string indexId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.SearchIndex,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates or changes a search index, which then builds asynchronously.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="index">The definition: at least one field.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The job that builds the index — read it with <see cref="SearchService.GetJobAsync"/>
    /// — or <see langword="null"/> when Emporix answers <c>204</c> without one.
    /// </returns>
    /// <remarks>
    /// A string field needs <c>Text</c>, <c>Autocomplete</c> or <c>Exact</c>; number,
    /// boolean and date fields take only <c>Exact</c>. Changing an existing index
    /// needs <c>Metadata.Version</c>. A retry after a timeout may meet the version
    /// the first attempt already moved, and answer <c>409</c>.
    /// </remarks>
    public async Task<JobId?> UpsertIndexAsync(
        string indexId,
        IndexRequest index,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);
        ArgumentNullException.ThrowIfNull(index);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(index, SearchJsonContext.Default.IndexRequest),
            },
            SearchJsonContext.Default.JobId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a search index, asynchronously.</summary>
    /// <param name="indexId">The index id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The job that deletes the index — read it with <see cref="SearchService.GetJobAsync"/>.</returns>
    public async Task<JobId?> DeleteIndexAsync(
        string indexId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/indexes/{Uri.EscapeDataString(indexId)}",
                Auth = Defaults.Service(auth),
            },
            SearchJsonContext.Default.JobId,
            cancellationToken).ConfigureAwait(false);
    }
}

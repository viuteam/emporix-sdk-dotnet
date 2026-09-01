using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.IndexingServiceModels;

namespace Viu.Emporix;

/// <summary>
/// Search indexing — which provider indexes the catalogue, and rebuilding it.
/// </summary>
/// <remarks>
/// <para>
/// Two providers are supported, Algolia and Battery Included, and only one is
/// active per tenant at a time. The provider name <em>is</em> the configuration
/// id.
/// </para>
/// <para>
/// The configuration comes in two flavours. The public one carries the search
/// key and is readable with any token; the full one also carries the write key
/// and needs <c>indexing.search_view</c>. A storefront wants
/// <see cref="GetPublicConfigurationAsync"/> — handing a browser a write key
/// would let anyone rewrite the index.
/// </para>
/// </remarks>
public sealed class IndexingService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal IndexingService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/indexing/{_tenant}";

    /// <summary>Adds an index provider.</summary>
    /// <param name="configuration">The provider, its keys and its index name.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new configuration's id, which is the provider name.</returns>
    public async Task<IndexCreationResponse?> CreateConfigurationAsync(
        IndexConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/configurations",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration, IndexingJsonContext.Default.IndexConfiguration),
            },
            IndexingJsonContext.Default.IndexCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists every index configuration, write keys included.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The response contains credentials. Do not pass it to a browser — that is
    /// what <see cref="ListPublicConfigurationsAsync"/> is for.
    /// </remarks>
    public async Task<IReadOnlyList<IndexConfiguration>> ListConfigurationsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configurations",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            IndexingJsonContext.Default.ListIndexConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one provider's configuration, write key included.</summary>
    /// <param name="provider">The provider name — <c>ALGOLIA</c> or <c>BATTERY_INCLUDED</c>.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IndexConfiguration?> GetConfigurationAsync(
        string provider,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/configurations/{Uri.EscapeDataString(provider)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            IndexingJsonContext.Default.IndexConfiguration,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces one provider's configuration.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="configuration">The configuration to store.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateConfigurationAsync(
        string provider,
        IndexConfiguration configuration,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(configuration);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/configurations/{Uri.EscapeDataString(provider)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    configuration, IndexingJsonContext.Default.IndexConfiguration),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Removes a provider's configuration.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteConfigurationAsync(
        string provider,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/configurations/{Uri.EscapeDataString(provider)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            cancellationToken);
    }

    /// <summary>Lists the index configurations without their write keys.</summary>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// What a storefront needs to query the index directly: the search key and
    /// the index name, and nothing that can write.
    /// </remarks>
    public async Task<IReadOnlyList<IndexPublicConfiguration>> ListPublicConfigurationsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/public/configurations",
                Auth = Defaults.Anonymous(auth),
                Idempotent = true,
            },
            IndexingJsonContext.Default.ListIndexPublicConfiguration,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Fetches one provider's configuration without its write key.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IndexPublicConfiguration?> GetPublicConfigurationAsync(
        string provider,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/public/configurations/{Uri.EscapeDataString(provider)}",
                Auth = Defaults.Anonymous(auth),
                Idempotent = true,
            },
            IndexingJsonContext.Default.IndexPublicConfiguration,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reindexes everything.</summary>
    /// <param name="request">Which mode to reindex in.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Answers <c>204</c> and reports nothing further: this call gives back no
    /// job to follow. <see cref="StartReindexJobAsync"/> does, and is the one to
    /// use when the outcome matters.
    /// </remarks>
    public Task ReindexAsync(
        Reindex request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/reindex",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(request, IndexingJsonContext.Default.Reindex),
            },
            cancellationToken);
    }

    /// <summary>Starts a reindex job for one entity type.</summary>
    /// <param name="request">The entity type, and whether to build RAG data too.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The job. <c>201</c> when it was created, <c>200</c> when an equivalent job
    /// was already running — both give back the job, so the caller sees the same
    /// thing either way.
    /// </returns>
    /// <remarks>
    /// Not repeatable. Wait for it with
    /// <see cref="EmporixPolling.WaitForAsync"/> over
    /// <see cref="GetReindexJobAsync"/>.
    /// </remarks>
    public async Task<ReindexJob?> StartReindexJobAsync(
        ReindexRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/reindex-jobs",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request, IndexingJsonContext.Default.ReindexRequest),
            },
            IndexingJsonContext.Default.ReindexJob,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists reindex jobs.</summary>
    /// <param name="query">A standard <c>q</c> filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size, at most 2000.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ReindexJob>> ListReindexJobsAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 2000);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add(new("q", query));
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/reindex-jobs",
                Auth = Defaults.Service(auth),
                Query = parameters,
                Idempotent = true,
            },
            IndexingJsonContext.Default.ListReindexJob,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads how a reindex job is going.</summary>
    /// <param name="reindexJobId">The job id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The call to poll. <c>ProcessedCount</c> against <c>ExpectedCount</c> is
    /// the progress.
    /// </remarks>
    public async Task<ReindexJob?> GetReindexJobAsync(
        string reindexJobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reindexJobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/reindex-jobs/{Uri.EscapeDataString(reindexJobId)}",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            IndexingJsonContext.Default.ReindexJob,
            cancellationToken).ConfigureAwait(false);
    }
}

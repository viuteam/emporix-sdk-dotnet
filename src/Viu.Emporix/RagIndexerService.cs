using Microsoft.Extensions.Options;
using Viu.Emporix.AiRagIndexerModels;

namespace Viu.Emporix;

/// <summary>
/// The RAG index — what an agent can retrieve over.
/// </summary>
/// <remarks>
/// <para>
/// Feeds the retrieval tool built by
/// <see cref="AiToolOperations.ReplaceRagEmporixAsync"/>. Indexing happens on
/// its own; this is for rebuilding it and for finding out what can be filtered
/// on.
/// </para>
/// <para>
/// Every call names an entity type. <c>ORDER</c> and <c>PRODUCT</c> are built
/// in; a tenant's own custom entity types work too.
/// </para>
/// </remarks>
public sealed class RagIndexerService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal RagIndexerService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/ai-rag-indexer/{_tenant}";

    /// <summary>Rebuilds the index for one entity type.</summary>
    /// <param name="type">
    /// The entity type — <c>ORDER</c>, <c>PRODUCT</c>, or one of your own.
    /// </param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Answers <c>204</c> and reports no job: this starts the work and tells you
    /// nothing further about it.
    /// </remarks>
    public Task ReindexAsync(
        string type,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(type)}/reindex",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the metadata fields the index holds for an entity type.</summary>
    /// <param name="type">The entity type.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<string>> GetRagMetadataAsync(
        string type,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(type)}/rag-metadata",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            RagIndexerJsonContext.Default.ListString,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Lists what a retrieval query can filter on.</summary>
    /// <param name="type">The entity type.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Each entry carries its key, a readable name, and the type it holds.</remarks>
    public async Task<IReadOnlyList<MetadataFilter>> GetFilterMetadataAsync(
        string type,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(type)}/filter-metadata",
                Auth = Defaults.Service(auth),
                Idempotent = true,
            },
            RagIndexerJsonContext.Default.ListMetadataFilter,
            cancellationToken).ConfigureAwait(false) ?? [];
    }
}

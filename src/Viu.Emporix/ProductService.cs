using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix;

/// <summary>Fine-tuning for the product write calls.</summary>
public sealed class ProductWriteOptions
{
    /// <summary>Suppresses automatic variant generation.</summary>
    public bool? SkipVariantGeneration { get; init; }

    /// <summary>Controls whether the product is reindexed immediately.</summary>
    public bool? DoIndex { get; init; }

    /// <summary>Skips validation of related items.</summary>
    public bool? SkipRelatedItemsValidation { get; init; }
}

/// <summary>Paging options for the list and search calls.</summary>
public sealed class ProductPageOptions
{
    /// <summary>The page number. Emporix counts from 1.</summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// The page size. Defaults to 60, as stated in the Emporix specification.
    /// </summary>
    /// <remarks>
    /// The Node SDK uses 50 here. We follow the specification; set the value
    /// explicitly if you depend on matching that behaviour.
    /// </remarks>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>
    /// Requests the total number of matches.
    /// </summary>
    /// <remarks>
    /// Off by default: Emporix determines the count with a second query, and
    /// that should not be incurred by every list.
    /// </remarks>
    public bool IncludeTotalCount { get; init; }

    /// <summary>The page size Emporix applies when none is given.</summary>
    internal const int DefaultPageSize = 60;
}

/// <summary>
/// The Emporix product catalog.
/// </summary>
/// <remarks>
/// Read calls default to an anonymous token; pass a customer token for
/// personalised prices. Write calls require a service token and do not belong in
/// a client application.
/// </remarks>
public sealed partial class ProductService
{
    private readonly EmporixHttpClient _http;
    private readonly ILogger<ProductService> _logger;
    private readonly string _tenant;

    internal ProductService(
        EmporixHttpClient http,
        IOptions<EmporixOptions> options,
        ILogger<ProductService> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _logger = logger;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/product/{_tenant}/products";

    /// <summary>Reads that resolve the product type instead of assuming BASIC.</summary>
    /// <remarks>
    /// The methods on this service return <see cref="BasicProductWithId"/> for
    /// every product, because the specification declares a product read as a
    /// <c>oneOf</c> over five schemas with no discriminator. That is right for a
    /// catalogue of plain products and wrong the moment a bundle or a variant
    /// appears. These reads return whichever shape <c>productType</c> names.
    /// </remarks>
    public ProductAnyTypeOperations AnyType => new(this);

    // ----- Reads -----

    /// <summary>Fetches a product by its id.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No product exists with this id.</exception>
    /// <exception cref="EmporixTransportException">The request did not reach Emporix.</exception>
    public Task<BasicProductWithId?> GetAsync(
        string productId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => GetOneCoreAsync(productId, ProductJsonContext.Default.BasicProductWithId, auth, cancellationToken);

    // The path and the auth default live here once. ProductAnyTypeOperations
    // passes a different JsonTypeInfo and gets the resolving read for free.
    internal async Task<T?> GetOneCoreAsync<T>(
        string productId,
        JsonTypeInfo<T> typeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Anonymous(auth),
            },
            typeInfo,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a product by its code.</summary>
    /// <param name="code">The product code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The product, or <see langword="null"/> when no product carries this code.</returns>
    public Task<BasicProductWithId?> GetByCodeAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => GetOneByCodeCoreAsync(code, ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);

    internal async Task<T?> GetOneByCodeCoreAsync<T>(
        string code,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        List<T>? matches = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Anonymous(auth),
                Query = [new("q", $"code:{code}")],
            },
            listTypeInfo,
            cancellationToken).ConfigureAwait(false);

        // «default» rather than «null»: T is unconstrained, so null does not
        // compile here.
        return matches is { Count: > 0 } ? matches[0] : default;
    }

    /// <summary>Fetches one page of the catalog.</summary>
    /// <param name="options">Page number, page size and total count.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<BasicProductWithId>> ListAsync(
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => ListPageAsync(
            options ?? new ProductPageOptions(),
            query: null,
            ProductJsonContext.Default.ListBasicProductWithId,
            auth,
            cancellationToken);

    /// <summary>
    /// Walks the entire catalog, fetching each page only when it is needed.
    /// </summary>
    /// <param name="pageSize">The page size; 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public IAsyncEnumerable<BasicProductWithId> ListAllAsync(
        int? pageSize = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => PaginatedItems.EnumerateAllAsync(
            (pageNumber, token) => ListAsync(
                new ProductPageOptions
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize ?? ProductPageOptions.DefaultPageSize,
                },
                auth,
                token),
            startPage: 1,
            cancellationToken);

    /// <summary>
    /// Searches products with an Emporix filter.
    /// </summary>
    /// <param name="query">
    /// The filter in Emporix' query language, for example <c>code:ABC</c> or
    /// <c>productType:VARIANT</c>. Space-separated terms are combined with AND.
    /// </param>
    /// <param name="options">Page number, page size and total count.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">Emporix rejected the filter.</exception>
    public Task<PaginatedItems<BasicProductWithId>> SearchAsync(
        string query,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return ListPageAsync(
            options ?? new ProductPageOptions(),
            query,
            ProductJsonContext.Default.ListBasicProductWithId,
            auth,
            cancellationToken);
    }

    /// <summary>
    /// Searches products by part of their name.
    /// </summary>
    /// <param name="term">The text to look for, as somebody would type it.</param>
    /// <param name="options">Page number, page size and total count.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The product filter expects <c>field:value</c>; a bare word is rejected.
    /// This method builds a name filter from the text and neutralises it so it
    /// passes as a regular expression.
    /// <para>
    /// Parentheses and quotes are replaced by spaces rather than removed, so
    /// «Access(instant)» does not run together into a single word. A text with
    /// nothing left produces an empty page — with no request, because an empty
    /// name filter would be rejected.
    /// </para>
    /// </remarks>
    public Task<PaginatedItems<BasicProductWithId>> SearchByNameAsync(
        string term,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => SearchByNameCoreAsync(
            term, options, ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);

    internal Task<PaginatedItems<T>> SearchByNameCoreAsync<T>(
        string term,
        ProductPageOptions? options,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ProductPageOptions page = options ?? new ProductPageOptions();

        // Replace rather than drop, then collapse runs of whitespace: two spaces
        // would be two literal spaces in the regular expression and match nothing.
        string cleaned = CollapseWhitespace()
            .Replace(QueryBreakingCharacters().Replace(term ?? string.Empty, " "), " ")
            .Trim();

        if (cleaned.Length == 0)
        {
            return Task.FromResult(new PaginatedItems<T>(
                [],
                page.PageNumber,
                page.PageSize,
                hasNextPage: false));
        }

        // Straight to the paged read rather than through the public SearchAsync,
        // which is not generic. Its only extra work is rejecting a blank query,
        // and the built filter can never be blank at this point.
        return ListPageAsync(
            page,
            $"name:(~{EscapeRegexMetacharacters(cleaned)})",
            listTypeInfo,
            auth,
            cancellationToken);
    }

    /// <summary>
    /// Fetches several products by their ids.
    /// </summary>
    /// <param name="productIds">The ids. An empty list causes no request.</param>
    /// <param name="chunkSize">How many ids per request; 100 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The result order does <em>not</em> match the order of the ids: beyond
    /// <paramref name="chunkSize"/> entries several requests run. Index by id if
    /// the mapping matters.
    /// </remarks>
    public Task<IReadOnlyList<BasicProductWithId>> GetManyByIdAsync(
        IReadOnlyCollection<string> productIds,
        int chunkSize = DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return SearchInChunksAsync(
            "id", productIds, chunkSize,
            ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);
    }

    /// <summary>
    /// Fetches several products by their codes.
    /// </summary>
    /// <param name="codes">The product codes. Duplicates are collapsed.</param>
    /// <param name="chunkSize">How many codes per request; 100 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Codes containing parentheses, commas, quotes or whitespace are skipped and
    /// logged: Emporix' query language uses those characters as delimiters and
    /// cannot escape them inside a list. The result order is not guaranteed, as
    /// with <see cref="GetManyByIdAsync"/>.
    /// </remarks>
    public Task<IReadOnlyList<BasicProductWithId>> GetManyByCodeAsync(
        IReadOnlyCollection<string> codes,
        int chunkSize = DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => GetManyByCodeCoreAsync(
            codes, chunkSize, ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);

    /// <summary>
    /// Collapses duplicates, drops codes the query language cannot carry, and
    /// chunks the rest.
    /// </summary>
    /// <remarks>
    /// A core rather than logic in the public method, so the resolving read on
    /// <c>ProductAnyTypeOperations</c> filters identically. Reaching
    /// <see cref="SearchInChunksAsync"/> directly would send a code containing a
    /// comma into the filter, where it breaks the value list apart — the one
    /// path this method exists to prevent.
    /// </remarks>
    internal Task<IReadOnlyList<T>> GetManyByCodeCoreAsync<T>(
        IReadOnlyCollection<string> codes,
        int chunkSize,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);

        List<string> distinct = [.. codes.Distinct(StringComparer.Ordinal)];
        List<string> usable = [.. distinct.Where(code => !QueryDelimiters().IsMatch(code))];

        if (usable.Count < distinct.Count)
        {
            Log.DroppedCodesWithDelimiters(_logger, distinct.Count - usable.Count);
        }

        return SearchInChunksAsync("code", usable, chunkSize, listTypeInfo, auth, cancellationToken);
    }

    /// <summary>
    /// Walks the variants of a parent-variant product.
    /// </summary>
    /// <param name="parentVariantId">The id of the parent product.</param>
    /// <param name="pageSize">The page size; 200 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public IAsyncEnumerable<BasicProductWithId> ListVariantsAsync(
        string parentVariantId,
        int pageSize = DefaultVariantPageSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentVariantId);

        return PaginatedItems.EnumerateAllAsync(
            (pageNumber, token) => SearchAsync(
                VariantsQuery(parentVariantId),
                new ProductPageOptions { PageNumber = pageNumber, PageSize = pageSize },
                auth,
                token),
            startPage: 1,
            cancellationToken);
    }

    /// <summary>The page size the variant walk uses when none is given.</summary>
    /// <remarks>
    /// A constant rather than a literal in two default parameter values. The
    /// resolving walk on <see cref="ProductAnyTypeOperations"/> has to page the
    /// same way, and two literals that disagree would do so silently.
    /// </remarks>
    internal const int DefaultVariantPageSize = 200;

    /// <summary>The filter that selects the variants of one parent.</summary>
    /// <remarks>
    /// A space combines the two conditions with AND. Keeping this syntax in one
    /// place means nobody has to reconstruct it — and that the resolving walk
    /// cannot end up asking a different question than this one.
    /// </remarks>
    internal static string VariantsQuery(string parentVariantId)
        => $"productType:VARIANT parentVariantId:{parentVariantId}";

    // ----- Writes. Service token by default, so server-side only. -----

    /// <summary>Creates a product.</summary>
    /// <param name="product">The product to create.</param>
    /// <param name="options">Fine-tuning for the write.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">Emporix rejected the product.</exception>
    /// <exception cref="EmporixInsufficientScopeException">The token lacks <c>product.product_manage</c>.</exception>
    /// <remarks>
    /// Pass whichever of the five creation types fits — the specification
    /// declares this body as a <c>oneOf</c> over all five, and the SDK sends
    /// whichever one it receives.
    /// </remarks>
    public async Task<ResourceLocation?> CreateAsync(
        IEmporixProductCreation product,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    product,
                    ProductJsonContext.Default.IEmporixProductCreation),
            },
            ProductJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes individual fields of a product.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="options">Fine-tuning for the write.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// Replaces only what is stated. For a full exchange see <see cref="ReplaceAsync"/>.
    /// </para>
    /// <para>
    /// <b>Not per product type.</b> The specification declares this body as one
    /// flat <c>productPartialUpdate</c> rather than a <c>oneOf</c>, and
    /// <see cref="ProductPartialUpdate"/> carries the union of the
    /// type-specific fields — <c>BundledProducts</c>, <c>VariantAttributes</c>
    /// and <c>Template</c> among them. So a bundle's contents are patched
    /// through this one type, with no bundle-specific alternative to choose.
    /// </para>
    /// </remarks>
    public Task UpdateAsync(
        string productId,
        ProductPartialUpdate changes,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    changes,
                    ProductJsonContext.Default.ProductPartialUpdate),
            },
            cancellationToken);
    }

    /// <summary>Replaces a product in full.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="product">The new state; fields not stated are cleared.</param>
    /// <param name="options">Fine-tuning for the write.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string productId,
        IEmporixProductUpdate product,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(product);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    product,
                    ProductJsonContext.Default.IEmporixProductUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a product.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="force">Deletes even when the product is still referenced.</param>
    /// <param name="doIndex">Controls whether the index is updated immediately.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string productId,
        bool? force = null,
        bool? doIndex = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        List<KeyValuePair<string, string?>> query = [];
        AddFlag(query, "force", force);
        AddFlag(query, "doIndex", doIndex);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Service(auth),
                Query = query,
            },
            cancellationToken);
    }

    /// <summary>Creates several products in one call.</summary>
    /// <param name="products">The products to create.</param>
    /// <param name="options">Fine-tuning for the write.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One result per entry, in input order.</returns>
    /// <remarks>
    /// <strong>Individual failures do not raise an exception.</strong> Emporix
    /// answers this call with 207 and reports the outcome per entry — inspect the
    /// result one by one.
    /// </remarks>
    public async Task<IReadOnlyList<BulkResponse>> CreateManyAsync(
        IReadOnlyCollection<IEmporixProductCreation> products,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (products.Count == 0)
        {
            return [];
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/bulk",
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    [.. products],
                    ProductJsonContext.Default.ListIEmporixProductCreation),
            },
            ProductJsonContext.Default.ListBulkResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    // ----- Internals -----

    internal const int DefaultChunkSize = 100;

    internal async Task<PaginatedItems<T>> ListPageAsync<T>(
        ProductPageOptions options,
        string? query,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", options.PageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", options.PageSize.ToString(CultureInfo.InvariantCulture)),
        ];

        if (query is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("q", query));
        }

        // The total count is requested through a header, not the address.
        List<KeyValuePair<string, string>>? headers = options.IncludeTotalCount
            ? [new("X-Total-Count", "true")]
            : null;

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Anonymous(auth),
                Query = parameters,
                Headers = headers,
            },
            listTypeInfo,
            options.PageNumber,
            options.PageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches across a list of values, splitting it into chunks.
    /// </summary>
    /// <remarks>
    /// The filter travels in the request body: a list of a hundred ids exceeds
    /// the permitted address length. Despite being a <c>POST</c> the call counts
    /// as repeatable — it only reads.
    /// </remarks>
    internal async Task<IReadOnlyList<T>> SearchInChunksAsync<T>(
        string field,
        IReadOnlyCollection<string> values,
        int chunkSize,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        if (values.Count == 0)
        {
            return [];
        }

        List<T> all = [];

        foreach (string[] chunk in values.Chunk(chunkSize))
        {
            List<T>? page = await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Post,
                    Path = $"{BasePath}/search",
                    Auth = Anonymous(auth),
                    Query = [new("pageSize", chunk.Length.ToString(CultureInfo.InvariantCulture))],
                    Content = EmporixJsonContent.Create(
                        new SearchQueryBody { Q = $"{field}:({string.Join(',', chunk)})" },
                        ProductJsonContext.Default.SearchQueryBody),
                    Idempotent = true,
                },
                listTypeInfo,
                cancellationToken).ConfigureAwait(false);

            if (page is not null)
            {
                all.AddRange(page);
            }
        }

        return all;
    }

    private static List<KeyValuePair<string, string?>>? WriteQuery(ProductWriteOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        List<KeyValuePair<string, string?>> query = [];
        AddFlag(query, "skipVariantGeneration", options.SkipVariantGeneration);
        AddFlag(query, "doIndex", options.DoIndex);
        AddFlag(query, "skipRelatedItemsValidation", options.SkipRelatedItemsValidation);

        return query.Count == 0 ? null : query;
    }

    private static void AddFlag(List<KeyValuePair<string, string?>> query, string name, bool? value)
    {
        if (value is { } flag)
        {
            query.Add(new KeyValuePair<string, string?>(name, flag ? "true" : "false"));
        }
    }

    /// <summary>Anonymous unless the call asks for something else.</summary>
    private static AuthContext Anonymous(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Anonymous() : auth;

    /// <summary>Service token unless the call asks for something else.</summary>
    private static AuthContext Service(AuthContext auth)
        => auth.Kind == AuthKind.None ? AuthContext.Service() : auth;

    /// <summary>
    /// Escapes regular-expression metacharacters — and only those.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Regex.Escape(string)"/>: that also escapes
    /// whitespace, and an escaped space in the filter is not the separator
    /// Emporix expects. The list matches the Node SDK's, which was verified
    /// against the live API.
    /// </remarks>
    private static string EscapeRegexMetacharacters(string value)
        => RegexMetacharacters().Replace(value, @"\$&");

    /// <summary>Regular-expression metacharacters, excluding whitespace.</summary>
    [GeneratedRegex(@"[.*+?^${}|[\]\\]")]
    private static partial Regex RegexMetacharacters();

    /// <summary>Characters that would break Emporix' name filter apart.</summary>
    [GeneratedRegex(@"[()""]")]
    private static partial Regex QueryBreakingCharacters();

    /// <summary>Runs of whitespace, collapsed into a single space.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();

    /// <summary>Characters that act as delimiters inside a value list.</summary>
    [GeneratedRegex(@"[(),""\s]")]
    private static partial Regex QueryDelimiters();

    /// <summary>Replaces many products in one call.</summary>
    /// <param name="products">The products in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Each entry reports its own status: a 200 on the call does not mean every
    /// product was written.
    /// </remarks>
    public async Task<IReadOnlyList<BulkResponse>> UpdateManyAsync(
        IEnumerable<IEmporixProductBulkUpdate> products,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        List<IEmporixProductBulkUpdate> body = [.. products];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Put,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        ProductJsonContext.Default.ListIEmporixProductBulkUpdate),
                },
                ProductJsonContext.Default.ListBulkResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Starts recalculating dynamic variants.</summary>
    /// <param name="request">Which products to recalculate.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Recalculation runs in the background: the response identifies a job, not
    /// a result. Follow it with <see cref="GetRecalculationJobAsync"/>.
    /// </remarks>
    public async Task<DynamicVariantRecalculationResponse?> RecalculateVariantsAsync(
        DynamicVariantRecalculationRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/recalculate",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    ProductJsonContext.Default.DynamicVariantRecalculationRequest),
            },
            ProductJsonContext.Default.DynamicVariantRecalculationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the recalculation jobs.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<DynamicVariantRecalculationJobResponse>> ListRecalculationJobsAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/recalculate/jobs",
                Auth = Defaults.Service(auth),
            },
            ProductJsonContext.Default.ListDynamicVariantRecalculationJobResponse,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Reads one recalculation job.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<DynamicVariantRecalculationJobResponse?> GetRecalculationJobAsync(
        string jobId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/recalculate/jobs/{Uri.EscapeDataString(jobId)}",
                Auth = Defaults.Service(auth),
            },
            ProductJsonContext.Default.DynamicVariantRecalculationJobResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Product templates: the attribute sets products are built from.</summary>
    public ProductTemplateOperations Templates => new(_http, _tenant);
}

/// <summary>
/// Product templates.
/// </summary>
/// <remarks>
/// A template defines which attributes a product carries. Reached through
/// <see cref="ProductService.Templates"/>.
/// </remarks>
public sealed class ProductTemplateOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal ProductTemplateOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/product/{_tenant}/product-templates";

    /// <summary>Lists the templates.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<ProductTemplateResponse>> ListAsync(
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query =
                [
                    new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
                    new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
                ],
            },
            ProductJsonContext.Default.ListProductTemplateResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a template.</summary>
    /// <param name="templateId">The template id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ProductTemplateResponse?> GetAsync(
        string templateId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(templateId)}",
                Auth = Defaults.Service(auth),
            },
            ProductJsonContext.Default.ProductTemplateResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a template.</summary>
    /// <param name="template">The template to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ResourceLocation?> CreateAsync(
        ProductTemplateCreation template,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    template,
                    ProductJsonContext.Default.ProductTemplateCreation),
            },
            ProductJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a template.</summary>
    /// <param name="templateId">The template id.</param>
    /// <param name="template">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string templateId,
        ProductTemplateUpdate template,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(template);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(templateId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    template,
                    ProductJsonContext.Default.ProductTemplateUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a template.</summary>
    /// <param name="templateId">The template id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string templateId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(templateId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Product reads that resolve <c>productType</c> into its own generated type.
/// </summary>
/// <remarks>
/// <para>
/// Reached through <c>client.Products.AnyType</c>. Every method mirrors the one
/// on <see cref="ProductService"/> — same parameters, same defaults, same
/// addresses — and returns <see cref="IEmporixProduct"/> instead of
/// <see cref="BasicProductWithId"/>, so the caller can pattern match:
/// </para>
/// <code>
/// var product = await client.Products.AnyType.GetAsync(id);
///
/// if (product is BundleProductWithId bundle)
/// {
///     foreach (var item in bundle.BundledProducts) { }
/// }
/// </code>
/// <para>
/// <b>One limitation.</b> The specification does not require <c>productType</c>
/// on a variant, so a variant sent without it resolves to
/// <see cref="BasicProductWithId"/> and its <c>parentVariantId</c> is reachable
/// only through the extension data. Whether Emporix ever omits it is not
/// established; deriving the type from other fields would be guessing.
/// </para>
/// </remarks>
public sealed class ProductAnyTypeOperations
{
    private readonly ProductService _products;

    // Holds the service rather than the http client and tenant, because the
    // point of this group is to reuse its cores — the paths and query
    // parameters exist once, and this only substitutes the type.
    internal ProductAnyTypeOperations(ProductService products) => _products = products;

    /// <summary>Fetches a product by its id, as its own shape.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IEmporixProduct?> GetAsync(
        string productId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.GetOneCoreAsync(
            productId, ProductJsonContext.Default.IEmporixProduct, auth, cancellationToken);

    /// <summary>Fetches a product by its code, as its own shape.</summary>
    /// <param name="code">The product code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IEmporixProduct?> GetByCodeAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.GetOneByCodeCoreAsync(
            code, ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);

    /// <summary>Lists products, each as its own shape.</summary>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> ListAsync(
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.ListPageAsync(
            options ?? new ProductPageOptions(),
            query: null,
            ProductJsonContext.Default.ListIEmporixProduct,
            auth,
            cancellationToken);

    /// <summary>Searches products with an Emporix <c>q</c> filter.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> SearchAsync(
        string query,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return _products.ListPageAsync(
            options ?? new ProductPageOptions(),
            query,
            ProductJsonContext.Default.ListIEmporixProduct,
            auth,
            cancellationToken);
    }

    /// <summary>Searches products by name.</summary>
    /// <param name="term">The search term.</param>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> SearchByNameAsync(
        string term,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.SearchByNameCoreAsync(
            term, options, ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);

    /// <summary>Fetches several products by id, as their own shapes.</summary>
    /// <param name="productIds">The ids.</param>
    /// <param name="chunkSize">How many ids per request.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<IEmporixProduct>> GetManyByIdAsync(
        IReadOnlyCollection<string> productIds,
        int chunkSize = ProductService.DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return _products.SearchInChunksAsync(
            "id", productIds, chunkSize,
            ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);
    }

    /// <summary>Fetches several products by code, as their own shapes.</summary>
    /// <param name="codes">The codes. Duplicates are collapsed.</param>
    /// <param name="chunkSize">How many codes per request.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Codes containing parentheses, commas, quotes or whitespace are skipped
    /// and logged, exactly as on <see cref="ProductService.GetManyByCodeAsync"/>
    /// — both go through the same core, so the two cannot drift apart.
    /// </remarks>
    public Task<IReadOnlyList<IEmporixProduct>> GetManyByCodeAsync(
        IReadOnlyCollection<string> codes,
        int chunkSize = ProductService.DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.GetManyByCodeCoreAsync(
            codes, chunkSize, ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);

    /// <summary>Walks the whole catalogue, each product as its own shape.</summary>
    /// <param name="pageSize">The page size; 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <remarks>
    /// Resolves per element rather than per page, so a page of plain products
    /// followed by a page of bundles arrives as both.
    /// </remarks>
    public IAsyncEnumerable<IEmporixProduct> ListAllAsync(
        int? pageSize = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => PaginatedItems.EnumerateAllAsync(
            (pageNumber, token) => ListAsync(
                new ProductPageOptions
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize ?? ProductPageOptions.DefaultPageSize,
                },
                auth,
                token),
            startPage: 1,
            cancellationToken);

    /// <summary>Walks the variants of a parent product, each as its own shape.</summary>
    /// <param name="parentVariantId">The id of the parent product.</param>
    /// <param name="pageSize">The page size; 200 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <remarks>
    /// The read where the plain method is most clearly wrong. Its filter pins
    /// <c>productType</c> to <c>VARIANT</c>, so every result is known to be a
    /// variant — and <see cref="ProductService.ListVariantsAsync"/> still hands
    /// back <see cref="BasicProductWithId"/>, leaving
    /// <c>parentVariantId</c> and <c>parentVariantPath</c> reachable only
    /// through the extension data. This one returns
    /// <see cref="VariantProductWithId"/>.
    /// </remarks>
    public IAsyncEnumerable<IEmporixProduct> ListVariantsAsync(
        string parentVariantId,
        int pageSize = ProductService.DefaultVariantPageSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentVariantId);

        return PaginatedItems.EnumerateAllAsync(
            (pageNumber, token) => SearchAsync(
                ProductService.VariantsQuery(parentVariantId),
                new ProductPageOptions { PageNumber = pageNumber, PageSize = pageSize },
                auth,
                token),
            startPage: 1,
            cancellationToken);
    }
}

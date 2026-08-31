using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.PriceModels;

namespace Viu.Emporix;

/// <summary>How a large price match is split into requests.</summary>
public sealed class PriceMatchOptions
{
    /// <summary>How many items per request. Defaults to 50.</summary>
    public int ChunkSize { get; init; } = 50;
}

/// <summary>
/// Emporix prices.
/// </summary>
/// <remarks>
/// The interesting call here is <see cref="MatchByContextAsync"/>: it resolves
/// what a specific person pays right now, taking currency, site, country and
/// customer group from their token rather than from parameters. That is why it
/// requires a customer or anonymous context — a service token carries no such
/// context, and the result would be an empty list rather than an error.
/// </remarks>
public sealed class PriceService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PriceService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/price/{_tenant}/prices";

    /// <summary>
    /// Resolves the prices that apply to the caller's own context.
    /// </summary>
    /// <param name="request">The items to price.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">The auth context carries no price context.</exception>
    /// <remarks>
    /// <para>
    /// Currency, site and country come from the token, not from parameters.
    /// A token minted without that context yields an <b>empty list and no
    /// error</b> — which reads exactly like «no prices configured». Set
    /// <see cref="EmporixStorefrontCredentials.Context"/> so anonymous tokens
    /// carry it.
    /// </para>
    /// <para>
    /// Sent as a <c>POST</c> because the item list does not fit in an address,
    /// but declared repeatable: the call only reads.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Match>> MatchByContextAsync(
        MatchByContext request,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/price/{_tenant}/match-prices-by-context",
                Auth = RequireContextAuth(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PriceJsonContext.Default.MatchByContext),
                Idempotent = true,
            },
            PriceJsonContext.Default.ListMatch,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Resolves prices for many items, splitting the list into several requests.
    /// </summary>
    /// <param name="items">The items to price.</param>
    /// <param name="options">How to split the list.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix bounds how many items one match request may carry. Results are
    /// concatenated in chunk order; within a chunk Emporix decides the order.
    /// The context still comes from the token — there is nothing else to carry
    /// across the chunks.
    /// </remarks>
    public async Task<IReadOnlyList<Match>> MatchByContextChunkedAsync(
        IReadOnlyCollection<Items> items,
        PriceMatchOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        int chunkSize = (options ?? new PriceMatchOptions()).ChunkSize;
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        if (items.Count == 0)
        {
            return [];
        }

        List<Match> all = [];

        foreach (Items[] chunk in items.Chunk(chunkSize))
        {
            all.AddRange(await MatchByContextAsync(
                new MatchByContext { Items = chunk },
                auth,
                cancellationToken).ConfigureAwait(false));
        }

        return all;
    }

    /// <summary>Fetches a price by its id.</summary>
    /// <param name="priceId">The price id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GetPrice?> GetAsync(
        string priceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
            },
            PriceJsonContext.Default.GetPrice,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of prices.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<GetPrice>> ListAsync(
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
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

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Query = parameters,
            },
            PriceJsonContext.Default.ListGetPrice,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a price.</summary>
    /// <param name="price">The price to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        CreatePrice price,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(price);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(price, PriceJsonContext.Default.CreatePrice),
            },
            cancellationToken);
    }

    /// <summary>Deletes a price.</summary>
    /// <param name="priceId">The price id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string priceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }


    /// <summary>Resolves prices for a set of items and an explicit context.</summary>
    /// <param name="request">Which items, currency, site and principal to price.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Unlike <see cref="MatchByContextAsync"/> the context is spelled out here
    /// rather than taken from the token, which is what makes this usable from a
    /// backend acting on someone else's behalf. Declared repeatable: it only
    /// reads.
    /// </remarks>
    public async Task<IReadOnlyList<MatchResponse>> MatchAsync(
        SearchPrices request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/price/{_tenant}/match-prices",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    PriceJsonContext.Default.SearchPrices),
                Idempotent = true,
            },
            PriceJsonContext.Default.ListMatchResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Searches prices with an Emporix query.</summary>
    /// <param name="query">The query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<GetPrice>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SearchRequest { Q = query },
                    PriceJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            PriceJsonContext.Default.ListGetPrice,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a price.</summary>
    /// <param name="priceId">The price id.</param>
    /// <param name="price">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string priceId,
        UpdatePrice price,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);
        ArgumentNullException.ThrowIfNull(price);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(price, PriceJsonContext.Default.UpdatePrice),
            },
            cancellationToken);
    }

    /// <summary>Creates prices in bulk.</summary>
    /// <param name="prices">The prices to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Each entry reports its own outcome; a 200 does not mean all of them landed.</remarks>
    public Task<IReadOnlyList<PriceBulkResponseEntry>> CreateManyAsync(
        IEnumerable<CreatePrice> prices,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Post, prices, auth, cancellationToken);

    /// <summary>Replaces prices in bulk.</summary>
    /// <param name="prices">The prices in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<PriceBulkResponseEntry>> UpdateManyAsync(
        IEnumerable<CreatePrice> prices,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, prices, auth, cancellationToken);

    /// <summary>Price models: how a price is calculated, before any amount.</summary>
    public PriceModelOperations Models => new(_http, _tenant);

    /// <summary>Price lists: the sets prices are grouped into.</summary>
    public PriceListOperations Lists => new(_http, _tenant);

    private async Task<IReadOnlyList<PriceBulkResponseEntry>> BulkAsync(
        HttpMethod method,
        IEnumerable<CreatePrice> prices,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prices);

        List<CreatePrice> body = [.. prices];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = method,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        PriceJsonContext.Default.ListCreatePrice),
                },
                PriceJsonContext.Default.ListPriceBulkResponseEntry,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>
    /// Ensures the call carries a token that has a price context.
    /// </summary>
    /// <remarks>
    /// Emporix reads currency, site and country from the token. A service token
    /// has none, and the request would come back empty rather than failing — the
    /// worst kind of error, because it looks like an answer.
    /// </remarks>
    private static AuthContext RequireContextAuth(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Anonymous or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "Price matching resolves currency, site and country from the token and "
                + "therefore needs a customer or anonymous context. With a service token "
                + "Emporix returns an empty list rather than an error.");
}

/// <summary>
/// Price models: how a price is calculated, before any amount is involved.
/// </summary>
/// <remarks>
/// A model says whether a price includes tax, how tiers are graduated, and so
/// on. Prices point at one. Reached through <see cref="PriceService.Models"/>.
/// </remarks>
public sealed class PriceModelOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PriceModelOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/price/{_tenant}/priceModels";

    /// <summary>Lists the price models.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PriceModelDefinitionRetrieval>> ListAsync(
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
                Query = PriceService.Paging(pageNumber, pageSize),
            },
            PriceJsonContext.Default.ListPriceModelDefinitionRetrieval,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a price model.</summary>
    /// <param name="priceModelId">The model id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PriceModelDefinitionRetrieval?> GetAsync(
        string priceModelId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceModelId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceModelId)}",
                Auth = Defaults.Service(auth),
            },
            PriceJsonContext.Default.PriceModelDefinitionRetrieval,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a price model.</summary>
    /// <param name="model">The model to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PriceModelDefinitionCreationResponse?> CreateAsync(
        PriceModelDefinitionCreation model,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    model,
                    PriceJsonContext.Default.PriceModelDefinitionCreation),
            },
            PriceJsonContext.Default.PriceModelDefinitionCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a price model.</summary>
    /// <param name="priceModelId">The model id.</param>
    /// <param name="model">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Prices already pointing at this model are recalculated against the new
    /// definition, so a change here reaches further than it looks.
    /// </remarks>
    public Task ReplaceAsync(
        string priceModelId,
        PriceModelDefinitionCreation model,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceModelId);
        ArgumentNullException.ThrowIfNull(model);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceModelId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    model,
                    PriceJsonContext.Default.PriceModelDefinitionCreation),
            },
            cancellationToken);
    }

    /// <summary>Deletes a price model.</summary>
    /// <param name="priceModelId">The model id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string priceModelId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceModelId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceModelId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

/// <summary>
/// Price lists and the prices inside them.
/// </summary>
/// <remarks>
/// A price list groups prices that belong together — a currency, a season, a
/// customer segment. Reached through <see cref="PriceService.Lists"/>.
/// </remarks>
public sealed class PriceListOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal PriceListOperations(EmporixHttpClient http, string tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    private string BasePath => $"/price/{_tenant}/price-lists";

    private string PricesIn(string priceListId)
        => $"{BasePath}/{Uri.EscapeDataString(priceListId)}/prices";

    /// <summary>Lists the price lists.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PriceList>> ListAsync(
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
                Query = PriceService.Paging(pageNumber, pageSize),
            },
            PriceJsonContext.Default.ListPriceList,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a price list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PriceList?> GetAsync(
        string priceListId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceListId)}",
                Auth = Defaults.Service(auth),
            },
            PriceJsonContext.Default.PriceList,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches price lists.</summary>
    /// <param name="query">The query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PriceList>> SearchAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Service(auth),
                Query = PriceService.Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SearchRequest { Q = query },
                    PriceJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            PriceJsonContext.Default.ListPriceList,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a price list.</summary>
    /// <param name="priceList">The list to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        PriceListCreation priceList,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(priceList);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    priceList,
                    PriceJsonContext.Default.PriceListCreation),
            },
            cancellationToken);
    }

    /// <summary>Replaces a price list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="priceList">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string priceListId,
        PriceListUpdate priceList,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentNullException.ThrowIfNull(priceList);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceListId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    priceList,
                    PriceJsonContext.Default.PriceListUpdate),
            },
            cancellationToken);
    }

    /// <summary>Deletes a price list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The prices in it go with it — this is not a way to empty a list.</remarks>
    public Task DeleteAsync(
        string priceListId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(priceListId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the prices in a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PriceListPrice>> ListPricesAsync(
        string priceListId,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = PricesIn(priceListId),
                Auth = Defaults.Service(auth),
                Query = PriceService.Paging(pageNumber, pageSize),
            },
            PriceJsonContext.Default.ListPriceListPrice,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one price from a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="priceId">The price id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PriceListPrice?> GetPriceAsync(
        string priceListId,
        string priceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{PricesIn(priceListId)}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
            },
            PriceJsonContext.Default.PriceListPrice,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches the prices in a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="query">The query expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<PriceListPrice>> SearchPricesAsync(
        string priceListId,
        string query,
        int pageNumber = 1,
        int pageSize = 60,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{PricesIn(priceListId)}/search",
                Auth = Defaults.Service(auth),
                Query = PriceService.Paging(pageNumber, pageSize),
                Content = EmporixJsonContent.Create(
                    new SearchRequest { Q = query },
                    PriceJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            PriceJsonContext.Default.ListPriceListPrice,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds a price to a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="price">The price to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task AddPriceAsync(
        string priceListId,
        PriceListPriceCreation price,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentNullException.ThrowIfNull(price);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = PricesIn(priceListId),
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    price,
                    PriceJsonContext.Default.PriceListPriceCreation),
            },
            cancellationToken);
    }

    /// <summary>Replaces a price in a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="priceId">The price id.</param>
    /// <param name="price">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplacePriceAsync(
        string priceListId,
        string priceId,
        PriceListPriceUpdate price,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);
        ArgumentNullException.ThrowIfNull(price);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{PricesIn(priceListId)}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    price,
                    PriceJsonContext.Default.PriceListPriceUpdate),
            },
            cancellationToken);
    }

    /// <summary>Removes a price from a list.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="priceId">The price id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeletePriceAsync(
        string priceListId,
        string priceId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentException.ThrowIfNullOrWhiteSpace(priceId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{PricesIn(priceListId)}/{Uri.EscapeDataString(priceId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Adds prices to a list in bulk.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="prices">The prices to add.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<PriceBulkResponseEntry>> AddPricesAsync(
        string priceListId,
        IEnumerable<PriceListPriceCreation> prices,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Post, priceListId, prices, auth, cancellationToken);

    /// <summary>Replaces prices in a list in bulk.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="prices">The prices in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<PriceBulkResponseEntry>> ReplacePricesAsync(
        string priceListId,
        IEnumerable<PriceListPriceCreation> prices,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, priceListId, prices, auth, cancellationToken);

    /// <summary>Removes prices from a list in bulk.</summary>
    /// <param name="priceListId">The list id.</param>
    /// <param name="priceIds">The prices to remove.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<PriceBulkResponseEntry>> DeletePricesAsync(
        string priceListId,
        IEnumerable<string> priceIds,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentNullException.ThrowIfNull(priceIds);

        List<string> ids = [.. priceIds];

        return ids.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Delete,
                    Path = $"{PricesIn(priceListId)}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        ids,
                        PriceJsonContext.Default.ListString),
                },
                PriceJsonContext.Default.ListPriceBulkResponseEntry,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    private async Task<IReadOnlyList<PriceBulkResponseEntry>> BulkAsync(
        HttpMethod method,
        string priceListId,
        IEnumerable<PriceListPriceCreation> prices,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priceListId);
        ArgumentNullException.ThrowIfNull(prices);

        List<PriceListPriceCreation> body = [.. prices];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = method,
                    Path = $"{PricesIn(priceListId)}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        PriceJsonContext.Default.ListPriceListPriceCreation),
                },
                PriceJsonContext.Default.ListPriceBulkResponseEntry,
                cancellationToken).ConfigureAwait(false) ?? [];
    }
}

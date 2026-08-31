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

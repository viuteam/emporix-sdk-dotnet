using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.FeeModels;

namespace Viu.Emporix;

/// <summary>
/// Fees, and which items and products they apply to.
/// </summary>
/// <remarks>
/// <para>
/// A fee is defined once and then attached to things. Emporix keeps two kinds of
/// attachment and they are not interchangeable: <see cref="ForItem"/> addresses
/// an item by its YRN, <see cref="ForProduct"/> addresses a product by its bare
/// id. Carts and orders already carry the resulting fees as read-only values.
/// </para>
/// <para>
/// Deposits, recycling charges and service fees are what this is for — anything
/// added to a line that is not the price and not tax.
/// </para>
/// </remarks>
public sealed class FeeService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal FeeService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/fee/{_tenant}/fees";

    /// <summary>Fetches a fee, with what it is attached to.</summary>
    /// <param name="feeId">The fee id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<FeeWithItems?> GetAsync(
        string feeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feeId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(feeId)}",
                Auth = Defaults.Service(auth),
            },
            FeeJsonContext.Default.FeeWithItems,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of fees.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<Fee>> ListAsync(
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
                Query = Paging(pageNumber, pageSize),
            },
            FeeJsonContext.Default.ListFee,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a fee.</summary>
    /// <param name="fee">The fee to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ItemFeeCreationResponse?> CreateAsync(
        Fee fee,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fee);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(fee, FeeJsonContext.Default.Fee),
            },
            FeeJsonContext.Default.ItemFeeCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a fee.</summary>
    /// <param name="feeId">The fee id.</param>
    /// <param name="fee">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Everything this fee is attached to now charges the new amount. Orders
    /// already placed keep what they were charged.
    /// </remarks>
    public Task ReplaceAsync(
        string feeId,
        Fee fee,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feeId);
        ArgumentNullException.ThrowIfNull(fee);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(feeId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(fee, FeeJsonContext.Default.Fee),
            },
            cancellationToken);
    }

    /// <summary>Deletes a fee.</summary>
    /// <param name="feeId">The fee id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Its attachments go with it; nothing charges it afterwards.</remarks>
    public Task DeleteAsync(
        string feeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feeId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(feeId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Fetches one page of item-to-fee attachments.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<ItemFee>> ListAttachmentsAsync(
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
                Path = $"/fee/{_tenant}/itemFees",
                Auth = Defaults.Service(auth),
                Query = Paging(pageNumber, pageSize),
            },
            FeeJsonContext.Default.ListItemFee,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Attaches fees to an item.</summary>
    /// <param name="attachment">Which item, and which fees.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ItemFeeCreationResponse?> AttachAsync(
        ItemFee attachment,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/fee/{_tenant}/itemFees",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(attachment, FeeJsonContext.Default.ItemFee),
            },
            FeeJsonContext.Default.ItemFeeCreationResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds the fees attached to one product.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="siteCodes">The sites to look in. Emporix requires at least one.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> because the search is a body, but declared repeatable: it
    /// only reads. The sites are not optional — a fee is attached per site, so
    /// «which sites» is part of the question.
    /// </remarks>
    public async Task<IReadOnlyList<ItemFee>> SearchByProductAsync(
        string productId,
        IEnumerable<string> siteCodes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(siteCodes);

        List<string> sites = [.. siteCodes];
        ArgumentOutOfRangeException.ThrowIfZero(sites.Count, nameof(siteCodes));

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/fee/{_tenant}/itemFees/searchByProductId",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    new SearchItemFee { ProductId = productId, SiteCodes = sites },
                    FeeJsonContext.Default.SearchItemFee),
                Idempotent = true,
            },
            FeeJsonContext.Default.ListItemFee,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Finds the fees attached to several products.</summary>
    /// <param name="productIds">
    /// The products, in whatever encoding Emporix expects — see the remarks.
    /// </param>
    /// <param name="siteCode">The site to look in. Required.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// <b>The parameter is a string, not a list, and that is not an oversight
    /// here.</b> The specification declares <c>productIds</c> as a plain string
    /// despite the plural name, and neither the specification nor the published
    /// documentation says how several ids are encoded inside it — comma,
    /// space, or otherwise. Guessing a separator would produce a signature that
    /// looks right and sends the wrong thing, so the value is passed through
    /// verbatim.
    /// </para>
    /// <para>
    /// Prefer <see cref="SearchByProductAsync"/>, which is fully specified, and
    /// use this once Emporix has confirmed the format.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ItemFee>> SearchByProductsAsync(
        string productIds,
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/fee/{_tenant}/itemFees/searchByProductIds",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    new SearchItemsFee { ProductIds = productIds, SiteCode = siteCode },
                    FeeJsonContext.Default.SearchItemsFee),
                Idempotent = true,
            },
            FeeJsonContext.Default.ListItemFee,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Finds the fees attached to several items, by YRN.</summary>
    /// <param name="itemYrns">The item references.</param>
    /// <param name="siteCode">The site to look in. Required.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<ItemFee>> SearchByItemAsync(
        IEnumerable<string> itemYrns,
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemYrns);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        List<string> yrns = [.. itemYrns];

        return yrns.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Post,
                    Path = $"/fee/{_tenant}/itemFees/search",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        new ItemYRNs { ItemYrns = yrns, SiteCode = siteCode },
                        FeeJsonContext.Default.ItemYRNs),
                    Idempotent = true,
                },
                FeeJsonContext.Default.ListItemFee,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>The fees attached to one item, addressed by its YRN.</summary>
    /// <param name="itemYrn">The item reference.</param>
    public FeeAttachmentOperations ForItem(string itemYrn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemYrn);
        return new FeeAttachmentOperations(_http, $"/fee/{_tenant}/itemFees/{Uri.EscapeDataString(itemYrn)}/fees");
    }

    /// <summary>The fees attached to one product, addressed by its bare id.</summary>
    /// <param name="productId">The product.</param>
    /// <remarks>
    /// A product id, not a YRN — the two attachment kinds address their target
    /// differently, and using one form on the other endpoint is a 404.
    /// </remarks>
    public FeeAttachmentOperations ForProduct(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return new FeeAttachmentOperations(_http, $"/fee/{_tenant}/productFees/{Uri.EscapeDataString(productId)}/fees");
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize) =>
    [
        new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
        new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// The fees attached to one thing.
/// </summary>
/// <remarks>
/// Reached through <see cref="FeeService.ForItem"/> or
/// <see cref="FeeService.ForProduct"/>, which differ only in how they address
/// the target. Everything below behaves the same either way.
/// </remarks>
public sealed class FeeAttachmentOperations
{
    private readonly EmporixHttpClient _http;
    private readonly string _basePath;

    internal FeeAttachmentOperations(EmporixHttpClient http, string basePath)
    {
        _http = http;
        _basePath = basePath;
    }

    /// <summary>Lists the attached fees.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<Fee>> ListAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = _basePath,
                Auth = Defaults.Service(auth),
            },
            FeeJsonContext.Default.ListFee,
            cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>Replaces the whole set of attached fees.</summary>
    /// <param name="feeIds">The fees that should be attached.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Whatever you leave out is detached. To add one without listing the rest,
    /// read first — there is no add-single endpoint.
    /// </remarks>
    public Task ReplaceAsync(
        IEnumerable<string> feeIds,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feeIds);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = _basePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    new FeeIdsUpdate { FeeIds = [.. feeIds] },
                    FeeJsonContext.Default.FeeIdsUpdate),
            },
            cancellationToken);
    }

    /// <summary>Detaches every fee.</summary>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>The fees themselves are untouched; only this attachment goes.</remarks>
    public Task DeleteAllAsync(
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = _basePath,
                Auth = Defaults.Service(auth),
            },
            cancellationToken);

    /// <summary>Detaches one fee.</summary>
    /// <param name="feeId">The fee to detach.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string feeId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feeId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{_basePath}/{Uri.EscapeDataString(feeId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

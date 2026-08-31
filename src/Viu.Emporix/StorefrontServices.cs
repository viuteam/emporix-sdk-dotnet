using System.Globalization;
using Microsoft.Extensions.Options;

namespace Viu.Emporix;

/// <summary>
/// Product availability per site.
/// </summary>
public sealed class AvailabilityService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal AvailabilityService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/availability/{_tenant}/availability";

    /// <summary>
    /// Fetches the availability of one product at one site.
    /// </summary>
    /// <param name="productId">The product id.</param>
    /// <param name="siteCode">The site code.</param>
    /// <param name="treatMissingAsAvailable">
    /// Treats «no stock record» as available rather than raising.
    /// </param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">
    /// No stock record exists and <paramref name="treatMissingAsAvailable"/> is off.
    /// </exception>
    /// <remarks>
    /// Emporix keeps stock records only for products that are actually tracked.
    /// For a catalog where most products are always in stock, a missing record
    /// means «available», not «unknown» — which is what the flag is for. It is
    /// off by default because assuming availability is the more expensive
    /// mistake.
    /// </remarks>
    public async Task<AvailabilityModels.Availability?> GetAsync(
        string productId,
        string siteCode,
        bool treatMissingAsAvailable = false,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        try
        {
            return await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Get,
                    Path = $"{BasePath}/{Uri.EscapeDataString(productId)}/{Uri.EscapeDataString(siteCode)}",
                    Auth = Defaults.Anonymous(auth),
                },
                AvailabilityJsonContext.Default.Availability,
                cancellationToken).ConfigureAwait(false);
        }
        catch (EmporixNotFoundException) when (treatMissingAsAvailable)
        {
            return new AvailabilityModels.Availability
            {
                ProductId = productId,
                Site = siteCode,
                Available = true,
            };
        }
    }

    /// <summary>Lists everything tracked at a site.</summary>
    /// <param name="siteCode">The site code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AvailabilityModels.Availability>> ListForSiteAsync(
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/site/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Anonymous(auth),
            },
            AvailabilityJsonContext.Default.ListAvailability,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Records or replaces the availability of a product.</summary>
    /// <param name="availability">The record to store.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task CreateAsync(
        AvailabilityModels.AvailabilityDto availability,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availability);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    availability,
                    AvailabilityJsonContext.Default.AvailabilityDto),
            },
            cancellationToken);
    }

    /// <summary>Removes the availability record of a product at a site.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="siteCode">The site code.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string productId,
        string siteCode,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Fetches availability for several products on one site.</summary>
    /// <param name="productIds">The products to look up.</param>
    /// <param name="siteCode">The site.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="treatMissingAsAvailable">
    /// Whether a product with no record counts as available. Off by default.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The result is in the order asked for, one entry per requested product,
    /// including the ones Emporix has no record for — a caller lining these up
    /// against a product list must not have to guess which one went missing.
    /// Sent as a <c>POST</c> because the id list does not fit in an address, but
    /// declared repeatable: it only reads.
    /// </remarks>
    public async Task<IReadOnlyList<AvailabilityModels.Availability>> GetManyAsync(
        IReadOnlyList<string> productIds,
        string siteCode,
        AuthContext auth = default,
        bool treatMissingAsAvailable = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        if (productIds.Count == 0)
        {
            return [];
        }

        List<AvailabilityModels.Availability> found = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = Defaults.Anonymous(auth),
                Query =
                [
                    new("site", siteCode),
                    new("pageSize", productIds.Count.ToString(CultureInfo.InvariantCulture)),
                ],
                Content = EmporixJsonContent.Create(
                    productIds.ToList(),
                    AvailabilityJsonContext.Default.ListString),
                Idempotent = true,
            },
            AvailabilityJsonContext.Default.ListAvailability,
            cancellationToken).ConfigureAwait(false) ?? [];

        Dictionary<string, AvailabilityModels.Availability> byId = [];
        foreach (AvailabilityModels.Availability entry in found)
        {
            if (entry.ProductId is { Length: > 0 } id)
            {
                byId[id] = entry;
            }
        }

        return
        [
            .. productIds.Select(id => byId.TryGetValue(id, out AvailabilityModels.Availability? hit)
                ? hit
                : new AvailabilityModels.Availability
                {
                    ProductId = id,
                    Site = siteCode,
                    Available = treatMissingAsAvailable,
                }),
        ];
    }

    /// <summary>Replaces a product's availability on a site.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="siteCode">The site.</param>
    /// <param name="availability">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string productId,
        string siteCode,
        AvailabilityModels.AvailabilityDto availability,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);
        ArgumentNullException.ThrowIfNull(availability);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}/{Uri.EscapeDataString(siteCode)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    availability,
                    AvailabilityJsonContext.Default.AvailabilityDto),
            },
            cancellationToken);
    }

    /// <summary>Creates availability records in bulk.</summary>
    /// <param name="records">The records to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Each entry carries its own status; a 200 does not mean all of them landed.</remarks>
    public Task<IReadOnlyList<AvailabilityModels.BulkResponse>> CreateManyAsync(
        IEnumerable<AvailabilityModels.AvailabilityBulkDto> records,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Post, records, auth, cancellationToken);

    /// <summary>Replaces availability records in bulk.</summary>
    /// <param name="records">The records in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<AvailabilityModels.BulkResponse>> UpdateManyAsync(
        IEnumerable<AvailabilityModels.AvailabilityBulkDto> records,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => BulkAsync(HttpMethod.Put, records, auth, cancellationToken);

    /// <summary>Deletes availability records in bulk.</summary>
    /// <param name="records">Which product and site to clear.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<AvailabilityModels.BulkResponse>> DeleteManyAsync(
        IEnumerable<AvailabilityModels.AvailabilityDeleteBulkDto> records,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        List<AvailabilityModels.AvailabilityDeleteBulkDto> body = [.. records];

        return body.Count == 0
            ? []
            : await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Delete,
                    Path = $"{BasePath}/bulk",
                    Auth = Defaults.Service(auth),
                    Content = EmporixJsonContent.Create(
                        body,
                        AvailabilityJsonContext.Default.ListAvailabilityDeleteBulkDto),
                },
                AvailabilityJsonContext.Default.ListBulkResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }

    private async Task<IReadOnlyList<AvailabilityModels.BulkResponse>> BulkAsync(
        HttpMethod method,
        IEnumerable<AvailabilityModels.AvailabilityBulkDto> records,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        List<AvailabilityModels.AvailabilityBulkDto> body = [.. records];

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
                        AvailabilityJsonContext.Default.ListAvailabilityBulkDto),
                },
                AvailabilityJsonContext.Default.ListBulkResponse,
                cancellationToken).ConfigureAwait(false) ?? [];
    }
}

/// <summary>
/// Turning a cart into an order.
/// </summary>
/// <remarks>
/// The one call that must never be repeated on its own. A server error can
/// arrive after Emporix already placed the order, so this is deliberately not
/// declared repeatable — see <see cref="EmporixRetryHandler"/>.
/// </remarks>
public sealed class CheckoutService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CheckoutService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    /// <summary>
    /// Places the order.
    /// </summary>
    /// <param name="checkout">The checkout request, built from the cart.</param>
    /// <param name="auth">The customer or anonymous context that owns the cart. Required.</param>
    /// <param name="saasToken">
    /// The second token from <see cref="CustomerSession.SaasToken"/>, where the
    /// tenant's checkout requires it.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// Emporix rejected the checkout — an invalid cart, a failed payment
    /// authorisation, or stock that ran out in the meantime.
    /// </exception>
    /// <remarks>
    /// <b>Not repeated automatically.</b> If this call fails with a server error
    /// or a timeout, whether the order exists is unknown — check before retrying,
    /// or you may charge twice.
    /// </remarks>
    public async Task<CheckoutModels.ResponseCheckout?> PlaceOrderAsync(
        CheckoutModels.RequestCheckout checkout,
        AuthContext auth,
        string? saasToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/checkout/{_tenant}/checkouts/order",
                Auth = RequireShopper(auth),
                Content = EmporixJsonContent.Create(
                    checkout,
                    CheckoutJsonContext.Default.RequestCheckout),
                Headers = saasToken is { Length: > 0 } ? [new("saas-token", saasToken)] : null,

                // Deliberately absent: Idempotent. Placing an order twice is the
                // failure this whole guard exists to prevent.
            },
            CheckoutJsonContext.Default.ResponseCheckout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns an accepted quote into an order.</summary>
    /// <param name="checkout">The quote checkout request.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="saasToken">An optional SaaS token, where the tenant requires one.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Same endpoint as <see cref="PlaceOrderAsync"/>, different body: the quote
    /// supplies what the cart otherwise would. Deliberately not repeatable — a
    /// retried checkout is a second order.
    /// </remarks>
    public async Task<CheckoutModels.ResponseCheckout?> PlaceOrderFromQuoteAsync(
        CheckoutModels.RequestFromQuoteCheckout checkout,
        AuthContext auth,
        string? saasToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"/checkout/{_tenant}/checkouts/order",
                Auth = RequireShopper(auth),
                Content = EmporixJsonContent.Create(
                    checkout,
                    CheckoutJsonContext.Default.RequestFromQuoteCheckout),
                Headers = saasToken is { Length: > 0 } ? [new("saas-token", saasToken)] : null,
            },
            CheckoutJsonContext.Default.ResponseCheckout,
            cancellationToken).ConfigureAwait(false);
    }

    private static AuthContext RequireShopper(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Anonymous or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "Checkout acts on a cart and therefore requires the customer or anonymous "
                + "context that owns it.");
}

/// <summary>
/// A customer's own orders, as a storefront sees them.
/// </summary>
/// <remarks>
/// Emporix keeps two order collections. This one, <c>/orders</c>, shows a person
/// what they ordered; the token decides which orders that is. The administrative
/// collection — creating, searching, splitting, calculating — is
/// <see cref="SalesOrderService"/>.
/// </remarks>
public sealed class OrderService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal OrderService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/order-v2/{_tenant}/orders";

    /// <summary>Fetches one of the caller's own orders.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No such order, or it is not this customer's.</exception>
    public async Task<OrderV2Models.Order?> GetAsync(
        string orderId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}",
                Auth = RequireCustomer(auth),
            },
            OrderJsonContext.Default.Order,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the signed-in customer's own orders.
    /// </summary>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Which orders come back follows from the token — there is no customer
    /// parameter, and there must not be one.
    /// </remarks>
    public async Task<PaginatedItems<OrderV2Models.Order>> ListMineAsync(
        AuthContext auth,
        string? query = null,
        int pageNumber = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = RequireCustomer(auth),
                Query = Paging(pageNumber, pageSize, query),
            },
            OrderJsonContext.Default.ListOrder,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the statuses an order may move to next.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Ask this before offering a «cancel» button: what is allowed depends on
    /// where the order currently stands.
    /// </remarks>
    public async Task<IReadOnlyList<OrderV2Models.Transition>> ListTransitionsAsync(
        string orderId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/transitions",
                Auth = RequireCustomer(auth),
            },
            OrderJsonContext.Default.ListTransition,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Moves the order to another status.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="status">The target status.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="saasToken">An optional SaaS token, where the tenant requires one.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// The transition is not allowed from the order's current status.
    /// </exception>
    public Task ChangeStatusAsync(
        string orderId,
        OrderV2Models.OrderStatus status,
        AuthContext auth,
        string? saasToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/transitions",
                Auth = RequireCustomer(auth),
                Content = EmporixJsonContent.Create(
                    new OrderV2Models.Transition { Status = status },
                    OrderJsonContext.Default.Transition),
                Headers = saasToken is { Length: > 0 } ? [new("saas-token", saasToken)] : null,
            },
            cancellationToken);
    }

    /// <summary>Cancels the order.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">The customer's own context. Required.</param>
    /// <param name="saasToken">An optional SaaS token, where the tenant requires one.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Cancelling is the <c>DECLINED</c> transition. Whether it is available at
    /// all depends on how far the order has progressed — check
    /// <see cref="ListTransitionsAsync"/> first.
    /// </remarks>
    public Task CancelAsync(
        string orderId,
        AuthContext auth,
        string? saasToken = null,
        CancellationToken cancellationToken = default)
        => ChangeStatusAsync(
            orderId,
            OrderV2Models.OrderStatus.DECLINED,
            auth,
            saasToken,
            cancellationToken);

    /// <summary>Lists a legal entity's orders.</summary>
    /// <param name="legalEntityId">The legal entity.</param>
    /// <param name="auth">A customer context with access to that entity. Required.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The B2B view: everything ordered on behalf of a company, not just what
    /// this one person ordered.
    /// </remarks>
    public async Task<PaginatedItems<OrderV2Models.Order>> ListForLegalEntityAsync(
        string legalEntityId,
        AuthContext auth,
        int pageNumber = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/order-v2/{_tenant}/legal-entity-orders"
                    + $"/{Uri.EscapeDataString(legalEntityId)}",
                Auth = RequireCustomer(auth),
                Query = Paging(pageNumber, pageSize, null),
            },
            OrderJsonContext.Default.ListOrder,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one of a legal entity's orders.</summary>
    /// <param name="legalEntityId">The legal entity.</param>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">A customer context with access to that entity. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<OrderV2Models.Order?> GetForLegalEntityAsync(
        string legalEntityId,
        string orderId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"/order-v2/{_tenant}/legal-entity-orders"
                    + $"/{Uri.EscapeDataString(legalEntityId)}"
                    + $"/{Uri.EscapeDataString(orderId)}",
                Auth = RequireCustomer(auth),
            },
            OrderJsonContext.Default.Order,
            cancellationToken).ConfigureAwait(false);
    }

    internal static List<KeyValuePair<string, string?>> Paging(int pageNumber, int pageSize, string? query)
    {
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

    private static AuthContext RequireCustomer(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Raw
            ? auth
            : throw new EmporixConfigurationException(
                "Storefront order calls derive the orders from the token and therefore "
                + "require that customer's own context. For administrative access use "
                + "SalesOrderService.");
}

/// <summary>
/// The administrative order collection.
/// </summary>
/// <remarks>
/// <c>/salesorders</c> is the back-office view: every order in the tenant,
/// creatable, searchable and editable. It defaults to a service token. What a
/// shopper sees is <see cref="OrderService"/>.
/// </remarks>
public sealed class SalesOrderService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal SalesOrderService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/order-v2/{_tenant}/salesorders";

    /// <summary>Fetches a sales order by its id.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<OrderV2Models.SalesOrder?> GetAsync(
        string orderId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
            },
            OrderJsonContext.Default.SalesOrder,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of sales orders.</summary>
    /// <param name="query">An optional Emporix filter.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<OrderV2Models.SalesOrder>> ListAsync(
        string? query = null,
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
                Query = OrderService.Paging(pageNumber, pageSize, query),
            },
            OrderJsonContext.Default.ListSalesOrder,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches sales orders.</summary>
    /// <param name="query">The Emporix search expression.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Sent as a <c>POST</c> because the expression does not fit in an address,
    /// but declared repeatable: the call only reads.
    /// </remarks>
    public async Task<PaginatedItems<OrderV2Models.SalesOrder>> SearchAsync(
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
                Query = OrderService.Paging(pageNumber, pageSize, null),
                Content = EmporixJsonContent.Create(
                    new OrderV2Models.SearchRequest { Q = query },
                    OrderJsonContext.Default.SearchRequest),
                Idempotent = true,
            },
            OrderJsonContext.Default.ListSalesOrder,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a sales order.</summary>
    /// <param name="order">The order to create.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Deliberately not marked repeatable: a retried create is a second order.
    /// </remarks>
    public async Task<OrderV2Models.ResourceLocation?> CreateAsync(
        OrderV2Models.SalesOrderCreationDto order,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    order,
                    OrderJsonContext.Default.SalesOrderCreationDto),
            },
            OrderJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a sales order.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="order">The new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ReplaceAsync(
        string orderId,
        OrderV2Models.OrderCreationDto order,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(order);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    order,
                    OrderJsonContext.Default.OrderCreationDto),
            },
            cancellationToken);
    }

    /// <summary>Updates parts of a sales order.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateAsync(
        string orderId,
        OrderV2Models.OrderUpdateDto changes,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    changes,
                    OrderJsonContext.Default.OrderUpdateDto),
            },
            cancellationToken);
    }

    /// <summary>Deletes a sales order.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string orderId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the statuses the order may move to next.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<OrderV2Models.Transition>> ListTransitionsAsync(
        string orderId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/transitions",
                Auth = Defaults.Service(auth),
            },
            OrderJsonContext.Default.ListTransition,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Moves the order to another status.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="status">The target status.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="saasToken">An optional SaaS token, where the tenant requires one.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// The transition is not allowed from the order's current status.
    /// </exception>
    public Task ChangeStatusAsync(
        string orderId,
        OrderV2Models.OrderStatus status,
        AuthContext auth = default,
        string? saasToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/transitions",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    new OrderV2Models.Transition { Status = status },
                    OrderJsonContext.Default.Transition),
                Headers = saasToken is { Length: > 0 } ? [new("saas-token", saasToken)] : null,
            },
            cancellationToken);
    }

    /// <summary>Reads the order's status history.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>Where the order has been, rather than where it may go next.</remarks>
    public async Task<OrderV2Models.HistoricalTransitionsResponse?> ListHistoricalTransitionsAsync(
        string orderId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/historical-transitions",
                Auth = Defaults.Service(auth),
            },
            OrderJsonContext.Default.HistoricalTransitionsResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Recalculates the order's totals.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="calculation">What to recalculate.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A <c>POST</c> that computes rather than changes, so it is declared
    /// repeatable.
    /// </remarks>
    public async Task<OrderV2Models.SalesOrder?> CalculateAsync(
        string orderId,
        OrderV2Models.OrderCalculationDto calculation,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(calculation);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/calculations",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    calculation,
                    OrderJsonContext.Default.OrderCalculationDto),
                Idempotent = true,
            },
            OrderJsonContext.Default.SalesOrder,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the order's entries.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="entries">The entries in their new state.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateEntriesAsync(
        string orderId,
        OrderV2Models.OrderEntriesDto entries,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(entries);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/entries",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    entries,
                    OrderJsonContext.Default.OrderEntriesDto),
            },
            cancellationToken);
    }

    /// <summary>Splits the order into several.</summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="request">How to split.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Fulfilment from several warehouses, typically. Not repeatable: splitting
    /// twice creates orders twice.
    /// </remarks>
    public async Task<OrderV2Models.OrderSplitResponse?> SplitAsync(
        string orderId,
        OrderV2Models.OrderSplitRequest request,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(request);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(orderId)}/split",
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    request,
                    OrderJsonContext.Default.OrderSplitRequest),
            },
            OrderJsonContext.Default.OrderSplitResponse,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Media assets.
/// </summary>
/// <remarks>
/// Every call here needs a service token. Storefronts read images through the
/// product they belong to, not through this service — its scopes are
/// server-side only.
/// </remarks>
public sealed class MediaService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal MediaService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/media/{_tenant}/assets";

    /// <summary>Fetches an asset's metadata.</summary>
    /// <param name="assetId">The asset id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<MediaModels.GetAsset?> GetAsync(
        string assetId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(assetId)}",
                Auth = Defaults.Service(auth),
            },
            MediaJsonContext.Default.GetAsset,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches one page of assets.</summary>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<PaginatedItems<MediaModels.GetAsset>> ListAsync(
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
            MediaJsonContext.Default.ListGetAsset,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers an asset that lives at an external address.</summary>
    /// <param name="asset">The asset to register.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<MediaModels.GetAssetLink?> CreateLinkAsync(
        MediaModels.AssetCreateLink asset,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Defaults.Service(auth),
                Content = EmporixJsonContent.Create(
                    asset,
                    MediaJsonContext.Default.AssetCreateLink),
            },
            MediaJsonContext.Default.GetAssetLink,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads an asset's bytes.
    /// </summary>
    /// <param name="assetId">The asset id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The raw response. The caller owns it and has to dispose it.
    /// </returns>
    /// <remarks>
    /// Returns the response unread rather than a byte array: an asset can be
    /// large, and buffering it in memory should be the caller's decision. For a
    /// public asset Emporix answers with a redirect instead of the bytes.
    /// </remarks>
    public Task<HttpResponseMessage> DownloadAsync(
        string assetId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        return _http.SendRawAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(assetId)}/download",
                Auth = Defaults.Service(auth),
            },
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    /// <summary>Deletes an asset.</summary>
    /// <param name="assetId">The asset id.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string assetId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(assetId)}",
                Auth = Defaults.Service(auth),
            },
            cancellationToken);
    }
}

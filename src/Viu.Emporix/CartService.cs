using System.Globalization;
using Microsoft.Extensions.Options;
using Viu.Emporix.CartModels;

namespace Viu.Emporix;

/// <summary>Criteria that identify the session's current cart.</summary>
/// <remarks>
/// Emporix defines uniqueness as <c>siteCode</c> plus <c>type</c> plus
/// <c>legalEntityId</c> plus the customer or session the token belongs to.
/// </remarks>
public sealed class CurrentCartQuery
{
    /// <summary>The site the cart belongs to. Required.</summary>
    public required string SiteCode { get; init; }

    /// <summary>The cart type, where a tenant distinguishes several.</summary>
    public string? Type { get; init; }

    /// <summary>The legal entity, in B2B scenarios.</summary>
    public string? LegalEntityId { get; init; }

    /// <summary>Creates a cart when no matching one exists.</summary>
    public bool Create { get; init; }
}

/// <summary>
/// Emporix shopping carts.
/// </summary>
/// <remarks>
/// Unlike the catalog services, every call here requires an <b>explicit</b>
/// auth context — either a customer or an anonymous one. A cart always belongs
/// to somebody; a service token has no cart, and silently defaulting to one
/// would attach the cart to the wrong party.
/// </remarks>
public sealed class CartService
{
    private readonly EmporixHttpClient _http;
    private readonly string _tenant;

    internal CartService(EmporixHttpClient http, IOptions<EmporixOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _tenant = options.Value.Tenant;
    }

    private string BasePath => $"/cart/{_tenant}/carts";

    /// <summary>Creates a cart.</summary>
    /// <param name="cart">The initial state; may be omitted.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">The auth context is neither customer nor anonymous.</exception>
    public async Task<CartCreated?> CreateAsync(
        CreateCart? cart,
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = RequireCartAuth(auth),
                Content = EmporixJsonContent.Create(
                    cart ?? new CreateCart(),
                    CartJsonContext.Default.CreateCart),
            },
            CartJsonContext.Default.CartCreated,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Fetches a cart by id.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixNotFoundException">No cart exists with this id.</exception>
    public async Task<Cart?> GetAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.Cart,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the current cart for this session, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    /// <param name="query">What identifies the cart.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// «No cart yet» is the normal state for a first visit, not a failure —
    /// hence <see langword="null"/> rather than an exception. Set
    /// <see cref="CurrentCartQuery.Create"/> to have Emporix create one on the
    /// spot. Any other error status propagates.
    /// </remarks>
    public async Task<Cart?> GetCurrentAsync(
        CurrentCartQuery query,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SiteCode);

        List<KeyValuePair<string, string?>> parameters = [new("siteCode", query.SiteCode)];

        if (query.Type is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("type", query.Type));
        }

        if (query.LegalEntityId is { Length: > 0 })
        {
            parameters.Add(new KeyValuePair<string, string?>("legalEntityId", query.LegalEntityId));
        }

        if (query.Create)
        {
            parameters.Add(new KeyValuePair<string, string?>("create", "true"));
        }

        try
        {
            return await _http.SendAsync(
                new EmporixRequest
                {
                    Method = HttpMethod.Get,
                    Path = BasePath,
                    Auth = RequireCartAuth(auth),
                    Query = parameters,
                },
                CartJsonContext.Default.Cart,
                cancellationToken).ConfigureAwait(false);
        }
        catch (EmporixNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Adds an item to the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="item">The item. Its product reference must be a YRN — see <see cref="ProductYrn"/>.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// Emporix rejected the item — most often because the product reference is a
    /// bare id rather than a YRN.
    /// </exception>
    public async Task<CartItemResponse?> AddItemAsync(
        string cartId,
        CartItemRequest item,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(item);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items",
                Auth = RequireCartAuth(auth),
                Content = EmporixJsonContent.Create(item, CartJsonContext.Default.CartItemRequest),
            },
            CartJsonContext.Default.CartItemResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes an item in the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="changes">The new values.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task UpdateItemAsync(
        string cartId,
        string itemId,
        UpdateCartItem changes,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items/{Uri.EscapeDataString(itemId)}",
                Auth = RequireCartAuth(auth),
                Content = EmporixJsonContent.Create(changes, CartJsonContext.Default.UpdateCartItem),
            },
            cancellationToken);
    }

    /// <summary>Removes an item from the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveItemAsync(
        string cartId,
        string itemId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items/{Uri.EscapeDataString(itemId)}",
                Auth = RequireCartAuth(auth),
            },
            cancellationToken);
    }

    /// <summary>Lists the cart's items with their calculated prices.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<CartItemResponse>> ListItemsAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.ListCartItemResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes every item from the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task ClearAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items",
                Auth = RequireCartAuth(auth),
            },
            cancellationToken);
    }

    /// <summary>Deletes the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}",
                Auth = RequireCartAuth(auth),
            },
            cancellationToken);
    }

    /// <summary>Checks the cart for problems that would block checkout.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CartValidationResult?> ValidateAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/validate",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.CartValidationResult,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reprices the cart and returns its updated state.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Two calls: Emporix answers the refresh itself without a body, so the cart
    /// is fetched afterwards. Returning the stale cart would be the more
    /// surprising outcome.
    /// </remarks>
    public async Task<Cart?> RefreshAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        AuthContext cartAuth = RequireCartAuth(auth);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/refresh",
                Auth = cartAuth,
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies a coupon to the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="couponCode">The coupon code.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixValidationException">
    /// Emporix rejected the coupon — unknown, expired, or its conditions are not met.
    /// </exception>
    public Task ApplyCouponAsync(
        string cartId,
        string couponCode,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(couponCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/coupons"
                    + $"/{Uri.EscapeDataString(couponCode)}",
                Auth = RequireCartAuth(auth),
            },
            cancellationToken);
    }

    /// <summary>Removes a coupon from the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="couponCode">The coupon code.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task RemoveCouponAsync(
        string cartId,
        string couponCode,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(couponCode);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/coupons"
                    + $"/{Uri.EscapeDataString(couponCode)}",
                Auth = RequireCartAuth(auth),
            },
            cancellationToken);
    }

    /// <summary>Replaces a cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="cart">The new state.</param>
    /// <param name="auth">What to authorise with.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// A backend or owner operation, so the auth context is passed through
    /// unguarded rather than restricted to a shopper. Emporix answers 204, so
    /// the cart is read back and returned — two round trips, one call.
    /// </remarks>
    public async Task<Cart?> UpdateAsync(
        string cartId,
        UpdateCart cart,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(cart);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}",
                Auth = auth,
                Content = EmporixJsonContent.Create(cart, CartJsonContext.Default.UpdateCart),
            },
            cancellationToken).ConfigureAwait(false);

        return await FetchAsync(cartId, auth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Searches carts across the tenant.</summary>
    /// <param name="query">The Emporix search expression.</param>
    /// <param name="auth">What to authorise with; a service token for admin use.</param>
    /// <param name="pageNumber">The page number, counting from 1.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="sort">An optional sort expression.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// An administrative call: a customer token cannot scan other people's carts,
    /// so the auth context is passed through and Emporix enforces the scope.
    /// Sent as a <c>POST</c> because the query does not fit in an address, but
    /// declared repeatable — it only reads.
    /// </remarks>
    public async Task<PaginatedItems<BaseCartItemResponse>> SearchAsync(
        string query,
        AuthContext auth,
        int pageNumber = 1,
        int pageSize = 60,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        List<KeyValuePair<string, string?>> parameters =
        [
            new("pageNumber", pageNumber.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            new("sort", sort),
        ];

        return await _http.SendPageAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/search",
                Auth = auth,
                Query = parameters,
                Content = EmporixJsonContent.Create(
                    new Search { Q = query },
                    CartJsonContext.Default.Search),
                Idempotent = true,
            },
            CartJsonContext.Default.ListBaseCartItemResponse,
            pageNumber,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches a single cart item.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CartItemResponse?> GetItemAsync(
        string cartId,
        string itemId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/items"
                    + $"/{Uri.EscapeDataString(itemId)}",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.CartItemResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds several items in one call.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="items">The items to add.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix reports a status per item rather than failing the batch, so check
    /// the returned entries — a 200 on the call does not mean every item landed.
    /// </remarks>
    public async Task<IReadOnlyList<SingleBatchResponse>> AddItemsAsync(
        string cartId,
        IEnumerable<CartItemRequest> items,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchRequest batch = [.. items];

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/itemsBatch",
                Auth = RequireCartAuth(auth),
                Content = EmporixJsonContent.Create(
                    batch,
                    CartJsonContext.Default.CartItemsBatchRequest),
            },
            CartJsonContext.Default.BatchResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Updates several items in one call.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="items">The items in their new state.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>As with adding, each entry carries its own status.</remarks>
    public async Task<CartItemsBatchUpdateResponse?> UpdateItemsAsync(
        string cartId,
        IEnumerable<CartItemRequest> items,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchUpdateRequest batch = [.. items];

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/itemsBatch",
                Auth = RequireCartAuth(auth),
                Content = EmporixJsonContent.Create(
                    batch,
                    CartJsonContext.Default.CartItemsBatchUpdateRequest),
            },
            CartJsonContext.Default.CartItemsBatchUpdateResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the cart to another site.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="siteCode">The target site.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix re-matches every price against the new site, so the returned cart
    /// can carry different totals than the one you sent.
    /// </remarks>
    public async Task<Cart?> ChangeSiteAsync(
        string cartId,
        string siteCode,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteCode);

        AuthContext cartAuth = RequireCartAuth(auth);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/changeSite",
                Auth = cartAuth,
                Content = EmporixJsonContent.Create(
                    new ChangeSite { SiteCode = siteCode },
                    CartJsonContext.Default.ChangeSite),
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes the cart's currency.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="currency">The target currency, as an ISO 4217 code.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>As with the site, prices are re-matched against the new currency.</remarks>
    public async Task<Cart?> ChangeCurrencyAsync(
        string cartId,
        string currency,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        AuthContext cartAuth = RequireCartAuth(auth);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/changeCurrency",
                Auth = cartAuth,
                Content = EmporixJsonContent.Create(
                    new Body { Currency = currency },
                    CartJsonContext.Default.Body),
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets the shipping address, keeping the billing address.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="address">The shipping address.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Emporix has no per-type address endpoint: addresses live in the cart's
    /// own array, and the update replaces that array wholesale. So this reads
    /// the cart, swaps the shipping entry and writes everything back — sending
    /// only the new address would silently drop the billing one.
    /// </remarks>
    public Task<Cart?> SetShippingAddressAsync(
        string cartId,
        AddressRequest address,
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => MergeAddressAsync(cartId, address, AddressRequestType.SHIPPING, auth, cancellationToken);

    /// <summary>Sets the billing address, keeping the shipping address.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="address">The billing address.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>See <see cref="SetShippingAddressAsync"/> for why this reads before writing.</remarks>
    public Task<Cart?> SetBillingAddressAsync(
        string cartId,
        AddressRequest address,
        AuthContext auth,
        CancellationToken cancellationToken = default)
        => MergeAddressAsync(cartId, address, AddressRequestType.BILLING, auth, cancellationToken);

    /// <summary>Sets both addresses at once.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="shipping">The shipping address, or <see langword="null"/> to leave it unset.</param>
    /// <param name="billing">The billing address, or <see langword="null"/> to leave it unset.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Writes the array in one go and therefore needs no prior read. Whatever
    /// you omit here is gone from the cart — that is the point of setting both.
    /// </remarks>
    public async Task<Cart?> SetAddressesAsync(
        string cartId,
        AddressRequest? shipping,
        AddressRequest? billing,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        AuthContext cartAuth = RequireCartAuth(auth);
        List<AddressRequest> addresses = [];

        if (shipping is not null)
        {
            shipping.Type = AddressRequestType.SHIPPING;
            addresses.Add(shipping);
        }

        if (billing is not null)
        {
            billing.Type = AddressRequestType.BILLING;
            addresses.Add(billing);
        }

        return await WriteAddressesAsync(cartId, addresses, cartAuth, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Merges anonymous carts into a signed-in customer's cart.</summary>
    /// <param name="customerCartId">The target cart; must belong to the signed-in customer.</param>
    /// <param name="anonymousCartIds">The carts to absorb.</param>
    /// <param name="auth">A customer context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="EmporixConfigurationException">The auth context is not a customer one.</exception>
    /// <remarks>
    /// The usual sign-in case: someone filled a cart anonymously and then logged
    /// in. The anonymous carts are closed on success. An anonymous token is not
    /// enough here — the target cart belongs to the customer, so Emporix wants
    /// that customer's token.
    /// </remarks>
    public async Task<Cart?> MergeAsync(
        string customerCartId,
        IEnumerable<string> anonymousCartIds,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerCartId);
        ArgumentNullException.ThrowIfNull(anonymousCartIds);

        if (auth.Kind is not AuthKind.Customer)
        {
            throw new EmporixConfigurationException(
                "Merging carts requires a customer auth context. The target cart "
                + "belongs to the signed-in customer, and Emporix checks that.");
        }

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(customerCartId)}/merge",
                Auth = auth,
                Content = EmporixJsonContent.Create(
                    new MergeCart { Carts = [.. anonymousCartIds] },
                    CartJsonContext.Default.MergeCart),
            },
            CartJsonContext.Default.Cart,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the discounts applied to the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IReadOnlyList<DiscountResponse>> ListDiscountsAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/discounts",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.ListDiscountResponse,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Removes every discount from the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Cart?> RemoveAllDiscountsAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        AuthContext cartAuth = RequireCartAuth(auth);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/discounts",
                Auth = cartAuth,
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes one discount by its position.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="discountIndex">The index Emporix reports on the discount.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// The index comes from <see cref="ListDiscountsAsync"/>; it is the server's
    /// position, not yours, so read before removing.
    /// </remarks>
    public async Task<Cart?> RemoveDiscountAsync(
        string cartId,
        int discountIndex,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentOutOfRangeException.ThrowIfNegative(discountIndex);

        AuthContext cartAuth = RequireCartAuth(auth);

        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Delete,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/discounts"
                    + $"/{discountIndex.ToString(CultureInfo.InvariantCulture)}",
                Auth = cartAuth,
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the delivery restrictions that apply to the cart.</summary>
    /// <param name="cartId">The cart id.</param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Reports the lead time and the days on which no delivery happens — what a
    /// checkout needs before offering a delivery date.
    /// </remarks>
    public async Task<CartDTRestrictions?> GetDeliveryRestrictionsAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/dtRestrictions",
                Auth = RequireCartAuth(auth),
            },
            CartJsonContext.Default.CartDTRestrictions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the entry of one address type and keeps every other one.
    /// </summary>
    private async Task<Cart?> MergeAddressAsync(
        string cartId,
        AddressRequest address,
        AddressRequestType type,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(address);

        AuthContext cartAuth = RequireCartAuth(auth);

        Cart? current = await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);

        List<AddressRequest> addresses = [];

        foreach (AddressResponse existing in current?.Addresses ?? [])
        {
            // Resending the others unchanged is what stops the write from
            // wiping them.
            if (ToRequestType(existing.Type) != type)
            {
                addresses.Add(ToRequest(existing));
            }
        }

        address.Type = type;
        addresses.Add(address);

        return await WriteAddressesAsync(cartId, addresses, cartAuth, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Cart?> WriteAddressesAsync(
        string cartId,
        List<AddressRequest> addresses,
        AuthContext cartAuth,
        CancellationToken cancellationToken)
    {
        await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}",
                Auth = cartAuth,
                Content = EmporixJsonContent.Create(
                    new UpdateCart { Addresses = addresses },
                    CartJsonContext.Default.UpdateCart),
            },
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(cartId, cartAuth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a cart without the shopper guard, for calls a service token may make.
    /// </summary>
    private Task<Cart?> FetchAsync(
        string cartId,
        AuthContext auth,
        CancellationToken cancellationToken)
        => _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}",
                Auth = auth,
            },
            CartJsonContext.Default.Cart,
            cancellationToken);

    private static AddressRequestType? ToRequestType(AddressResponseType? type) => type switch
    {
        AddressResponseType.SHIPPING => AddressRequestType.SHIPPING,
        AddressResponseType.BILLING => AddressRequestType.BILLING,
        _ => null,
    };

    /// <summary>
    /// Narrows a returned address to what an update accepts. The server-owned
    /// fields (origin, site, legal entity) are not writable and are left out.
    /// </summary>
    private static AddressRequest ToRequest(AddressResponse address) => new()
    {
        ContactName = address.ContactName,
        CompanyName = address.CompanyName,
        Street = address.Street,
        StreetNumber = address.StreetNumber,
        StreetAppendix = address.StreetAppendix,
        ZipCode = address.ZipCode,
        City = address.City,
        Country = address.Country,
        State = address.State,
        ContactPhone = address.ContactPhone,
        Type = ToRequestType(address.Type),
        Metadata = address.Metadata,
        Mixins = address.Mixins,
    };

    /// <summary>
    /// Ensures the call carries a context a cart can belong to.
    /// </summary>
    /// <remarks>
    /// A cart always belongs to somebody — a signed-in customer or an anonymous
    /// session. A service token belongs to neither. Defaulting silently would
    /// attach the cart to the wrong party, and that only shows up once someone's
    /// cart is empty.
    /// </remarks>
    private static AuthContext RequireCartAuth(AuthContext auth)
        => auth.Kind is AuthKind.Customer or AuthKind.Anonymous
            ? auth
            : throw new EmporixConfigurationException(
                "Cart calls require an explicit customer or anonymous auth context. "
                + "A cart belongs to a person, not to a service.");
}

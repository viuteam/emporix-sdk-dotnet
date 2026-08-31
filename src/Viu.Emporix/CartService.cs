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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viu.Emporix.CartModels;

namespace Viu.Emporix;

/// <summary>
/// One operation of a cart command chain, run by <c>CartService.ExecuteAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written because the generated <see cref="ExecuteCommand"/> declares its
/// body as <c>object? Data</c>, which no source-generated serializer context can
/// write unless the runtime type inside happens to be registered — and a boxed
/// <see cref="JsonElement"/> is not. Here the body is a <see cref="JsonElement"/>,
/// which writes itself verbatim.
/// </para>
/// <para>
/// Build commands with the factories, one per command type and named after it.
/// They serialise each body with the type information the REST methods use, so
/// both paths send the same JSON, and they set every option explicitly: the
/// generated <see cref="ExecuteCommandOptions"/> starts with
/// <c>Partial = false</c> and <c>ExpandCalculation = true</c>, which would
/// otherwise travel with every command. A command type a later specification
/// adds is reachable through the initializer before it has a factory.
/// </para>
/// </remarks>
public sealed class CartCommand
{
    /// <summary>The operation to run.</summary>
    /// <remarks>
    /// Required, because the enum's default is its first member: a command built
    /// without a type would silently mean <see cref="ExecuteCommandType.AddCartItem"/>.
    /// </remarks>
    [JsonPropertyName("type")]
    public required ExecuteCommandType Type { get; init; }

    /// <summary>The request body of the equivalent REST operation, if it takes one.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>The remaining path and query parameters of the equivalent REST operation.</summary>
    /// <remarks>
    /// Set every property when you build one yourself: see the remarks on the
    /// class for the two defaults the generated type starts with.
    /// </remarks>
    [JsonPropertyName("options")]
    public ExecuteCommandOptions? Options { get; init; }

    /// <summary>Adds an item, as <see cref="CartService.AddItemAsync"/> does.</summary>
    /// <param name="item">The item. Its product reference must be a YRN — see <see cref="ProductYrn"/>.</param>
    /// <param name="resourceVersion">
    /// The cart version to check the write against: needed on every
    /// participating write under <see cref="Versioning.Explicit"/>, and on the
    /// first one under <see cref="Versioning.Follow"/>.
    /// </param>
    public static CartCommand AddCartItem(CartItemRequest item, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new CartCommand
        {
            Type = ExecuteCommandType.AddCartItem,
            Data = JsonSerializer.SerializeToElement(item, CartJsonContext.Default.CartItemRequest),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Changes an item, as <see cref="CartService.UpdateItemAsync"/> does.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="changes">The new values.</param>
    /// <param name="partial">
    /// <see langword="true"/> to change only the fields <paramref name="changes"/>
    /// sets. Emporix's default, <see langword="false"/>, replaces the whole item.
    /// </param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand UpdateCartItem(
        string itemId,
        CartModels.UpdateCartItem changes,
        bool? partial = null,
        int? resourceVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(changes);

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCartItem,
            Data = JsonSerializer.SerializeToElement(changes, CartJsonContext.Default.UpdateCartItem),
            Options = OptionsOrNull(itemId: itemId, partial: partial, resourceVersion: resourceVersion),
        };
    }

    /// <summary>Removes an item, as <see cref="CartService.RemoveItemAsync"/> does.</summary>
    /// <param name="itemId">The item id.</param>
    public static CartCommand DeleteCartItem(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartItem,
            Options = OptionsOrNull(itemId: itemId),
        };
    }

    /// <summary>Removes every item, as <see cref="CartService.ClearAsync"/> does.</summary>
    public static CartCommand DeleteCartItems()
        => new() { Type = ExecuteCommandType.DeleteCartItems };

    /// <summary>Reads the cart, as <see cref="CartService.GetAsync"/> does.</summary>
    /// <param name="expandCalculation">
    /// <see langword="false"/> for the cart without its calculation. Emporix
    /// calculates it by default.
    /// </param>
    /// <param name="zipCode">The shipping zip code to calculate with; give the country with it.</param>
    /// <param name="countryCode">The two-letter shipping country to calculate with; give the zip code with it.</param>
    /// <remarks>
    /// A <c>GetCart</c> followed by another write returns a cart that is already
    /// stale: put it last to read what the chain left behind.
    /// </remarks>
    /// <exception cref="ArgumentException">Only one of <paramref name="zipCode"/> and <paramref name="countryCode"/> is given.</exception>
    public static CartCommand GetCart(
        bool? expandCalculation = null,
        string? zipCode = null,
        string? countryCode = null)
    {
        if ((zipCode is null) != (countryCode is null))
        {
            throw new ArgumentException(
                "Give the zip code and the country code together, or neither.",
                zipCode is null ? nameof(zipCode) : nameof(countryCode));
        }

        return new CartCommand
        {
            Type = ExecuteCommandType.GetCart,
            Options = OptionsOrNull(
                expandCalculation: expandCalculation,
                zipCode: zipCode,
                countryCode: countryCode),
        };
    }

    /// <summary>Adds several items, as <see cref="CartService.AddItemsAsync"/> does.</summary>
    /// <param name="items">The items; at least one.</param>
    public static CartCommand AddCartItemsBatch(IEnumerable<CartItemRequest> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchRequest batch = [.. items];
        ArgumentOutOfRangeException.ThrowIfZero(batch.Count, nameof(items));

        return new CartCommand
        {
            Type = ExecuteCommandType.AddCartItemsBatch,
            Data = JsonSerializer.SerializeToElement(batch, CartJsonContext.Default.CartItemsBatchRequest),
        };
    }

    /// <summary>Changes several items, as <see cref="CartService.UpdateItemsAsync"/> does.</summary>
    /// <param name="items">The items in their new state; at least one.</param>
    /// <param name="partial"><see langword="true"/> to change only the fields each entry sets.</param>
    public static CartCommand UpdateCartItemsBatch(IEnumerable<CartItemRequest> items, bool? partial = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchUpdateRequest batch = [.. items];
        ArgumentOutOfRangeException.ThrowIfZero(batch.Count, nameof(items));

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCartItemsBatch,
            Data = JsonSerializer.SerializeToElement(batch, CartJsonContext.Default.CartItemsBatchUpdateRequest),
            Options = OptionsOrNull(partial: partial),
        };
    }

    /// <summary>Changes the cart itself, as <see cref="CartService.UpdateAsync"/> does.</summary>
    /// <param name="cart">The new values.</param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand UpdateCart(CartModels.UpdateCart cart, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCart,
            Data = JsonSerializer.SerializeToElement(cart, CartJsonContext.Default.UpdateCart),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Applies a discount: a coupon code, or an external discount.</summary>
    /// <param name="discount">
    /// The discount. A coupon is <c>new Discount { Code = "…" }</c>, which is what
    /// <see cref="CartService.ApplyCouponAsync"/> sends.
    /// </param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand ApplyCartDiscount(Discount discount, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(discount);

        return new CartCommand
        {
            Type = ExecuteCommandType.ApplyCartDiscount,
            Data = JsonSerializer.SerializeToElement(discount, CartJsonContext.Default.Discount),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Lists the cart's discounts, as <see cref="CartService.ListDiscountsAsync"/> does.</summary>
    public static CartCommand GetCartDiscounts()
        => new() { Type = ExecuteCommandType.GetCartDiscounts };

    /// <summary>
    /// Removes the discounts with these codes, or every discount, as
    /// <see cref="CartService.RemoveAllDiscountsAsync"/> does without codes.
    /// </summary>
    /// <param name="codes">The codes to remove; <see langword="null"/> removes every discount.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="codes"/> is empty or holds a blank code. Emporix reads a
    /// missing filter as «remove every discount», and a filter that came out
    /// empty by accident must not mean that.
    /// </exception>
    public static CartCommand DeleteCartDiscounts(IEnumerable<string>? codes = null)
    {
        List<string>? filter = codes is null ? null : [.. codes];

        if (filter is { Count: 0 } || filter?.Exists(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException(
                "Give at least one code, none of them blank, or null to remove every discount.",
                nameof(codes));
        }

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartDiscounts,
            Options = OptionsOrNull(codes: filter),
        };
    }

    /// <summary>Removes one discount, as <see cref="CartService.RemoveDiscountAsync"/> does.</summary>
    /// <param name="discountIndex">The index Emporix reports on the discount; read it before removing.</param>
    public static CartCommand DeleteCartDiscount(int discountIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discountIndex);

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartDiscount,

            // The specification types this option as a string; the REST path
            // segment it stands for is a number.
            Options = OptionsOrNull(discountIndex: discountIndex.ToString(CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>Recalculates the cart, as <see cref="CartService.RefreshAsync"/> does.</summary>
    public static CartCommand RefreshCart()
        => new() { Type = ExecuteCommandType.RefreshCart };

    /// <summary>Validates the cart, as <see cref="CartService.ValidateAsync"/> does.</summary>
    public static CartCommand ValidateCart()
        => new() { Type = ExecuteCommandType.ValidateCart };

    /// <summary>
    /// The options of a command, or <see langword="null"/> when there is nothing
    /// to send.
    /// </summary>
    /// <remarks>
    /// Sets all eight properties, the unused ones to <see langword="null"/>: the
    /// generated type starts with <c>Partial = false</c> and
    /// <c>ExpandCalculation = true</c>, and <c>WhenWritingNull</c> leaves out
    /// only what is null.
    /// </remarks>
    private static ExecuteCommandOptions? OptionsOrNull(
        string? itemId = null,
        bool? partial = null,
        bool? expandCalculation = null,
        string? zipCode = null,
        string? countryCode = null,
        int? resourceVersion = null,
        ICollection<string>? codes = null,
        string? discountIndex = null)
    {
        ExecuteCommandOptions options = new()
        {
            ItemId = itemId,
            Partial = partial,
            ExpandCalculation = expandCalculation,
            ZipCode = zipCode,
            CountryCode = countryCode,
            ResourceVersion = resourceVersion,
            Codes = codes,
            DiscountIndex = discountIndex,
        };

        return options is
        {
            ItemId: null,
            Partial: null,
            ExpandCalculation: null,
            ZipCode: null,
            CountryCode: null,
            ResourceVersion: null,
            Codes: null,
            DiscountIndex: null
        }
            ? null
            : options;
    }
}

/// <summary>The body of an execute request: the commands, and nothing else.</summary>
/// <remarks>
/// <c>onError</c> and <c>versioning</c> are query parameters, so they have no
/// place here.
/// </remarks>
internal sealed class CartCommandChain
{
    /// <summary>The commands, in the order they run.</summary>
    [JsonPropertyName("commands")]
    public required IReadOnlyList<CartCommand> Commands { get; init; }
}

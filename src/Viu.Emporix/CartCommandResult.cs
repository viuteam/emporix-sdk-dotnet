using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Viu.Emporix.CartModels;

/// <remarks>
/// <para>
/// <see cref="Data"/> carries the REST response body of whichever command ran,
/// so what it holds depends on <see cref="Type"/>. Each reader belongs to one
/// command type and refuses a result of another: reading a validation result as
/// a cart would fill a mostly empty <see cref="Cart"/> and say nothing.
/// </para>
/// <para>
/// A command that failed reads as <see langword="null"/>, or as an empty list:
/// its data is the REST error body, not the command's type. Check
/// <see cref="Code"/> to tell a failure from a command that returned nothing.
/// </para>
/// </remarks>
public partial class ExecuteCommandResult
{
    /// <summary>The cart a <c>GetCart</c> command read.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public Cart? ReadCart()
        => Read(nameof(ExecuteCommandType.GetCart), CartJsonContext.Default.Cart);

    /// <summary>The id and YRN of the item an <c>AddCartItem</c> command created.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public CreatedCartItem? ReadCreatedItem()
        => Read(nameof(ExecuteCommandType.AddCartItem), CartJsonContext.Default.CreatedCartItem);

    /// <summary>The outcome per item of an <c>AddCartItemsBatch</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<SingleBatchResponse> ReadAddedItems()
        => Read(nameof(ExecuteCommandType.AddCartItemsBatch), CartJsonContext.Default.BatchResponse) ?? [];

    /// <summary>The outcome per item of an <c>UpdateCartItemsBatch</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<UpdateCartItemsBatchEntryResponse> ReadUpdatedItems()
        => Read(
            nameof(ExecuteCommandType.UpdateCartItemsBatch),
            CartJsonContext.Default.CartItemsBatchUpdateResponse) ?? [];

    /// <summary>The discount an <c>ApplyCartDiscount</c> command applied.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public AppliedDiscount? ReadAppliedDiscount()
        => Read(nameof(ExecuteCommandType.ApplyCartDiscount), CartJsonContext.Default.AppliedDiscount);

    /// <summary>The discounts a <c>GetCartDiscounts</c> command listed.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<DiscountResponse> ReadDiscounts()
        => Read(nameof(ExecuteCommandType.GetCartDiscounts), CartJsonContext.Default.ListDiscountResponse) ?? [];

    /// <summary>The verdict of a <c>ValidateCart</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public CartValidationResult? ReadValidation()
        => Read(nameof(ExecuteCommandType.ValidateCart), CartJsonContext.Default.CartValidationResult);

    private T? Read<T>(string command, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!string.Equals(Type, command, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Result {Index} belongs to a {Type} command; this reader is for {command}.");
        }

        return Code is >= 200 and < 300
            && Data is JsonElement { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } body
            ? body.Deserialize(typeInfo)
            : null;
    }
}

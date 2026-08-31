using System.Text.Json.Serialization;

namespace Viu.Emporix;

/// <summary>
/// The request body for search endpoints that expect the filter in the body
/// rather than the address.
/// </summary>
/// <remarks>
/// Emporix bounds the length of an address; a search across a hundred ids does
/// not fit. These endpoints accept the same filter via <c>POST</c>.
/// </remarks>
internal sealed class SearchQueryBody
{
    [JsonPropertyName("q")]
    public required string Q { get; init; }
}

/// <summary>
/// Serialization for the product service.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated and therefore reflection-free, as ADR-0004 requires. Every
/// type that travels over the wire needs an entry — a missing one is reported by
/// the compiler, not by the runtime.
/// </para>
/// <para>
/// <b>One context per service, without exception.</b> The generator derives its
/// property names from type names, and Emporix reuses names across services:
/// <c>Metadata</c>, <c>Vendor</c>, <c>Price</c> and others exist in several
/// specifications. A shared context collides on those and would need a manual
/// override per clash — unworkable across 43 services. Grouping even three small
/// services was enough to collide on <c>Metadata</c>, which is why there is no
/// «these few are fine together» case.
/// <para>
/// For the same reason the entries are fully qualified rather than imported: two
/// services both define <c>Brand</c> and <c>Label</c>, so a <c>using</c> would be
/// ambiguous.
/// </para>
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SearchQueryBody))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductWithId))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.BasicProductWithId>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductUpdate))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.BasicProductCreation>))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.BasicProductBulkUpdate>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ResourceLocation))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.BulkResponse>))]
internal sealed partial class ProductJsonContext : JsonSerializerContext;

/// <summary>The identifier Emporix returns when a cart is created.</summary>
public sealed class CartCreated
{
    /// <summary>The id of the new cart.</summary>
    [JsonPropertyName("cartId")]
    public string? CartId { get; set; }
}

/// <summary>Serialization for the cart service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CartCreated))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CreateCart))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.Cart))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartItemRequest))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartItemResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.CartModels.CartItemResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.UpdateCartItem))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartValidationResult))]
internal sealed partial class CartJsonContext : JsonSerializerContext;

/// <summary>Serialization for the category service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.Category))]
[JsonSerializable(typeof(List<Viu.Emporix.CategoryModels.Category>))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryTree))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryPartialUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryAssignment))]
[JsonSerializable(typeof(List<Viu.Emporix.CategoryModels.CategoryAssignment>))]
internal sealed partial class CategoryJsonContext : JsonSerializerContext;

/// <summary>Serialization for the brand service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.BrandServiceModels.BrandResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.BrandServiceModels.BrandResponse>))]
[JsonSerializable(typeof(Viu.Emporix.BrandServiceModels.Brand))]
[JsonSerializable(typeof(Viu.Emporix.BrandServiceModels.UpdateBrand))]
internal sealed partial class BrandJsonContext : JsonSerializerContext;

/// <summary>Serialization for the label service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.LabelServiceModels.Label))]
[JsonSerializable(typeof(List<Viu.Emporix.LabelServiceModels.Label>))]
[JsonSerializable(typeof(Viu.Emporix.LabelServiceModels.LabelCreation))]
[JsonSerializable(typeof(Viu.Emporix.LabelServiceModels.LabelUpdate))]
internal sealed partial class LabelJsonContext : JsonSerializerContext;

/// <summary>Serialization for the catalog service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CatalogModels.Catalog))]
[JsonSerializable(typeof(List<Viu.Emporix.CatalogModels.Catalog>))]
[JsonSerializable(typeof(Viu.Emporix.CatalogModels.CreateCatalog))]
[JsonSerializable(typeof(Viu.Emporix.CatalogModels.CreateCatalogResponse))]
[JsonSerializable(typeof(Viu.Emporix.CatalogModels.UpdateCatalog))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;

/// <summary>Serialization for the customer service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CustomerLogin))]
[JsonSerializable(typeof(RefreshSessionBody))]
[JsonSerializable(typeof(PasswordResetRequest))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.Customer))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.AddressDto))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerModels.AddressDto>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.AddressCreateDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.AddressUpdateDto))]
internal sealed partial class CustomerJsonContext : JsonSerializerContext;

/// <summary>Serialization for the price service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.MatchByContext))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.Match))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.Match>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.GetPrice))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.GetPrice>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.CreatePrice))]
internal sealed partial class PriceJsonContext : JsonSerializerContext;

/// <summary>Serialization for the availability service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.AvailabilityModels.Availability))]
[JsonSerializable(typeof(List<Viu.Emporix.AvailabilityModels.Availability>))]
[JsonSerializable(typeof(Viu.Emporix.AvailabilityModels.AvailabilityDto))]
internal sealed partial class AvailabilityJsonContext : JsonSerializerContext;

/// <summary>Serialization for the checkout service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CheckoutModels.RequestCheckout))]
[JsonSerializable(typeof(Viu.Emporix.CheckoutModels.ResponseCheckout))]
internal sealed partial class CheckoutJsonContext : JsonSerializerContext;

/// <summary>Serialization for the order service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.Order))]
[JsonSerializable(typeof(List<Viu.Emporix.OrderV2Models.Order>))]
internal sealed partial class OrderJsonContext : JsonSerializerContext;

/// <summary>Serialization for the media service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.GetAsset))]
[JsonSerializable(typeof(List<Viu.Emporix.MediaModels.GetAsset>))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetCreateLink))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.GetAssetLink))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;

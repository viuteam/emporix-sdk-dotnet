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
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantRecalculationRequest))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantRecalculationResponse))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantRecalculationJobResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.DynamicVariantRecalculationJobResponse>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ProductTemplateResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.ProductTemplateResponse>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ProductTemplateCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ProductTemplateUpdate))]
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
[JsonSerializable(typeof(Viu.Emporix.CartModels.Discount))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.UpdateCart))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.Search))]
[JsonSerializable(typeof(List<Viu.Emporix.CartModels.BaseCartItemResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartItemsBatchRequest))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartItemsBatchUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.BatchResponse))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartItemsBatchUpdateResponse))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.ChangeSite))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.Body))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.MergeCart))]
[JsonSerializable(typeof(List<Viu.Emporix.CartModels.DiscountResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.CartDTRestrictions))]
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
[JsonSerializable(typeof(List<Viu.Emporix.CategoryModels.Category>))]
[JsonSerializable(typeof(List<Viu.Emporix.CategoryModels.CategoryTree>))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryTreeSearchRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.SearchRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.CategoryUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.BulkAssignmentRequest))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.BulkAssignmentUpsertRequest))]
[JsonSerializable(typeof(List<Viu.Emporix.CategoryModels.BulkAssignmentResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CategoryModels.AssignmentRequest))]
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
[JsonSerializable(typeof(Viu.Emporix.CatalogModels.UpdateCatalogProperties))]
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
[JsonSerializable(typeof(SocialLoginRequest))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.ValidateTokenResponse))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.PasswordChangeDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.PasswordUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.RefreshToken))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.ChangeEmailRequestDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerModels.UpdateEmail))]
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
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Viu.Emporix.AvailabilityModels.AvailabilityDto))]
[JsonSerializable(typeof(List<Viu.Emporix.AvailabilityModels.AvailabilityBulkDto>))]
[JsonSerializable(typeof(List<Viu.Emporix.AvailabilityModels.AvailabilityDeleteBulkDto>))]
[JsonSerializable(typeof(List<Viu.Emporix.AvailabilityModels.BulkResponse>))]
internal sealed partial class AvailabilityJsonContext : JsonSerializerContext;

/// <summary>Serialization for the checkout service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CheckoutModels.RequestCheckout))]
[JsonSerializable(typeof(Viu.Emporix.CheckoutModels.ResponseCheckout))]
[JsonSerializable(typeof(Viu.Emporix.CheckoutModels.RequestFromQuoteCheckout))]
internal sealed partial class CheckoutJsonContext : JsonSerializerContext;

/// <summary>Serialization for the order service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.Order))]
[JsonSerializable(typeof(List<Viu.Emporix.OrderV2Models.Order>))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.SalesOrder))]
[JsonSerializable(typeof(List<Viu.Emporix.OrderV2Models.SalesOrder>))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.Transition))]
[JsonSerializable(typeof(List<Viu.Emporix.OrderV2Models.Transition>))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.HistoricalTransitionsResponse))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.SearchRequest))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.SalesOrderCreationDto))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderCreationDto))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderUpdateDto))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderCalculationDto))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderEntriesDto))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderSplitRequest))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.OrderSplitResponse))]
[JsonSerializable(typeof(Viu.Emporix.OrderV2Models.ResourceLocation))]
internal sealed partial class OrderJsonContext : JsonSerializerContext;

/// <summary>Serialization for the media service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.GetAsset))]
[JsonSerializable(typeof(List<Viu.Emporix.MediaModels.GetAsset>))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetCreateLink))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.GetAssetLink))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetCreateBlob))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetUpdateBlob))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetUpdateLink))]
[JsonSerializable(typeof(Viu.Emporix.MediaModels.AssetReferenceUpdate))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;

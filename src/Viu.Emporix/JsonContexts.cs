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
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(EmporixProductConverter),
        typeof(EmporixProductCreationConverter),
        typeof(EmporixProductUpdateConverter),
        typeof(EmporixProductBulkUpdateConverter),
    ])]
[JsonSerializable(typeof(SearchQueryBody))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductWithId))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.BasicProductWithId>))]
// The resolving read returns the interface; the four types below never
// travelled over the wire before, because every read produced the basic shape.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProduct))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProduct>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductWithId))]
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
// The write bodies. Four of the five are a oneOf in the specification, so the
// parameter type is an interface and the concrete types below are what the
// converters serialize as.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductCreation))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProductCreation>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductBulkUpdate))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProductBulkUpdate>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductBulkUpdate))]
// PATCH /products/{productId} declares this single flat schema — not a oneOf.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ProductPartialUpdate))]
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
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.CreatePrice>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.SearchRequest))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.SearchPrices))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.MatchResponse>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.UpdatePrice))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.PriceBulkResponseEntry>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceModelDefinitionCreation))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceModelDefinitionCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceModelDefinitionRetrieval))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.PriceModelDefinitionRetrieval>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceList))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.PriceList>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceListCreation))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceListUpdate))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceListPrice))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.PriceListPrice>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceListPriceCreation))]
[JsonSerializable(typeof(List<Viu.Emporix.PriceModels.PriceListPriceCreation>))]
[JsonSerializable(typeof(Viu.Emporix.PriceModels.PriceListPriceUpdate))]
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

/// <summary>Serialization for the tax service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxRetrieval))]
[JsonSerializable(typeof(List<Viu.Emporix.TaxServiceModels.TaxRetrieval>))]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxCreation))]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxUpdate))]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxCalculationRequest))]
[JsonSerializable(typeof(Viu.Emporix.TaxServiceModels.TaxCalculationResponse))]
internal sealed partial class TaxJsonContext : JsonSerializerContext;

/// <summary>Serialization for the returns service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ReturnsModels.FullEmployeeReturn))]
[JsonSerializable(typeof(List<Viu.Emporix.ReturnsModels.FullEmployeeReturn>))]
[JsonSerializable(typeof(Viu.Emporix.ReturnsModels.BasicEmployeeReturn))]
[JsonSerializable(typeof(Viu.Emporix.ReturnsModels.UpdateEmployeeReturn))]
[JsonSerializable(typeof(Viu.Emporix.ReturnsModels.ReturnId))]
[JsonSerializable(typeof(List<Viu.Emporix.ReturnsModels.PatchOperation>))]
internal sealed partial class ReturnJsonContext : JsonSerializerContext;

/// <summary>Serialization for the invoice service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.InvoiceModels.JobRequest))]
[JsonSerializable(typeof(Viu.Emporix.InvoiceModels.JobCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.InvoiceModels.JobStatusResponse))]
internal sealed partial class InvoiceJsonContext : JsonSerializerContext;

/// <summary>Serialization for the coupon service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.Coupon))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.BaseCoupon))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.CouponCreation))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.CouponWithIdAndStatus))]
[JsonSerializable(typeof(List<Viu.Emporix.CouponModels.CouponWithIdAndStatus>))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.Redemption))]
[JsonSerializable(typeof(List<Viu.Emporix.CouponModels.Redemption>))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.RedemptionCreation))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.ReferralCoupon))]
[JsonSerializable(typeof(Viu.Emporix.CouponModels.ResourceLocation))]
internal sealed partial class CouponJsonContext : JsonSerializerContext;

/// <summary>Serialization for the fee service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.Fee))]
[JsonSerializable(typeof(List<Viu.Emporix.FeeModels.Fee>))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.FeeWithItems))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.ItemFee))]
[JsonSerializable(typeof(List<Viu.Emporix.FeeModels.ItemFee>))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.ItemFeeCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.FeeIdsUpdate))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.ItemYRNs))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.SearchItemFee))]
[JsonSerializable(typeof(Viu.Emporix.FeeModels.SearchItemsFee))]
internal sealed partial class FeeJsonContext : JsonSerializerContext;

/// <summary>Serialization for the payment service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.PaymentModeRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.PaymentMethodUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.PaymentModeResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.PaymentModels.PaymentModeResponse>))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.PaymentModeFrontendResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.PaymentModels.PaymentModeFrontendResponse>))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.InitializePaymentRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.InitializePaymentResponse))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.AuthorizePaymentRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.AuthorizeFrontendPaymentRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.AuthorizePaymentResponse))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.CaptureRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.RefundRequest))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.CommonPaymentResponse))]
[JsonSerializable(typeof(Viu.Emporix.PaymentModels.PaymentTransactionResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.PaymentModels.PaymentTransactionResponse>))]
internal sealed partial class PaymentJsonContext : JsonSerializerContext;

/// <summary>Serialization for the shipping service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.Site))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.Site>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.FindSiteRequest))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.ActualDeliveryWindow))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.ActualDeliveryWindow>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.DeliveryWindowValidationDto))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.DeliveryCycle))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.Zone))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.Zone>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.Method))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.Method>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.ResourceCreatedResponse))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.QuotePayload))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.QuoteSlot))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.QuoteResponseItem))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.QuoteResponseItem>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.MinimumFee))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.Group))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.Group>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.CGRelation))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.CGRelation>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.BasicDeliveryTime))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.BasicDeliveryTime>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.DeliveryTime))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.DeliveryTime>))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.UpdateDeliveryTime))]
[JsonSerializable(typeof(Viu.Emporix.ShippingModels.SlotCreation))]
[JsonSerializable(typeof(List<Viu.Emporix.ShippingModels.SlotCreation>))]
internal sealed partial class ShippingJsonContext : JsonSerializerContext;

/// <summary>Serialization for customer management. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.LegalEntity))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerManagementModels.LegalEntity>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.LegalEntityCreate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.LegalEntityUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.ContactAssignment))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerManagementModels.ContactAssignment>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.ContactAssignmentCreate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.ContactAssignmentUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.Location))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerManagementModels.Location>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.LocationCreate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.LocationUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.ResourceId))]
[JsonSerializable(typeof(Viu.Emporix.CustomerManagementModels.QParam))]
internal sealed partial class LegalEntityJsonContext : JsonSerializerContext;

/// <summary>Serialization for the quote service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(EmporixQuoteCreationConverter)])]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.QuoteModels.QuoteResponse>))]
// The create body is a oneOf over the two below: one quote built by hand, one
// built from a cart. The interface is the parameter type that carries either.
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.IEmporixQuoteCreation))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteCreateFromCartRequest))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteUpdateStatus))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteHistory))]
[JsonSerializable(typeof(List<Viu.Emporix.QuoteModels.QuoteHistory>))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteReasonResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.QuoteModels.QuoteReasonResponse>))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteReasonCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteReasonUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.QuoteModels.QuoteReasonIdResponse))]
internal sealed partial class QuoteJsonContext : JsonSerializerContext;

/// <summary>Serialization for the approval service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.GetApprovalResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.ApprovalServiceModels.GetApprovalResponse>))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.CreateCartApprovalRequest))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.CreateQuoteApprovalRequest))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.UpdateApprovalRequest))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.ApprovalPermittedRequest))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.ApprovalPermittedResponse))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.SearchUsersRequest))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.User))]
[JsonSerializable(typeof(List<Viu.Emporix.ApprovalServiceModels.User>))]
[JsonSerializable(typeof(Viu.Emporix.ApprovalServiceModels.CreatedResource))]
internal sealed partial class ApprovalJsonContext : JsonSerializerContext;

/// <summary>Serialization for the segment service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.SegmentResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.SegmentResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.SegmentCreation))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.SegmentCreation>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.SegmentUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.SegmentsSearch))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.Match))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.CustomerAssignmentUpsert))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.CustomerAssignmentUpsertBulk>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.CustomerAssignmentResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.CustomerAssignmentResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.ItemAssignmentUpsert))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.ItemAssignmentUpsertBulk>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.ItemAssignmentResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.ItemAssignmentResponse>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerSegmentModels.CategoryTreeResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.BulkAssignmentResponse>))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerSegmentModels.PatchOperation>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class SegmentJsonContext : JsonSerializerContext;

/// <summary>Serialization for customer administration. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.CustomerForSellerDto))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerServiceModels.CustomerForSellerDto>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.CustomerSignupBySellerDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.CustomerUpdateBySellerDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.CustomerPatchBySellerDto))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerServiceModels.CustomerImportDto>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.Address))]
[JsonSerializable(typeof(List<Viu.Emporix.CustomerServiceModels.Address>))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.Address_2))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.AddressUpdateDto))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.ResourceLocation))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.Body))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.PasswordMigrationRetentionConfigRequest))]
[JsonSerializable(typeof(Viu.Emporix.CustomerServiceModels.PasswordMigrationRetentionConfigResponse))]
internal sealed partial class CustomerAdminJsonContext : JsonSerializerContext;

/// <summary>Serialization for the site service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.SiteSettingsServiceModels.SiteDto))]
[JsonSerializable(typeof(List<Viu.Emporix.SiteSettingsServiceModels.SiteDto>))]
[JsonSerializable(typeof(Viu.Emporix.SiteSettingsServiceModels.ResourceLocation))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class SiteJsonContext : JsonSerializerContext;

/// <summary>Serialization for the vendor service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.Vendor))]
[JsonSerializable(typeof(List<Viu.Emporix.VendorServiceModels.Vendor>))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.VendorCreate))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.VendorUpdate))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.Location))]
[JsonSerializable(typeof(List<Viu.Emporix.VendorServiceModels.Location>))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.LocationCreate))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.LocationUpdate))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.ResourceId))]
[JsonSerializable(typeof(Viu.Emporix.VendorServiceModels.QParam))]
internal sealed partial class VendorJsonContext : JsonSerializerContext;

/// <summary>Serialization for the currency service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.CurrencyRetrieval))]
[JsonSerializable(typeof(List<Viu.Emporix.CurrencyServiceModels.CurrencyRetrieval>))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.CurrencyCreation))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.CurrencyCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.CurrencyUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.ExchangeRateRetrieval))]
[JsonSerializable(typeof(List<Viu.Emporix.CurrencyServiceModels.ExchangeRateRetrieval>))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.ExchangeRateCreationRequest))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.ExchangeRateUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.CurrencyServiceModels.ExchangeRateResponse))]
internal sealed partial class CurrencyJsonContext : JsonSerializerContext;

/// <summary>Serialization for the country service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.CountryServiceModels.Country))]
[JsonSerializable(typeof(List<Viu.Emporix.CountryServiceModels.Country>))]
[JsonSerializable(typeof(Viu.Emporix.CountryServiceModels.CountryUpdate))]
[JsonSerializable(typeof(Viu.Emporix.CountryServiceModels.Region))]
[JsonSerializable(typeof(List<Viu.Emporix.CountryServiceModels.Region>))]
internal sealed partial class CountryJsonContext : JsonSerializerContext;

/// <summary>Serialization for the IAM service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.UserResponse>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserExtendedResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.UserExtendedResponse>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.UserScopesResponse))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.GroupsQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.GroupsQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.GroupCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.GroupUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.GroupIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AssignmentQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.AssignmentQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AssignmentCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AssignmentIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AccessControlQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.AccessControlQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AccessControlUpsertRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.AccessControlIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.CustomScopeQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.CustomScopeQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.CustomScopeUpsertRequest))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.CustomScopeIdResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.PermissionQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.PermissionQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.ResourceQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.ResourceQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.RoleQueryDocument>))]
[JsonSerializable(typeof(Viu.Emporix.IamModels.RoleQueryDocument))]
[JsonSerializable(typeof(List<Viu.Emporix.IamModels.TemplateQueryDocument>))]
internal sealed partial class IamJsonContext : JsonSerializerContext;

/// <summary>Serialization for tenant configuration. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ConfigurationModels.BaseConfiguration))]
[JsonSerializable(typeof(List<Viu.Emporix.ConfigurationModels.BaseConfiguration>))]
[JsonSerializable(typeof(List<Viu.Emporix.ConfigurationModels.ClientConfiguration>))]
internal sealed partial class ConfigurationJsonContext : JsonSerializerContext;

/// <summary>Serialization for the session context. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.SessionContextModels.SessionContext_GET))]
[JsonSerializable(typeof(Viu.Emporix.SessionContextModels.SessionContext_PUT))]
[JsonSerializable(typeof(Viu.Emporix.SessionContextModels.SessionContext_PATCH))]
[JsonSerializable(typeof(Viu.Emporix.SessionContextModels.ContextAttribute))]
internal sealed partial class SessionContextJsonContext : JsonSerializerContext;

/// <summary>Serialization for the webhook service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.WebhookModels.ConfigurationGet))]
[JsonSerializable(typeof(List<Viu.Emporix.WebhookModels.ConfigurationGet>))]
[JsonSerializable(typeof(Viu.Emporix.WebhookModels.WebhookConfigCreation))]
[JsonSerializable(typeof(Viu.Emporix.WebhookModels.WebhookConfigUpdate))]
[JsonSerializable(typeof(List<Viu.Emporix.WebhookModels.WebhookConfigPartialUpdate>))]
[JsonSerializable(typeof(Viu.Emporix.WebhookModels.WebhookSubscriptions))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class WebhookJsonContext : JsonSerializerContext;

/// <summary>Serialization for the unit service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.Unit))]
[JsonSerializable(typeof(List<Viu.Emporix.UnitHandlingServiceModels.Unit>))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.BaseUnit))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.UpdateUnit))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.ConversionFactorPayload))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.ConversionFactorResponse))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.ConversionPayload))]
[JsonSerializable(typeof(Viu.Emporix.UnitHandlingServiceModels.ConversionResponse))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class UnitJsonContext : JsonSerializerContext;

/// <summary>Serialization for sequential identifiers. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.SequenceSchema))]
[JsonSerializable(typeof(List<Viu.Emporix.SequentialIdModels.SequenceSchema>))]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.SequenceSchemaCreate))]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.NextIdCommandRequest))]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.NextIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.SchemaBatchNextIdRequest))]
[JsonSerializable(typeof(Viu.Emporix.SequentialIdModels.SchemaBatchNextIdResponse))]
internal sealed partial class SequentialIdJsonContext : JsonSerializerContext;

/// <summary>Serialization for the schema service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.SchemaResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.SchemaModels.SchemaResponse>))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.SchemaCreation))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.SchemaUpdate))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.SchemaTypes))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.SchemaFileResponse))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.ReferenceCreation))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.ReferenceUpdate))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.CustomSchemaTypeResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.SchemaModels.CustomSchemaTypeResponse>))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.CustomSchemaTypeCreation))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.CustomSchemaTypeUpdate))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.BulkPatchCustomInstanceRequest))]
[JsonSerializable(typeof(List<Viu.Emporix.SchemaModels.BulkResponse>))]
[JsonSerializable(typeof(List<Viu.Emporix.SchemaModels.PatchOperation>))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.ExportImportRequest))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.ExportImportResponse))]
[JsonSerializable(typeof(Viu.Emporix.SchemaModels.IdResponse))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class SchemaJsonContext : JsonSerializerContext;


/// <summary>Serialization for the import service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportConfig))]
[JsonSerializable(typeof(List<Viu.Emporix.ImportServiceModels.ImportConfig>))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportStream))]
[JsonSerializable(typeof(List<Viu.Emporix.ImportServiceModels.ImportStream>))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.Schedule))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.Body))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportRun))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportRunPage))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.RunDetail))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.CancelResult))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ErrorRecordPage))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportedRecordPage))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportStats))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.JobGroup))]
[JsonSerializable(typeof(List<Viu.Emporix.ImportServiceModels.JobGroup>))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.HealthSettings))]
[JsonSerializable(typeof(Viu.Emporix.ImportServiceModels.ImportLicense))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class ImportJsonContext : JsonSerializerContext;

/// <summary>Serialization for the indexing service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.IndexConfiguration))]
[JsonSerializable(typeof(List<Viu.Emporix.IndexingServiceModels.IndexConfiguration>))]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.IndexPublicConfiguration))]
[JsonSerializable(typeof(List<Viu.Emporix.IndexingServiceModels.IndexPublicConfiguration>))]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.IndexCreationResponse))]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.Reindex))]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.ReindexRequest))]
[JsonSerializable(typeof(Viu.Emporix.IndexingServiceModels.ReindexJob))]
[JsonSerializable(typeof(List<Viu.Emporix.IndexingServiceModels.ReindexJob>))]
internal sealed partial class IndexingJsonContext : JsonSerializerContext;

/// <summary>Serialization for the shopping-list service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.OwnShoppingListCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.OwnShoppingListUpdateRequest))]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.EmployeeShoppingListCreateRequest))]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.GetShoppingList))]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.GetShoppingLists))]
[JsonSerializable(typeof(Viu.Emporix.ShoppingListModels.Response))]
internal sealed partial class ShoppingListJsonContext : JsonSerializerContext;

/// <summary>Serialization for the reward-points service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.CustomerSummaryBatchOut))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.PointsSummaryOut))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.NewCustomerIn))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.AddedPoints))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.RedeemedPoints))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.RedeemOption))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.RedeemOptions))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.RedeemOptionIn))]
[JsonSerializable(typeof(Viu.Emporix.RewardPointsModels.RedeemCouponOut))]
[JsonSerializable(typeof(int))]
internal sealed partial class RewardPointsJsonContext : JsonSerializerContext;

/// <summary>Serialization for the pick-pack service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.Order))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.OrderList))]
[JsonSerializable(typeof(List<Viu.Emporix.PickPackModels.OrderList>))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.OrderStatusChange))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.Assignee))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.PackagingProductsChange))]
[JsonSerializable(typeof(List<Viu.Emporix.PickPackModels.PackagingProductsChange>))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.OrderEntryEventCreate))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.OrderEntryEventResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.PickPackModels.OrderEntryEventResponse>))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.RecalculationJobCreation))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.RecalculationJob))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.Response))]
[JsonSerializable(typeof(Viu.Emporix.PickPackModels.Response4))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class PickPackJsonContext : JsonSerializerContext;

/// <summary>
/// The <c>{ "id": … }</c> the AI service answers a successful <c>PUT</c> with.
/// </summary>
/// <remarks>
/// The specification declares this shape as <c>IdResponse</c>, but the generator
/// emits that type without its property, so there is nothing to deserialise
/// into. Rather than reach into the generated type's extension data on every
/// call, the SDK declares the two fields itself. If a later specification sync
/// fixes the generated type, this can go.
/// </remarks>
internal sealed class AiIdResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>Serialization for the AI service. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AiIdResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiPatchOperation>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.TextGenerationRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.CompletionRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.GenerationResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgenticRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ChatResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.ChatResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.JobIdResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AttachmentResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.QParamSearchBody))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.AgentResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentFromTemplateRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentTemplateResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.AgentTemplateResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ConversationResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.ConversationResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.TokenRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.TokenResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.TokenResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.OAuthRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.OAuthResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.OAuthResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.SlackNativeToolRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.TeamsNativeToolRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.RagCustomNativeToolRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.RagEmporixNativeToolRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.CustomMcpServerRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.DynamicMcpServerRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ProviderModelsResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.ProviderModelsResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.CommerceEventsResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.Job))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.Job>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentRequestResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.AgentRequestResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentSessionResponse))]
[JsonSerializable(typeof(List<Viu.Emporix.AiServiceModels.AgentSessionResponse>))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.AgentAnalyticsResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ExecutionsResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ExportRequest))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ExportResponse))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.DataWithChecksum))]
[JsonSerializable(typeof(Viu.Emporix.AiServiceModels.ImportResponse))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class AiJsonContext : JsonSerializerContext;

/// <summary>Serialization for the RAG indexer. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.AiRagIndexerModels.MetadataFilter))]
[JsonSerializable(typeof(List<Viu.Emporix.AiRagIndexerModels.MetadataFilter>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class RagIndexerJsonContext : JsonSerializerContext;

/// <summary>
/// Serialization for cloud functions.
/// </summary>
/// <remarks>
/// Only <see cref="System.Text.Json.JsonElement"/>: the typed form of
/// <see cref="CloudFunctionService.InvokeAsync"/> takes its type information
/// from the caller, because there is no schema to generate from (ADR-0009).
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class CloudFunctionJsonContext : JsonSerializerContext;

/// <summary>Serialization for the audit log. See <see cref="ProductJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Viu.Emporix.AuditLogsChangelogModels.ChangelogHistoryResponse))]
internal sealed partial class AuditLogJsonContext : JsonSerializerContext;

/// <summary>
/// Serialization for the mixin writer.
/// </summary>
/// <remarks>
/// Its own context, as every service has: the writer assembles a map of mixin
/// keys to already-serialized values and needs type information for it. The
/// values arrive as <see cref="System.Text.Json.JsonElement"/>, so this context
/// never has to know a tenant's own shapes — those come from the caller's
/// context by way of the descriptor.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>))]
internal sealed partial class MixinJsonContext : JsonSerializerContext;

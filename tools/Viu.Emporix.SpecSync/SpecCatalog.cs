namespace Viu.Emporix.SpecSync;

/// <summary>An OpenAPI specification of an Emporix service.</summary>
/// <param name="Name">The short name; determines file name and output folder.</param>
/// <param name="Url">The address the specification lives at.</param>
internal sealed record SpecSource(string Name, string Url);

/// <summary>
/// The catalog of Emporix API specifications.
/// </summary>
/// <remarks>
/// The addresses point at <c>emporix/api-references</c>, the public repository
/// Emporix' own documentation is built from.
/// </remarks>
internal static class SpecCatalog
{
    private const string Base =
        "https://raw.githubusercontent.com/emporix/api-references/refs/heads/main";

    /// <summary>Every known specification, sorted by name.</summary>
    public static IReadOnlyList<SpecSource> All { get; } =
    [
        new("ai-rag-indexer", $"{Base}/artificial-intelligence/ai-rag-indexer/api-reference/api.yml"),
        new("ai-service", $"{Base}/artificial-intelligence/ai-service/api-reference/api.yml"),
        new("approval-service", $"{Base}/companies-and-customers/approval-service/approval-api-reference/api.yml"),
        new("audit-logs-changelog", $"{Base}/utilities/audit-logs-changelog/api-reference/api.yml"),
        new("availability", $"{Base}/orders/availability/api-reference/api.yml"),
        new("brand-service", $"{Base}/products-labels-and-brands/brand-service/api-reference/api.yml"),
        new("cart", $"{Base}/checkout/cart/api-reference/api.yml"),
        new("catalog", $"{Base}/catalogs-and-categories/catalog/api-reference/api.yml"),
        new("category", $"{Base}/catalogs-and-categories/category-tree/api-reference/api.yml"),
        new("checkout", $"{Base}/checkout/checkout/api-reference/api.yml"),
        new("configuration", $"{Base}/configuration/configuration-service/api-reference/api.yml"),
        new("country-service", $"{Base}/configuration/country-service/api-reference/api.yml"),
        new("coupon", $"{Base}/rewards-and-promotions/coupon/api-reference/api.yml"),
        new("currency-service", $"{Base}/configuration/currency-service/api-reference/api.yml"),
        new("customer", $"{Base}/companies-and-customers/customer-management/api-reference/api.yml"),

        // The B2B service for legal entities, contacts and locations lives
        // upstream under «client-management»; here it keeps the Node SDK's name.
        new("customer-management", $"{Base}/companies-and-customers/client-management/api-reference/api.yml"),

        new("customer-segment", $"{Base}/companies-and-customers/customer-segments/api-reference/api.yml"),
        new("customer-service", $"{Base}/companies-and-customers/customer-service/api-reference/api.yml"),
        new("fee", $"{Base}/checkout/fee/api-reference/api.yml"),
        new("iam", $"{Base}/users-and-permissions/iam/api-reference/api.yml"),
        new("import-service", $"{Base}/utilities/import-service/api-reference/api.yml"),
        new("indexing-service", $"{Base}/configuration/indexing-service/api-reference/api.yml"),

        // Three specifications use the .yaml extension upstream rather than
        // .yml — mind that when editing this list.
        new("invoice", $"{Base}/orders/invoice/api-reference/api.yaml"),

        new("label-service", $"{Base}/products-labels-and-brands/label-service/api-reference/api.yml"),
        new("media", $"{Base}/media/media/api-reference/api.yml"),
        new("oauth-service", $"{Base}/authentication/oauth-service/api-reference/api.yml"),
        new("order-v2", $"{Base}/orders/order/api-reference/api.yml"),
        new("payment", $"{Base}/checkout/payment-gateway/api-reference/api.yml"),
        new("pick-pack", $"{Base}/orders/pick-pack/api-reference/api.yml"),
        new("price", $"{Base}/prices-and-taxes/price-service/api-reference/api.yml"),
        new("product", $"{Base}/products-labels-and-brands/product-service/api-reference/api.yml"),
        new("quote", $"{Base}/quotes/quote/api-reference/api.yaml"),
        new("returns", $"{Base}/orders/returns/api-reference/api.yml"),
        new("reward-points", $"{Base}/rewards-and-promotions/reward-points/api-reference/api.yml"),
        new("schema", $"{Base}/utilities/schema/api-reference/api.yml"),
        new("sequential-id", $"{Base}/utilities/sequential-id/api-reference/api.yml"),
        new("session-context", $"{Base}/users-and-permissions/session-context/api-reference/api.yaml"),
        new("shipping", $"{Base}/delivery-and-shipping/shipping/api-reference/api.yml"),
        new("shopping-list", $"{Base}/checkout/shopping-list/api-reference/api.yml"),
        new("site-settings-service", $"{Base}/configuration/site-settings-service/api-reference/api.yml"),
        new("tax-service", $"{Base}/prices-and-taxes/tax-service/api-reference/api.yml"),
        new("unit-handling-service", $"{Base}/configuration/unit-handling-service/api-reference/api.yml"),
        new("vendor-service", $"{Base}/companies-and-customers/vendor-service/api-reference/api.yml"),
        new("webhook", $"{Base}/webhooks/webhook-service/api-reference/api.yml"),
    ];
}

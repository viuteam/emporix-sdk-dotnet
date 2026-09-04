using System.Diagnostics;
using Viu.Emporix;
using Viu.Emporix.SmokeTest;

// Walks the anonymous storefront flow against a real Emporix tenant and reports
// what each step did.
//
// This exists because a stubbed HttpMessageHandler cannot tell a right address
// from a wrong one. Six calls in this SDK once pointed at endpoints the API does
// not have, and every one of them had a passing unit test, because the test
// asserted the same wrong address the code built. SpecPathTests now catches that
// class of mistake against the specifications; this catches what only the live
// API knows — request bodies it rejects, responses that deserialise to nothing,
// and scopes a token turns out not to carry.
//
// Nothing here writes anything a tenant keeps: the cart it creates is deleted
// again, and no order is placed. Run it before releasing.
//
//   EMPORIX_TENANT=… EMPORIX_CLIENT_ID=… \
//   EMPORIX_SITE=main EMPORIX_CURRENCY=CHF EMPORIX_COUNTRY=CH \
//   dotnet run --project samples/Viu.Emporix.SmokeTest

Configuration? configuration = Configuration.FromEnvironment(out string? missing);

if (configuration is null)
{
    Console.WriteLine($"The smoke test needs {missing}.");
    Console.WriteLine();
    Console.WriteLine("  EMPORIX_TENANT     your tenant, lowercase");
    Console.WriteLine("  EMPORIX_CLIENT_ID  a storefront client id (anonymous sessions need no secret)");
    Console.WriteLine("  EMPORIX_SITE       the site code, e.g. main");
    Console.WriteLine("  EMPORIX_CURRENCY   ISO 4217, e.g. CHF        (optional, but prices need it)");
    Console.WriteLine("  EMPORIX_COUNTRY    ISO 3166-1, e.g. CH       (optional)");
    Console.WriteLine("  EMPORIX_PRODUCT_ID a product known to have a price (optional)");
    Console.WriteLine("  EMPORIX_HOST       override the API host      (optional)");
    Console.WriteLine();
    Console.WriteLine("  EMPORIX_BACKEND_CLIENT_ID  client credentials, for the seller-side pass (optional)");
    Console.WriteLine("  EMPORIX_BACKEND_SECRET     its secret — never commit this              (optional)");
    return 2;
}

using EmporixClient client = new(configuration.ToOptions());

Runner runner = new();
AuthContext shopper = AuthContext.Anonymous();

Console.WriteLine($"Tenant {configuration.Tenant}, site {configuration.Site} — anonymous flow");
Console.WriteLine();

// The first call is also the token check: it cannot succeed unless the anonymous
// session was minted and attached.
List<string> productIds = [];
bool priced = false;
(string Product, string PriceId, string? Currency, double? Original, double? Effective)? pricedProduct = null;

string? productId = await runner.RunAsync("anonymous session and product list", async () =>
{
    PaginatedItems<Viu.Emporix.ProductModels.BasicProductWithId> page =
        await client.Products.ListAsync(new ProductPageOptions { PageSize = 5 }, shopper);

    productIds.AddRange(page.Items.Select(p => p.Id).Where(id => id is { Length: > 0 })!);

    // A tenant may list plenty of products and have prices for none of them,
    // which makes the pricing steps prove nothing. EMPORIX_PRODUCT_ID names one
    // that is priced, so the flow can be walked to the end.
    if (configuration.ProductId is { Length: > 0 } known && !productIds.Contains(known))
    {
        productIds.Insert(0, known);
    }

    return productIds.Count == 0
        ? Step.Empty("no products in this tenant — the later steps have nothing to work with")
        : Step.Ok($"{page.Items.Count} products", productIds[0]);
});

// Not covered here: whether Emporix sends productType on a VARIANT. The
// specification leaves it optional there, and the resolving reads on
// Products.AnyType fall back to the basic shape without it. Establishing this
// needs a tenant with variant products — read one back through
// Products.AnyType.GetAsync and check the returned type is
// VariantProductWithId rather than BasicProductWithId.
//
// Two write questions that were open here have since been answered against the
// live viu tenant, on 2026-09-04, by a throwaway probe rather than by this
// file — the seller-side pass stays read-only by design:
//
//   PUT /products/bulk accepts a mixed array. One BASIC and one BUNDLE in a
//   single call came back as two entries, which is what the specification's
//   array of oneOf promised and nothing had confirmed.
//
//   PATCH silently discards productType. A BASIC product patched with
//   productType BUNDLE answered 204 and read back as BASIC, unchanged. So the
//   field the SDK used to send in that body was never doing anything, and no
//   caller was harmed by it.
await runner.RunAsync("fetch one product", async () =>
{
    if (productId is null)
    {
        return Step.Skipped("no product id from the previous step");
    }

    Viu.Emporix.ProductModels.BasicProductWithId? product =
        await client.Products.GetAsync(productId, shopper);

    // A null here is the failure the unit tests cannot see: a 200 whose body
    // does not fit the model deserialises to nothing at all.
    return product?.Id is { Length: > 0 }
        ? Step.Ok("read back by id")
        : Step.Failed("the response did not deserialise into a product");
});

await runner.RunAsync("categories", async () =>
{
    PaginatedItems<Viu.Emporix.CategoryModels.Category> page =
        await client.Categories.ListAsync(new CategoryPageOptions { PageSize = 5 }, shopper);

    return Step.Ok($"{page.Items.Count} categories");
});

await runner.RunAsync("price matching by context", async () =>
{
    if (productId is null)
    {
        return Step.Skipped("no product id");
    }

    // Every product on the page, not just the first: one product without a
    // price says nothing about whether pricing works.
    IReadOnlyList<Viu.Emporix.PriceModels.Items> items =
    [
        .. productIds.Select(id => new Viu.Emporix.PriceModels.Items
        {
            ItemId = new Viu.Emporix.PriceModels.ItemId5 { Id = id, ItemType = "PRODUCT" },

            // Required: without it Emporix answers «Quantity must not be null».
            Quantity = new Viu.Emporix.PriceModels.MatchMeasurementUnit { Quantity = 1 },
        }),
    ];

    IReadOnlyList<Viu.Emporix.PriceModels.MatchResponse> matches =
        await client.Prices.MatchByContextChunkedAsync(items, auth: shopper);

    // An empty list is not an error here, but it is the exact symptom of a
    // token minted without currency, site and country — worth saying out loud
    // rather than reporting a pass.
    // The priceId is what a cart item needs, so the matched product and its
    // price are carried forward rather than just a yes-or-no.
    foreach (Viu.Emporix.PriceModels.MatchResponse match in matches)
    {
        if (match.PriceId is { Length: > 0 } id && match.ItemId?.Id is { Length: > 0 } item)
        {
            pricedProduct = (item, id, match.Currency, match.OriginalValue, match.EffectiveValue);
            break;
        }
    }

    priced = matches.Count > 0;

    return priced
        ? Step.Ok(
            $"{matches.Count} price(s) for {items.Count} product(s)"
            + (pricedProduct is { } p ? $", first on {p.Product}" : ", none usable for a cart item"))
        : Step.Empty(
            $"no prices for any of {items.Count} products — either none are configured "
            + "for this currency and site, or the token carries no context");
});

await runner.RunAsync("availability", async () =>
{
    if (productId is null)
    {
        return Step.Skipped("no product id");
    }

    Viu.Emporix.AvailabilityModels.Availability? availability =
        await client.Availability.GetAsync(
            productId,
            configuration.Site,
            treatMissingAsAvailable: true,
            auth: shopper);

    return Step.Ok(availability?.Available == true ? "available" : "not available");
});

string? cartId = await runner.RunAsync("create a cart", async () =>
{
    CartCreated? cart = await client.Carts.CreateAsync(
        new Viu.Emporix.CartModels.CreateCart
        {
            SiteCode = configuration.Site,

            // Required: a cart without a currency cannot price anything.
            Currency = configuration.Currency ?? "CHF",
        },
        shopper);

    return cart?.CartId is { Length: > 0 } id
        ? Step.Ok("created", id)
        : Step.Failed("no cart id came back");
});

await runner.RunAsync("add an item to the cart", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("no cart");
    }

    if (pricedProduct is not { } match)
    {
        // Emporix refuses an item without a price id, and that id comes from the
        // match that found nothing. Reporting this as a failure would blame the
        // SDK for a tenant with no prices configured.
        return Step.Skipped("no price was matched, and a cart item needs one");
    }

    // Emporix identifies the item by its YRN, not by the bare id — the guard in
    // CartService rejects a bare id before the request leaves. The price id
    // comes from the match: without it the item is rejected as «Internal type
    // must have priceId set».
    Viu.Emporix.CartModels.CartItemResponse? item = await client.Carts.AddItemAsync(
        cartId,
        new Viu.Emporix.CartModels.CartItemRequest
        {
            ItemYrn = ProductYrn.Create(configuration.Tenant, match.Product),
            Quantity = 1,
            // Emporix wants the price the storefront showed, not just its id:
            // it checks that the cart agrees with what the customer saw.
            Price = new Viu.Emporix.CartModels.PriceRowItem
            {
                PriceId = match.PriceId,
                Currency = match.Currency ?? configuration.Currency ?? "CHF",
                OriginalAmount = match.Original ?? 0,
                EffectiveAmount = match.Effective ?? match.Original ?? 0,
            },
        },
        shopper);

    return item is null
        ? Step.Failed("the item did not come back")
        : Step.Ok("added");
});

await runner.RunAsync("read the current cart", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("no cart");
    }

    Viu.Emporix.CartModels.Cart? cart = await client.Carts.GetCurrentAsync(
        new CurrentCartQuery { SiteCode = configuration.Site },
        shopper);

    return cart?.Id == cartId
        ? Step.Ok("the session resolves to the cart just created")
        : Step.Failed($"expected the new cart, got {cart?.Id ?? "nothing"}");
});

await runner.RunAsync("shipping zones for the site", async () =>
{
    IReadOnlyList<Viu.Emporix.ShippingModels.Zone> zones =
        await client.Shipping.ForSite(configuration.Site).ListZonesAsync(shopper);

    // No zone means nothing can be delivered from this site, which is worth
    // saying out loud rather than passing on an empty list.
    return zones.Count > 0
        ? Step.Ok($"{zones.Count} zone(s)")
        : Step.Empty("no shipping zones — nothing is deliverable from this site");
});

await runner.RunAsync("shipping quote", async () =>
{
    // A quote takes a total and a destination, not a cart — the endpoint is
    // usable before a cart exists, which is why a product page can show
    // «delivery from».
    Viu.Emporix.ShippingModels.QuotePayload payload = new()
    {
        CartTotal = new Viu.Emporix.ShippingModels.CartTotal
        {
            Amount = 50,
            Currency = configuration.Currency ?? "CHF",
        },
        ShipToAddress = new Viu.Emporix.ShippingModels.Address
        {
            Street = "Rennweg",
            StreetNumber = "38",
        },
    };

    try
    {
        IReadOnlyList<Viu.Emporix.ShippingModels.QuoteResponseItem> quote =
            await client.Shipping.ForSite(configuration.Site).QuoteAsync(payload, shopper);

        return quote.Count > 0
            ? Step.Ok($"{quote.Count} method(s) offered")
            : Step.Empty("no delivery method applies to this total and address");
    }
    catch (EmporixValidationException exception)
        when (exception.Message.Contains("No matching method", StringComparison.OrdinalIgnoreCase))
    {
        // Emporix answers «we do not deliver there» with a 400, not an empty
        // list. The SDK is right to surface that as an exception — a malformed
        // request looks the same to it — but for this smoke test it means the
        // address used here is outside the tenant's zone, not that the call is
        // broken. The rest of the step ran: the request was accepted, routed
        // and answered.
        return Step.Empty(
            "the address used here is outside this tenant's delivery zone — "
            + "the call itself reached the service and was understood");
    }
});

await runner.RunAsync("payment methods a shopper may see", async () =>
{
    // The reduced view. Reaching the configured one from a storefront token
    // would be a leak, and Emporix refuses it — which is the point.
    IReadOnlyList<Viu.Emporix.PaymentModels.PaymentModeFrontendResponse> modes =
        await client.Payments.Modes.ListForFrontendAsync(shopper);

    return modes.Count > 0
        ? Step.Ok($"{modes.Count} method(s)")
        : Step.Empty("no payment method is enabled for this tenant");
});

await runner.RunAsync("validate the cart", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("no cart");
    }

    await client.Carts.ValidateAsync(cartId, shopper);
    return Step.Ok("accepted");
});

await runner.RunAsync("delete the cart", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("nothing to clean up");
    }

    await client.Carts.DeleteAsync(cartId, shopper);
    return Step.Ok("cleaned up");
});

// ---------------------------------------------------------------------------
// The seller's side. Everything above ran on an anonymous storefront token;
// none of it can reach a service configured by a seller. This second pass needs
// client credentials, and it runs only when they are there.
//
// Read-only by design. Every call below is a GET or a search: the point is to
// prove the addresses, the scopes and the response shapes, not to leave anything
// behind in a tenant.
if (!configuration.HasBackendCredentials)
{
    Console.WriteLine();
    Console.WriteLine(
        "Skipping the service-token pass — set EMPORIX_BACKEND_CLIENT_ID and");
    Console.WriteLine(
        "EMPORIX_BACKEND_SECRET to also check the services a seller configures.");
    Console.WriteLine();
    return runner.Report();
}

Console.WriteLine();
Console.WriteLine("Service token — the seller's side (read-only)");
Console.WriteLine();

AuthContext service = AuthContext.Service();

await runner.RunAsync("service token and tax configuration", async () =>
{
    // Also the token check for this pass: a wrong secret fails here, before
    // anything else has a chance to look like the problem.
    IReadOnlyList<Viu.Emporix.TaxServiceModels.TaxRetrieval> taxes =
        await client.Taxes.ListAsync(auth: service);

    return taxes.Count > 0
        ? Step.Ok($"{taxes.Count} tax configuration(s)")
        : Step.Empty("no tax configuration on this tenant");
});

await runner.RunAsync("sites", async () =>
{
    IReadOnlyList<Viu.Emporix.SiteSettingsServiceModels.SiteDto> sites =
        await client.Sites.ListAsync(auth: service);

    return sites.Count > 0
        ? Step.Ok(string.Join(", ", sites.Select(s => s.Code).Take(5)))
        : Step.Empty("no site is configured");
});

await runner.RunAsync("shipping zones for the site", async () =>
{
    IReadOnlyList<Viu.Emporix.ShippingModels.Zone> zones =
        await client.Shipping.ForSite(configuration.Site).ListZonesAsync(auth: service);

    return zones.Count > 0
        ? Step.Ok($"{zones.Count} zone(s)")
        : Step.Empty($"site {configuration.Site} has no shipping zone");
});

await runner.RunAsync("IAM groups", async () =>
{
    PaginatedItems<Viu.Emporix.IamModels.GroupsQueryDocument> groups =
        await client.Iam.Groups.ListAsync(pageSize: 5, auth: service);

    return groups.Items.Count > 0
        ? Step.Ok($"{groups.Items.Count} group(s) on the first page")
        : Step.Empty("no IAM group is defined");
});

await runner.RunAsync("custom entity types", async () =>
{
    IReadOnlyList<Viu.Emporix.SchemaModels.CustomSchemaTypeResponse> types =
        await client.Schemas.CustomEntities.ListAsync(auth: service);

    return types.Count > 0
        ? Step.Ok($"{types.Count} type(s)")
        : Step.Empty("this tenant defines no custom entity types");
});

// ---- Wave 5. None of these had ever been called live before this pass. ----

await runner.RunAsync("import configurations", async () =>
{
    IReadOnlyList<Viu.Emporix.ImportServiceModels.ImportConfig> configs =
        await client.Imports.ListConfigsAsync(service);

    return configs.Count > 0
        ? Step.Ok($"{configs.Count} configuration(s)")
        : Step.Empty("the import tool is not configured on this tenant");
});

await runner.RunAsync("public index configuration", async () =>
{
    // Deliberately the public variant: the full one carries write keys, and a
    // smoke test has no business printing those.
    IReadOnlyList<Viu.Emporix.IndexingServiceModels.IndexPublicConfiguration> indexes =
        await client.Indexing.ListPublicConfigurationsAsync(service);

    return indexes.Count > 0
        ? Step.Ok(string.Join(", ", indexes.Select(i => i.Provider?.ToString()).Take(3)))
        : Step.Empty("no search provider is configured");
});

await runner.RunAsync("reward redemption options", async () =>
{
    IReadOnlyList<Viu.Emporix.RewardPointsModels.RedeemOption> options =
        await client.RewardPoints.ListRedeemOptionsAsync(service);

    return options.Count > 0
        ? Step.Ok($"{options.Count} option(s)")
        : Step.Empty("loyalty is not configured on this tenant");
});

await runner.RunAsync("AI agents", async () =>
{
    IReadOnlyList<Viu.Emporix.AiServiceModels.AgentResponse> agents =
        await client.Ai.Agents.ListAsync(new AiListOptions { PageSize = 5 }, service);

    return agents.Count > 0
        ? Step.Ok($"{agents.Count} agent(s)")
        : Step.Empty("no agent is configured");
});

await runner.RunAsync("AI tools", async () =>
{
    // The one that returns JSON rather than a type, because four shapes share
    // the endpoint. Worth exercising: if the union ever gains a discriminator,
    // this is where it would show.
    System.Text.Json.JsonElement tools =
        await client.Ai.Tools.ListAsync(new AiListOptions { PageSize = 5 }, service);

    return tools.ValueKind == System.Text.Json.JsonValueKind.Array
        ? Step.Ok($"{tools.GetArrayLength()} tool(s)")
        : Step.Empty($"the endpoint answered with {tools.ValueKind}, not an array");
});

await runner.RunAsync("shopping lists", async () =>
{
    IReadOnlyList<Viu.Emporix.ShoppingListModels.GetShoppingList> lists =
        await client.ShoppingLists.ListAsync(auth: service);

    return lists.Count > 0
        ? Step.Ok($"{lists.Count} customer(s) with a list")
        : Step.Empty("nobody has a shopping list on this tenant");
});

await runner.RunAsync("audit log, last 30 days", async () =>
{
    // Unfiltered means «the last 30 days», not «everything» — Emporix applies
    // that window itself. An empty answer here is a quiet tenant, not a fault.
    Viu.Emporix.AuditLogsChangelogModels.ChangelogHistoryResponse? page =
        await client.AuditLogs.ListAsync(size: 5, auth: service);

    return page is { Items.Count: > 0 }
        ? Step.Ok($"{page.TotalElements} change(s) in the window")
        : Step.Empty("no change recorded in the last 30 days");
});

Console.WriteLine();
return runner.Report();

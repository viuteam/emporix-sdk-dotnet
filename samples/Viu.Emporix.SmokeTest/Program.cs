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
    Console.WriteLine("  EMPORIX_HOST       override the API host      (optional)");
    return 2;
}

using EmporixClient client = new(configuration.ToOptions());

Runner runner = new();
AuthContext shopper = AuthContext.Anonymous();

Console.WriteLine($"Tenant {configuration.Tenant}, site {configuration.Site} — anonymous flow");
Console.WriteLine();

// The first call is also the token check: it cannot succeed unless the anonymous
// session was minted and attached.
string? productId = await runner.RunAsync("anonymous session and product list", async () =>
{
    PaginatedItems<Viu.Emporix.ProductModels.BasicProductWithId> page =
        await client.Products.ListAsync(new ProductPageOptions { PageSize = 5 }, shopper);

    return page.Items.Count == 0
        ? Step.Empty("no products in this tenant — the later steps have nothing to work with")
        : Step.Ok($"{page.Items.Count} products", page.Items[0].Id);
});

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

    IReadOnlyList<Viu.Emporix.PriceModels.Match> matches = await client.Prices.MatchByContextChunkedAsync(
        [new Viu.Emporix.PriceModels.Items
        {
            ItemId = new Viu.Emporix.PriceModels.ItemId4 { Id = productId, ItemType = "PRODUCT" },
        }],
        auth: shopper);

    // An empty list is not an error here, but it is the exact symptom of a
    // token minted without currency, site and country — worth saying out loud
    // rather than reporting a pass.
    return matches.Count > 0
        ? Step.Ok($"{matches.Count} price(s)")
        : Step.Empty("no prices — check EMPORIX_CURRENCY, EMPORIX_SITE and EMPORIX_COUNTRY");
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
        new Viu.Emporix.CartModels.CreateCart { SiteCode = configuration.Site },
        shopper);

    return cart?.CartId is { Length: > 0 } id
        ? Step.Ok("created", id)
        : Step.Failed("no cart id came back");
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

Console.WriteLine();
return runner.Report();

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Viu.Emporix;
using Viu.Emporix.CartModels;
using Viu.Emporix.PriceModels;
using Viu.Emporix.ProductModels;
using Viu.Emporix.Storefront;

// A storefront backend on top of the SDK — the shape most consumers will build.
//
// What it shows that the console samples cannot:
//
//   * registration through services.AddEmporix, so one client instance is shared
//     and its connections are pooled;
//   * an AuthContext derived per request rather than held on the client, which
//     is what makes one instance safe for many concurrent shoppers;
//   * SDK exceptions translated into HTTP status codes once, in a filter,
//     instead of a try/catch in every endpoint.
//
// Configure it the same way as the smoke test:
//
//   EMPORIX_TENANT=… EMPORIX_CLIENT_ID=… EMPORIX_SITE=main EMPORIX_CURRENCY=CHF \
//   dotnet run --project samples/Viu.Emporix.Storefront

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

string tenant = builder.Configuration["EMPORIX_TENANT"]
    ?? throw new InvalidOperationException("EMPORIX_TENANT is not set.");
string clientId = builder.Configuration["EMPORIX_CLIENT_ID"]
    ?? throw new InvalidOperationException("EMPORIX_CLIENT_ID is not set.");
string site = builder.Configuration["EMPORIX_SITE"] ?? "main";

builder.Services.AddEmporix(options =>
{
    options.Tenant = tenant;
    options.Credentials.Storefront = new EmporixStorefrontCredentials
    {
        ClientId = clientId,

        // Currency, site and country are bound into the anonymous token. Without
        // them a price lookup answers with an empty list and no error.
        Context = new EmporixStorefrontContext
        {
            SiteCode = site,
            Currency = builder.Configuration["EMPORIX_CURRENCY"],
            TargetLocation = builder.Configuration["EMPORIX_COUNTRY"],
        },
    };
});

// Source-generated serialization, because the SDK requires it and a host that
// fell back on reflection would not publish AOT.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, StorefrontJsonContext.Default));

WebApplication app = builder.Build();

// One place decides what an SDK failure means over HTTP. Repeating this per
// endpoint is how a 404 ends up reported as a 500.
app.UseExceptionHandler(handler => handler.Run(EmporixProblem.WriteAsync));

// ---------------------------------------------------------------- catalogue --

app.MapGet("/products", async (
    EmporixClient emporix,
    HttpContext context,
    int page = 1,
    int size = 20,
    CancellationToken cancellationToken = default) =>
{
    PaginatedItems<BasicProductWithId> products = await emporix.Products.ListAsync(
        new ProductPageOptions { PageNumber = page, PageSize = size },
        context.Shopper(),
        cancellationToken);

    return TypedResults.Ok(products.Items.Select(ProductView.From).ToList());
});

app.MapGet("/products/{id}", async Task<Results<Ok<ProductView>, NotFound>> (
    string id,
    EmporixClient emporix,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    BasicProductWithId? product =
        await emporix.Products.GetAsync(id, context.Shopper(), cancellationToken);

    // A missing product is a 404 from Emporix and reaches the filter as one; a
    // null body is the other way it can be absent.
    return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductView.From(product));
});

app.MapGet("/products/{id}/availability", async (
    string id,
    EmporixClient emporix,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    // «No stock record» is not «no such product»: a storefront wants false
    // here, not a 404 the caller has to interpret. The SDK makes that a
    // decision rather than a default.
    Viu.Emporix.AvailabilityModels.Availability? availability = await emporix.Availability.GetAsync(
        id,
        site,
        treatMissingAsAvailable: true,
        context.Shopper(),
        cancellationToken);

    return TypedResults.Ok(availability?.Available == true);
});

// -------------------------------------------------------------------- cart --

app.MapPost("/carts", async (
    EmporixClient emporix,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    CartCreated? cart = await emporix.Carts.CreateAsync(
        new CreateCart
        {
            SiteCode = site,
            Currency = builder.Configuration["EMPORIX_CURRENCY"] ?? "CHF",
        },
        context.Shopper(),
        cancellationToken);

    return TypedResults.Ok(cart?.CartId);
});

app.MapGet("/carts/{id}", async Task<Results<Ok<Cart>, NotFound>> (
    string id,
    EmporixClient emporix,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    Cart? cart = await emporix.Carts.GetAsync(id, context.Shopper(), cancellationToken);

    return cart is null ? TypedResults.NotFound() : TypedResults.Ok(cart);
});

app.MapPost("/carts/{id}/items", async (
    string id,
    AddItem request,
    EmporixClient emporix,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    // The price comes from a match, not from the caller: letting a client name
    // its own price is how a storefront sells at whatever the browser says.
    IReadOnlyList<MatchResponse> prices = await emporix.Prices.MatchByContextChunkedAsync(
        [
            new Items
            {
                ItemId = new ItemId5 { Id = request.ProductId, ItemType = "PRODUCT" },
                Quantity = new MatchMeasurementUnit { Quantity = request.Quantity },
            },
        ],
        auth: context.Shopper(),
        cancellationToken: cancellationToken);

    if ((prices.Count > 0 ? prices[0] : null) is not { PriceId.Length: > 0 } price)
    {
        return Results.Problem(
            "No price is configured for this product in this currency and site.",
            statusCode: StatusCodes.Status409Conflict);
    }

    await emporix.Carts.AddItemAsync(
        id,
        new CartItemRequest
        {
            ItemYrn = ProductYrn.Create(tenant, request.ProductId),
            Quantity = request.Quantity,
            Price = new PriceRowItem
            {
                PriceId = price.PriceId,
                Currency = price.Currency ?? "CHF",
                OriginalAmount = price.OriginalValue ?? 0,
                EffectiveAmount = price.EffectiveValue ?? price.OriginalValue ?? 0,
            },
        },
        context.Shopper(),
        cancellationToken);

    return Results.NoContent();
});

app.Run();

/// <summary>What the storefront exposes of a product.</summary>
/// <param name="Id">The product id.</param>
/// <param name="Code">The code the tenant assigns.</param>
/// <param name="Name">The name, in whatever languages Emporix returned.</param>
/// <remarks>
/// Deliberately not the SDK model: a storefront's own shape survives an SDK
/// upgrade, and this is where <see cref="LocalizedString"/> is resolved to the
/// single text a page needs.
/// </remarks>
internal sealed record ProductView(string? Id, string? Code, string? Name)
{
    public static ProductView From(BasicProductWithId product)
        => new(product.Id, product.Code, product.Name?.ToString());
}

/// <summary>A request to put something in a cart.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">How many.</param>
internal sealed record AddItem(string ProductId, int Quantity);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<ProductView>))]
[JsonSerializable(typeof(ProductView))]
[JsonSerializable(typeof(LocalizedString))]
[JsonSerializable(typeof(AddItem))]
[JsonSerializable(typeof(ProblemResponse))]
[JsonSerializable(typeof(Cart))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal sealed partial class StorefrontJsonContext : JsonSerializerContext;

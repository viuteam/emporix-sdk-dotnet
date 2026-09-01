# Viu.Emporix

.NET SDK for the [Emporix Commerce Engine](https://emporix.io). Targets .NET 10,
is Native AOT compatible, and keeps dependencies to a minimum.

> **Not an official Emporix product.** This SDK is built and maintained by
> [viu](https://www.viu.ch). It is neither published nor supported by Emporix AG.
> «Emporix» is a trademark of its respective owner and is used here solely to
> describe compatibility.

## Installation

```bash
dotnet add package Viu.Emporix
```

## Quick start

### With dependency injection

```csharp
services.AddEmporix(options =>
{
    options.Tenant = "mytenant";

    // For server-side calls.
    options.Credentials.Backend = new EmporixServiceCredentials
    {
        ClientId = "...",
        Secret = "...",
    };

    // For anonymous catalog browsing.
    options.Credentials.Storefront = new EmporixStorefrontCredentials
    {
        ClientId = "...",
        Context = new EmporixStorefrontContext
        {
            Currency = "CHF",
            SiteCode = "main",
            TargetLocation = "CH",
        },
    };
});
```

Then take the `EmporixClient`:

```csharp
public sealed class CatalogController(EmporixClient emporix)
{
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var product = await emporix.Products.GetAsync(id, cancellationToken: cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}
```

An incomplete configuration fails **at application startup**, not on the first
call.

### Without a container

```csharp
using EmporixClient client = new(new EmporixOptions
{
    Tenant = "mytenant",
    Credentials = { Storefront = new EmporixStorefrontCredentials { ClientId = "..." } },
});

await foreach (var product in client.Products.ListAllAsync())
{
    Console.WriteLine(product.Code);
}
```

## Authentication

What a call is authorised with lives **in the call**, not on the client. That is
why one instance safely serves many concurrent users — including a server acting
for a different customer on every request.

| Call | Who owns the token | Used for |
| --- | --- | --- |
| `AuthContext.Anonymous()` | the SDK | browsing the catalog without a sign-in |
| `AuthContext.Service()` | the SDK | server-side writes |
| `AuthContext.Service("partner")` | the SDK | a named credential set |
| `AuthContext.Customer(token)` | the calling application | signed-in customers, personalised prices |
| `AuthContext.Raw(token)` | the calling application | tokens from SSO or token exchange |

When omitted, every method picks the fitting default: reads go anonymous, writes
use a service token.

```csharp
// Anonymous — the default.
var page = await client.Products.ListAsync();

// With a customer token, for personalised prices.
var mine = await client.Products.GetAsync("p1", AuthContext.Customer(customerToken));

// Server-side write.
await client.Products.CreateAsync(product);
```

For tokens the SDK owns, a 401 triggers **one** fresh token and a retry. A
customer token belongs to the calling application; its 401 is passed through —
unless an `ICustomerTokenRefresher` is registered.

## Products

```csharp
// Single item.
var product = await client.Products.GetAsync("p1");
var byCode = await client.Products.GetByCodeAsync("COFFEE-500");

// One page.
var page = await client.Products.ListAsync(new ProductPageOptions
{
    PageNumber = 1,
    PageSize = 60,
    IncludeTotalCount = true,
});

// Everything, one page fetched at a time.
await foreach (var item in client.Products.ListAllAsync())
{
    // …
}

// Search.
var found = await client.Products.SearchAsync("productType:BASIC");
var byName = await client.Products.SearchByNameAsync("coffee");

// Many at once — split into chunks automatically.
var many = await client.Products.GetManyByIdAsync(ids);

// Variants of a parent product.
await foreach (var variant in client.Products.ListVariantsAsync("parent-1"))
{
    // …
}

// Writes.
var created = await client.Products.CreateAsync(newProduct);
await client.Products.UpdateAsync("p1", changes);
await client.Products.DeleteAsync("p1", force: true);
```

### Localized text

Names and descriptions are `LocalizedString`, not `string`. Emporix returns the
same field in two shapes — every translation when the request named no language,
one text when it did — and this type reads both:

```csharp
product.Name?.ToString()   // some text, whichever the response carried
product.Name?.Get("de")    // that language, or null
product.Name?.GetOrAny("de")   // that language, or any other rather than nothing
```

Set `EmporixStorefrontContext.Language` to have Emporix translate; leave it
unset to get every language and choose per request.

## The other services

Twenty-six of the Emporix services are covered, each with the full set of
operations the API offers for it. Each hangs off the client under a name that
says what it owns:

| Property | Covers |
|---|---|
| `client.Products` | products, variants, search, bulk fetch |
| `client.Categories` | categories, the tree, assignments |
| `client.Brands` | brands |
| `client.Labels` | labels |
| `client.Catalogs` | catalogs and their categories |
| `client.Carts` | carts, items, coupons, validation |
| `client.Customers` | sign-up, sign-in, profile, addresses |
| `client.Prices` | prices, price matching, price models and price lists |
| `client.Availability` | stock per site |
| `client.Checkout` | turning a cart into an order |
| `client.Orders` | a customer's own orders |
| `client.SalesOrders` | the administrative order collection |
| `client.Media` | assets, links, downloads |
| `client.Taxes` | tax configuration and calculation |
| `client.Fees` | fees, and what they attach to |
| `client.Coupons` | coupons, redemptions, referral coupons |
| `client.Payments` | payment methods, and moving money |
| `client.Shipping` | zones, methods, quotes, delivery times |
| `client.Returns` | returns against an order |
| `client.Invoices` | invoice generation, as a job |
| `client.LegalEntities` | the companies a B2B tenant sells to |
| `client.ContactAssignments` | who may act for which company |
| `client.Locations` | where a company receives |
| `client.CustomerAdmin` | customers, as a seller manages them |
| `client.Approvals` | carts and quotes waiting for a decision |
| `client.Quotes` | negotiated prices, before they become orders |
| `client.Segments` | who gets which prices and which catalogue |

A storefront flow from browsing to order:

```csharp
var shopper = AuthContext.Anonymous();

// What does this person pay right now? Currency, site and country come from
// the token, which is why matching needs a shopper context.
var prices = await client.Prices.MatchByContextChunkedAsync(items, auth: shopper);

// Is it in stock?
var stock = await client.Availability.GetAsync("p1", "main");

// Cart. A cart belongs to a person, so a service token is refused.
var cart = await client.Carts.GetCurrentAsync(
    new CurrentCartQuery { SiteCode = "main", Create = true },
    shopper);
await client.Carts.AddItemAsync(cart!.Id, item, shopper);

// Order. Deliberately never retried — a repeated checkout is a second order.
var order = await client.Checkout.PlaceOrderAsync(checkout, shopper);

// Afterwards, from the customer's own token.
var mine = await client.Orders.ListMineAsync(AuthContext.Customer(token));
```

Some services group operations that belong together:

| | |
| --- | --- |
| `client.Prices.Models` · `client.Prices.Lists` | price models, price lists and their prices |
| `client.Products.Templates` | the attribute sets products are built from |
| `client.Categories.Assignments` | what sits in a category, also by reference |
| `client.Customers.Addresses` | a customer's own addresses and their tags |
| `client.Payments.Modes` | what a tenant accepts, and the reduced view a browser may see |
| `client.Shipping.ForSite(site)` | zones, methods, quotes, groups — everything per site |
| `client.Shipping.DeliveryTimes` | delivery times and slots, configured tenant-wide |
| `client.Coupons.Redemptions(code)` | the redemptions of one coupon |
| `client.Fees.ForItem(yrn)` · `client.Fees.ForProduct(id)` | the fees attached to one thing |
| `client.Segments.Customers(id)` · `client.Segments.Items(id)` | who is in a segment, and what it grants |
| `client.Quotes.Reasons` | why a quote was requested or refused |
| `client.CustomerAdmin.AddressesOf(number)` | one customer's addresses, seller-side |

A cart item is addressed by its YRN, not by a bare product id — `ProductYrn.Create(tenant, id)`
builds one, and passing a bare id is refused before the request leaves. It also
needs the `priceId` from a price match: Emporix rejects an item without one.

Several services refuse the wrong kind of token instead of failing quietly.
Price matching with a service token would return an empty list — indistinguishable
from «no prices configured» — so it throws `EmporixConfigurationException` before
the request is sent. Carts, checkout and own-orders do the same.

### What is not here yet

These twenty-six services carry the operations the Emporix API offers for them.
What is missing is the other 22 — platform (IAM, schemas, webhooks, sites) and
the AI and import services. Their generated types ship in the package, so
anything missing is reachable through the underlying `HttpClient`.

The full picture is in
[docs/analysis.md](docs/analysis.md#actual-coverage-2026-08-31), and what happens
to the other 36 in [docs/roadmap.md](docs/roadmap.md).

## Error handling

Every failure derives from `EmporixException`. The two main branches separate
«Emporix responded» from «the request never arrived»:

```csharp
try
{
    await client.Products.GetAsync(id);
}
catch (EmporixNotFoundException)
{
    // 404
}
catch (EmporixInsufficientScopeException ex)
{
    // 403 — ex.RequiredScope names the missing scope.
}
catch (EmporixRateLimitException ex)
{
    // 429 — ex.RetryAfter carries the server's requested wait.
}
catch (EmporixApiException ex)
{
    // Any other response with an error status.
    logger.LogError("{Status} {Code} {CorrelationId}", ex.StatusCode, ex.ErrorCode, ex.CorrelationId);
}
catch (EmporixTransportException)
{
    // Timeout or network problem — there was no response.
}
```

Every failure carries a `CorrelationId` that the SDK sent along. Quote it in
support requests.

A response body that is not JSON — an HTML error page from an upstream proxy,
say — never turns into a serialization failure: it is available verbatim in
`RawBody`.

## Retries

5xx and 429 responses are retried with a growing wait and jitter, capped at 8
seconds. A server-supplied `Retry-After` takes precedence.

**`POST` and `PATCH` are not retried** unless the call explicitly declares itself
repeatable. A server error can arrive after the server has already applied the
change — a repeated order would be a duplicate order.

## Samples

| Project | What it shows |
| --- | --- |
| `samples/Viu.Emporix.Sample` | the smallest thing that works — no container |
| `samples/Viu.Emporix.Storefront` | an ASP.NET Core backend: `AddEmporix`, an `AuthContext` per request, SDK failures mapped to status codes in one place |
| `samples/Viu.Emporix.SmokeTest` | the anonymous flow against a real tenant (see below) |

The storefront sample is the shape most consumers will build, and it publishes
Native AOT like the rest — being trimmable in a console app would prove little
about the host the SDK actually runs in.

```bash
EMPORIX_TENANT=your-tenant EMPORIX_CLIENT_ID=your-client-id EMPORIX_SITE=main EMPORIX_CURRENCY=CHF dotnet run --project samples/Viu.Emporix.Storefront
```

## Before releasing: the smoke test

Unit tests use a stubbed `HttpMessageHandler`, which cannot tell a right address
from a wrong one — that is how six calls in this SDK once pointed at endpoints
Emporix does not have, each with a passing test.
[`SpecPathTests`](tests/Viu.Emporix.Tests/SpecPathTests.cs) now catches that
against the vendored specifications, but only a real call catches a body the API
rejects, a response that deserialises to nothing, or a scope a token turns out
not to carry.

`samples/Viu.Emporix.SmokeTest` walks the anonymous storefront flow against a
real tenant — session, products, categories, prices, availability, cart, item,
validation — and exits non-zero if any step fails. It creates one cart and
deletes it again; it places no order.

It has already earned its keep: the first run against a live tenant broke on
the first call and turned up seven defects, among them product names that could
not be parsed at all, a price match that returned the request type instead of
the response, and three places where the specification disagrees with the API.

```bash
EMPORIX_TENANT=your-tenant EMPORIX_CLIENT_ID=your-client-id EMPORIX_SITE=main EMPORIX_CURRENCY=CHF EMPORIX_COUNTRY=CH EMPORIX_PRODUCT_ID=a-priced-product dotnet run --project samples/Viu.Emporix.SmokeTest
```

`EMPORIX_PRODUCT_ID` is worth setting: a tenant can list plenty of products and
have prices for none of them, and then the pricing and cart-item steps skip
themselves and prove nothing. Naming one product that is priced walks the flow
to the end. `EMPORIX_HOST` overrides the API host; everything else is optional.

Credentials come from the environment and are never read from a file in the
repository. Nothing it prints contains a token.

## Contributing

The types under `src/Viu.Emporix/Generated/` are produced from the Emporix
specifications and are **not edited by hand**:

```bash
dotnet tool restore
dotnet run --project tools/Viu.Emporix.SpecSync
```

That downloads the specifications, applies the documented repairs and
regenerates the types. Adjustments belong in the pipeline or in a separate file
alongside.

## License

MIT — see [LICENSE](LICENSE).

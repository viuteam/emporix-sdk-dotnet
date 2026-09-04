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

## Environments

**Emporix separates environments by tenant, not by host.** Every one of the 44
vendored specifications points at `https://api.emporix.io`; there is no staging
host. Dev, staging and production are three tenants with three sets of
credentials, and `Host` stays at its default unless you are pointing at a proxy or
a mock server.

Which means dev and production are one string apart. Everything below is about
making that string hard to get wrong.

### Configuration per environment

Bind the options from configuration and let the layering .NET already has do the
work:

```csharp
builder.Services.AddEmporix(builder.Configuration.GetSection("Emporix"));
```

```jsonc
// appsettings.json — what is the same everywhere
{
  "Emporix": {
    "Credentials": {
      "Storefront": {
        "ClientId": "...",
        "Context": { "Currency": "CHF", "SiteCode": "main", "TargetLocation": "CH" }
      }
    }
  }
}

// appsettings.Development.json — only what differs
{ "Emporix": { "Tenant": "acme-dev" } }
```

The section's shape is `EmporixOptions`, so anything on it can be configured this
way — timeouts, retry, the token cache, and the named credential sets under
`Credentials:Custom:<name>`.

To take everything from configuration and still override one value in code, bind
first and adjust after; the calls apply in order:

```csharp
builder.Services.AddEmporix(builder.Configuration.GetSection("Emporix"));
builder.Services.Configure<EmporixOptions>(o => o.Tenant = ResolveTenant());
```

The delegate form shown under [Quick start](#with-dependency-injection) stays the
right choice when the values come from somewhere that is not `IConfiguration` —
flat environment variables of your own naming, for instance, as
`samples/Viu.Emporix.Storefront` does.

### Secrets belong in none of those files

`Credentials.Backend.Secret` is the one value that must not sit in a file in your
repository. Locally use user secrets; deployed, use an environment variable or a
vault. The .NET configuration stack layers them for you — this wins over any JSON
file, with no code change:

```bash
Emporix__Credentials__Backend__Secret=...
```

The storefront client id is a different matter: anonymous sessions need no secret,
so that one may ship inside a browser bundle.

### Two mistakes worth designing against

**A misconfigured deployment should not start.** `AddEmporix` registers
`ValidateOnStart`, so a missing or malformed tenant fails at startup rather than on
the first API call, in production, as an empty product list. That guarantee holds
for both overloads.

**Log the tenant once, at startup.** The SDK never logs tokens, but nothing tells
you which tenant a running process is talking to. Without that line, «why are there
no products here» takes hours to separate from «wrong tenant»:

```csharp
app.Logger.LogInformation(
    "Emporix tenant {Tenant}", app.Services.GetRequiredService<IOptions<EmporixOptions>>().Value.Tenant);
```

### Several environments in one process

For a migration or a sync job that talks to two tenants at once, register a client
per environment as a keyed singleton:

```csharp
foreach (string name in new[] { "dev", "prod" })
{
    EmporixOptions options = builder.Configuration
        .GetSection($"Emporix:{name}").Get<EmporixOptions>()!;

    builder.Services.AddKeyedSingleton(name, (sp, _) =>
        new EmporixClient(options, sp.GetRequiredService<ILoggerFactory>()));
}

public sealed class Migration(
    [FromKeyedServices("dev")] EmporixClient source,
    [FromKeyedServices("prod")] EmporixClient target);
```

`AddEmporix` itself registers exactly one configuration, so this is the way for
now. **Keep each client a singleton**: one built through its public constructor
owns its own connection pool, and building one per request exhausts sockets the
same way `new HttpClient()` in a loop does.

### `Credentials.Custom` is not for environments

It looks like it might be. It is a set of named client credentials *within one
tenant*, addressed per call:

```csharp
await client.Imports.StartRunAsync(configId, auth: AuthContext.Service("import-writer"));
```

Useful for least privilege — a writing client for the import job, a read-only one
for the dashboard. It cannot reach another tenant, because the tenant lives on the
options and not on the `AuthContext`.

## Mixins

A mixin is a set of tenant-defined fields under `entity.mixins.<key>`, described
by a JSON Schema that Emporix versions for you. The SDK reads and writes them
typed, and filters on them:

```csharp
var delivery = MixinReader.Read(product.Mixins, Mixins.DeliveryOptions);

var w = MixinWriter.Create()
    .Set(Mixins.DeliveryOptions, new DeliveryOptionsMixinV6 { Packaging = "Paper" });

product.Mixins          = w.Values;
product.Metadata.Mixins = w.SchemaUrls;   // Emporix leaves a mixin unvalidated without this

string q = MixinQuery.For(Mixins.DeliveryOptions)
    .Where(d => d.Packaging, Condition.EqualTo("Paper"))
    .Where(d => d.Weight, Condition.AtLeast(2))
    .Build()
    .Build();

await client.Products.SearchAsync(q);
```

The condition decides which operators an attribute accepts, so
`Condition.AtLeast` on a text attribute is a compile error rather than a query
the backend rejects. `Or` needs `compoundLogicalQuery`, which only some services
support, so it returns a type whose `Build` requires naming the endpoint:

```csharp
a.Or(b).Build(EmporixQuery.ProductSearch);    // fine
a.Or(b).Build(EmporixQuery.CategorySearch);   // throws — Category cannot run it
a.Or(b).Build();                              // does not compile
```

The types come from your tenant, so they are generated into your repository:

```bash
dotnet tool install --global Viu.Emporix.MixinSync

emporix-mixins pull && emporix-mixins generate    # commit the output
emporix-mixins check                              # for CI; exits 1 on drift
```

`emporix-mixins.json` sits beside your solution; credentials come from
`EMPORIX_BACKEND_CLIENT_ID` and `EMPORIX_BACKEND_SECRET`, so the file carries
nothing secret:

```json
{
  "tenant": "acme",
  "namespace": "Acme.Mixins",
  "out": "src/Acme.Shop/Mixins/Generated",
  "lockFile": "src/Acme.Shop/Mixins/mixins.lock.json"
}
```

`check` is the part worth automating. Emporix assigns a new schema version on
every change, so put this in your own repository:

```yaml
on:
  schedule: [{ cron: "0 6 * * *" }]
  workflow_dispatch: {}
jobs:
  drift:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
      - run: dotnet tool install --global Viu.Emporix.MixinSync
      - run: emporix-mixins pull && emporix-mixins generate
        env:
          EMPORIX_BACKEND_CLIENT_ID: ${{ secrets.EMPORIX_BACKEND_CLIENT_ID }}
          EMPORIX_BACKEND_SECRET: ${{ secrets.EMPORIX_BACKEND_SECRET }}
      - uses: peter-evans/create-pull-request@v8
        with:
          title: "chore: sync mixin schema versions"
          branch: mixins/sync
```

A raised version then arrives as a pull request with the type diff beside it.

Five `q` forms come from the Node SDK and are **not yet verified against a live
tenant** — the range syntax, the localized path, `exists`/`missing` semantics,
whitespace escaping, and whether `metadata.mixins` must be resent on `PATCH`.
They are listed in
[the design spec](docs/superpowers/specs/2026-09-03-mixin-codegen-design.md).

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

### Bundles, variants and the other product shapes

Emporix returns five shapes from a product read — basic, bundle, parent
variant, variant and dynamic variant — and the specification declares them as a
union with no discriminator. The methods above return the basic shape for all
five, which is right for a catalogue of plain products. Where bundles or
variants matter, `AnyType` resolves them:

```csharp
var product = await client.Products.AnyType.GetAsync(id);

if (product is BundleProductWithId bundle)
{
    foreach (var item in bundle.BundledProducts)
        Console.WriteLine($"{item.ProductId} x{item.Amount}");
}
else if (product is VariantProductWithId variant)
{
    Console.WriteLine($"a variant of {variant.ParentVariantId}");
}
```

The same seven reads exist there — `GetAsync`, `GetByCodeAsync`, `ListAsync`,
`SearchAsync`, `SearchByNameAsync`, `GetManyByIdAsync`, `GetManyByCodeAsync` —
with identical parameters, so a mixed search resolves each result on its own.

Nothing is lost through the plain methods either: unknown fields land in the
`AdditionalProperties` extension data and can be read from there. `AnyType`
gives you the typed path instead of that.

**One limitation:** the specification does not require `productType` on a
variant. A variant sent without it resolves to the basic shape. Deriving the
type from other fields would be guessing, so the SDK does not.

A `productType` the vendored specification does not list makes the read throw,
here and on the plain methods alike — the generated enum property refuses an
unknown value, and it carries the converter as an attribute, which no setting
of ours overrides. Worth knowing because Emporix has extended that list before.

### Writing a bundle or a variant

The write calls take the same five shapes. `CreateAsync`, `ReplaceAsync`,
`CreateManyAsync` and `UpdateManyAsync` accept whichever one you pass:

```csharp
await client.Products.CreateAsync(new BundleProductCreation
{
    Code = "gift-box",
    BundledProducts = { new Anonymous { ProductId = "p1", Amount = 2 } },
});

await client.Products.CreateAsync(new VariantProductCreation
{
    Code = "shirt-red-m",
    ParentVariantId = "shirt",
});
```

A bulk call may mix them, which is what the specification's array of `oneOf`
permits:

```csharp
await client.Products.UpdateManyAsync(
[
    new BasicProductBulkUpdate { Id = "p1", Published = true },
    new BundleProductBulkUpdate
    {
        Id = "g1",
        BundledProducts = { new Anonymous { ProductId = "p2", Amount = 1 } },
    },
]);
```

**`PATCH` is the exception, and it is not per type.** The specification declares
one flat schema there, so `UpdateAsync` takes `ProductPartialUpdate` — which
carries the union of the type-specific fields, `BundledProducts`,
`VariantAttributes` and `Template` included:

```csharp
await client.Products.UpdateAsync("g1", new ProductPartialUpdate
{
    BundledProducts = [new Anonymous { ProductId = "p2", Amount = 1 }],
});
```

Note the assignment rather than a collection initializer: `BundledProducts` is
nullable on this type and carries a default instance on the bundle types, so
`= { … }` compiles here and throws at runtime.

If you are coming from an earlier version, `UpdateAsync` is the one write call
whose signature broke. It used to take `BasicProductUpdate`, which had no
property for any of those fields.

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

Every Emporix service is covered, and all but one with the full set of operations
the API offers for it — `Availability` carries the stock records but not the
locations a site ships from, five operations that no caller has needed yet. The
gap is pinned by a test rather than left to be discovered, so it is a gap someone
chose. Each service hangs off the client under a name that says what it owns:

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
| `client.Iam` | users, groups, and what they may do |
| `client.Schemas` | schemas and custom entities |
| `client.Sites` | the storefronts a tenant runs |
| `client.Vendors` | who sells, in a marketplace |
| `client.Currencies` | currencies and exchange rates |
| `client.Countries` | countries and regions |
| `client.Webhooks` | Emporix calling out when something happens |
| `client.Units` | units of measure, and converting between them |
| `client.SequentialIds` | order numbers and the like |
| `client.Configuration` | tenant and client configuration |
| `client.SessionContext` | what a session carries beyond its token |
| `client.Imports` | bringing data in from somewhere else |
| `client.Indexing` | which provider indexes the catalogue, and rebuilding it |
| `client.PickPack` | the warehouse side of an order |
| `client.ShoppingLists` | what a customer means to buy later |
| `client.RewardPoints` | loyalty points, and what they buy |
| `client.Ai` | text generation, and agents that do things |
| `client.RagIndexer` | what an agent can retrieve over |
| `client.CloudFunctions` | code a tenant deployed, invoked by name |
| `client.AuditLogs` | who changed what, when, and from which value to which |

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
| `client.Iam.Users` · `client.Iam.Groups` · `client.Iam.AccessControls` | identity, membership and grants |
| `client.Schemas.CustomEntities` · `client.Schemas.InstancesOf(type)` | a tenant's own shapes, and their records |
| `client.Sites.MixinsOf(code)` | a tenant's own settings on one site |
| `client.Configuration.ForClient(id)` | configuration narrowed to one client |
| `client.Ai.Agents` · `client.Ai.Templates` | agents, and the templates they are built from |
| `client.Ai.Tools` · `client.Ai.McpServers` | what an agent may reach out to |
| `client.Ai.Tokens` · `client.Ai.OAuths` | the credentials those need |
| `client.Ai.Conversations` · `client.Ai.Logs` · `client.Ai.Jobs` | what was said, what was done, what is still running |

A cart item is addressed by its YRN, not by a bare product id — `ProductYrn.Create(tenant, id)`
builds one, and passing a bare id is refused before the request leaves. It also
needs the `priceId` from a price match: Emporix rejects an item without one.

Several services refuse the wrong kind of token instead of failing quietly.
Price matching with a service token would return an empty list — indistinguishable
from «no prices configured» — so it throws `EmporixConfigurationException` before
the request is sent. Carts, checkout and own-orders do the same.

### Where the SDK hands you JSON instead of a type

Three places, all for the same reason: the specification declares a `oneOf` with
no discriminator the generator can act on, so choosing one of the alternatives
would silently drop the others' fields.

- `client.Ai.Tools` — a tool is a Slack, Teams or one of two retrieval kinds.
  Writing is typed, one method per kind; reading hands back a `JsonElement` whose
  `type` says which it is.
- `client.Ai.McpServers` — the same split between a server Emporix hosts and one
  you run yourself.
- `client.CloudFunctions` — no specification exists at all, by design. The caller
  supplies the type information; see [ADR-0009](docs/adr/0009-cloud-functions.md).

Everything else returns a generated type. The full picture is in
[docs/analysis.md](docs/analysis.md#actual-coverage-2026-08-31); how the coverage
was built up, wave by wave, is in [docs/roadmap.md](docs/roadmap.md).

### Waiting for work that takes a while

Imports, reindexing, invoice generation and asynchronous chats all answer with a
job rather than a result. `EmporixPolling.WaitForAsync` waits for one, with a
growing interval and a timeout that is distinct from cancellation:

```csharp
var job = await EmporixPolling.WaitForAsync(
    poll: ct => client.Imports.GetRunAsync(runId, cancellationToken: ct),
    isComplete: r => r?.Status is not (ImportRunStatus.RUNNING or ImportRunStatus.PENDING),
    cancellationToken: cancellationToken);
```

There is no job type, because the four job shapes in the API share no field a
type system can use — [ADR-0008](docs/adr/0008-long-running-jobs.md) has the
detail. Two endpoints stream their progress instead of being polled;
`client.Imports.StreamEventsAsync` and `client.Ai.ChatStreamAsync` return the
response unread, and `System.Net.ServerSentEvents` — already in the `net10.0`
shared framework — parses it in three lines
([ADR-0007](docs/adr/0007-streaming.md)).

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

### The seller's side

An anonymous storefront token cannot reach anything a seller configures, so the
smoke test has a second pass that runs on client credentials. It is read-only —
every call is a `GET` or a search — and it covers the services no browser ever
sees: taxes, sites, shipping zones, IAM, custom entities, imports, indexing,
reward options, AI agents and tools, shopping lists and the audit log.

```bash
EMPORIX_BACKEND_CLIENT_ID=your-client-id EMPORIX_BACKEND_SECRET=your-secret dotnet run --project samples/Viu.Emporix.SmokeTest
```

Without those two the pass says so and skips itself, which is why the same
command works with or without them. Put the secret in the environment or a
repository secret — never in a file in the repository.

## Releasing

Releases are cut by Release Please: every push to `main` updates a release pull
request, and merging it publishes to nuget.org. What that asks of a contributor is
one thing — a commit message a machine can classify:

```
feat: add the audit log service
fix(cart): send the item YRN rather than the bare id
```

`feat` moves the minor version, `fix` the patch, and `chore`/`test`/`ci` move
nothing. The full picture, including what is deliberately still manual, is in
[docs/releasing.md](docs/releasing.md).

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) covers the commit convention, the pull-request
flow and the handful of mistakes that cost time in this repository. The one worth
repeating here: the types under `src/Viu.Emporix/Generated/` are produced from
the Emporix specifications and are **not edited by hand**:

```bash
dotnet tool restore
dotnet run --project tools/Viu.Emporix.SpecSync
```

That downloads the specifications, applies the documented repairs and
regenerates the types. Adjustments belong in the pipeline or in a separate file
alongside.

## License

MIT — see [LICENSE](LICENSE).

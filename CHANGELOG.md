# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions are derived from Git tags by MinVer: tagging `v0.1.0-preview.1` produces
exactly that package version.

## [Unreleased]

### Added

- Seven services that complete a checkout: `Taxes`, `Fees`, `Coupons`,
  `Payments`, `Shipping`, `Returns` and `Invoices` — 101 operations. Nineteen of
  the 48 Emporix services are now covered.
- Grouped operations for the new services: `Payments.Modes`,
  `Shipping.ForSite(site)`, `Shipping.DeliveryTimes`, `Coupons.Redemptions(code)`,
  `Fees.ForItem(yrn)` and `Fees.ForProduct(id)`.
- Seven B2B services: `LegalEntities`, `ContactAssignments`, `Locations`,
  `CustomerAdmin`, `Approvals`, `Quotes` and `Segments` — 84 operations, and
  what `Orders.ListForLegalEntityAsync` and `Checkout.PlaceOrderFromQuoteAsync`
  had been pointing at with nothing behind them. Twenty-six of the 48 Emporix
  services are now covered.
- Grouped operations for those: `Segments.Customers(id)`, `Segments.Items(id)`,
  `Quotes.Reasons` and `CustomerAdmin.AddressesOf(number)`.

### Fixed

- The generation pipeline keeps the meaningful name when dissolving an alias
  over an untitled schema. NSwag calls a schema it cannot title `Anonymous2`,
  and the resolver was dissolving the named alias into it — which put
  `Anonymous2` in a public signature.
- The generation pipeline renames generated types that differ only in letter
  case. The shipping specification defines both `MetaData` and `Metadata`, which
  collide in a case-insensitive file name and made the JSON source generator
  abort — silently taking every other serialization context in the assembly with
  it.

### Notes

- No payment call is ever retried. Emporix offers no idempotency key on
  authorize, capture, refund or cancel, so a retry cannot be made safe.
- `Fees.SearchByProductsAsync` takes the product ids as a single string, not a
  list. The specification types the field that way despite the plural name, and
  neither it nor the published documentation says how several ids are encoded
  inside it. Passing the value through verbatim beats guessing a separator; use
  `SearchByProductAsync`, which is fully specified.

## [0.1.0-preview.1]

The first prerelease. The core is complete, and so are twelve of the 48 Emporix
services — each with the full set of operations the API offers for it, 193 in
all. The remaining 36 services have no facade yet; their generated types ship in
the package. See
[docs/analysis.md](docs/analysis.md#actual-coverage-2026-08-31).

### Added

- `EmporixClient` as the entry point, usable with dependency injection through
  `services.AddEmporix(...)` or standalone through its public constructor.
- `AuthContext` per call — anonymous, service, customer and raw tokens. One client
  instance safely serves many concurrent users.
- `DefaultTokenProvider` with a per-credential-set token cache, single-flight
  acquisition, and anonymous session renewal that preserves the `sessionId`.
- Retry for 5xx and 429 with exponential backoff and jitter, honouring
  `Retry-After`. `POST` and `PATCH` are only retried when a call declares itself
  repeatable.
- An exception hierarchy under `EmporixException`, split into
  `EmporixApiException` (Emporix responded) and `EmporixTransportException` (the
  request never arrived). Both Emporix error formats are parsed, including the
  gateway format used for 401.
- A correlation id on every request and on every failure, including failures from
  the token endpoints.
- Pagination with three-tier next-page detection and `IAsyncEnumerable` walking
  through `ListAllAsync`.
- Twelve services on `EmporixClient`:
  - `Products` — reads, search including name search, bulk fetch by id and code
    with automatic chunking, variant listing, and the full write surface.
  - `Categories` — categories, the category tree, and assignments.
  - `Brands`, `Labels`, `Catalogs` — the catalog metadata.
  - `Carts` — carts, items, coupons and validation.
  - `Customers` — sign-up, sign-in, session refresh, profile and addresses.
  - `Prices` — prices, context-aware and explicit matching, price models and
    price lists, with bulk operations throughout.
  - `Availability` — stock per site, with an opt-in reading of «no record» as
    available.
  - `Checkout` — placing an order, deliberately never marked repeatable.
  - `Orders` and `SalesOrders` — the shopper's own orders and the
    administrative collection, kept apart because Emporix does.
  - `Media` — assets, uploads, links, downloads and product attachment.
- Native AOT compatibility throughout; serialization is source-generated.
- A generation pipeline (`tools/Viu.Emporix.SpecSync`) that downloads all 43
  Emporix specifications, repairs known defects, records a sha256 manifest and
  regenerates the types.

[Unreleased]: https://github.com/viuteam/emporix-sdk-dotnet/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/viuteam/emporix-sdk-dotnet/releases/tag/v0.1.0-preview.1

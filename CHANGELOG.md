# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions are derived from Git tags by MinVer: tagging `v0.1.0-preview.1` produces
exactly that package version.

## [Unreleased]

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

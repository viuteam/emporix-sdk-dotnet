# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions come from Git tags, derived by MinVer. From 0.2.0 onwards the tag is cut
by Release Please when its release pull request is merged, and the sections below
it are generated from the commit history — see
[docs/releasing.md](docs/releasing.md).

## [0.2.0](https://github.com/viuteam/emporix-sdk-dotnet/compare/v0.1.0...v0.2.0) (2026-09-02)


### Added

* add the import schedule delete, and check coverage both ways ([b18c919](https://github.com/viuteam/emporix-sdk-dotnet/commit/b18c919d0f829a4d64e593129f93d53185f3f9cd))
* add the import schedule delete, and check specification coverage both ways ([ad4c716](https://github.com/viuteam/emporix-sdk-dotnet/commit/ad4c7168fbd132d6d18c20c8bd51b3e5a3a19cb2))


### Fixed

* sync generated types with upstream Emporix specifications ([809e599](https://github.com/viuteam/emporix-sdk-dotnet/commit/809e599673d46414df79228444b0eaff5823af9d))
* sync generated types with upstream Emporix specifications ([3e5494e](https://github.com/viuteam/emporix-sdk-dotnet/commit/3e5494e67d841cd61054d56aec734aeb701e752a))


### Documentation

* add CLAUDE.md ([1e099bb](https://github.com/viuteam/emporix-sdk-dotnet/commit/1e099bb292d3eabdd2756cb274f2f88414c6be4f))

## 0.1.0

### Added

- `Imports.DeleteScheduleAsync`, for the `DELETE` on a configuration's schedule
  that an upstream sync added.

- `AddEmporix(IConfiguration)` alongside the delegate overload, so the options can
  be bound from `appsettings.json` and its per-environment layers without writing
  the `Bind` line by hand. Bound by the configuration binding source generator
  rather than by reflection, which is what keeps the package's AOT promise
  ([ADR-0004](docs/adr/0004-aot-trimming.md)) intact.

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
- Twelve platform services: `Iam`, `Schemas`, `Sites`, `Vendors`, `Currencies`,
  `Countries`, `Webhooks`, `Units`, `SequentialIds`, `Configuration` and
  `SessionContext` — 159 operations. Thirty-seven of the 48 Emporix services are
  now covered.
- Grouped operations for those: `Iam.Users`, `Iam.Groups`, `Iam.AccessControls`,
  `Schemas.CustomEntities`, `Schemas.InstancesOf(type)`, `Sites.MixinsOf(code)`
  and `Configuration.ForClient(id)`.
- The last nine services: `Imports`, `Indexing`, `PickPack`, `ShoppingLists`,
  `RewardPoints`, `Ai`, `RagIndexer`, `CloudFunctions` and `AuditLogs` — 124
  operations. **Every Emporix service is now covered**: 47 properties on the
  client over 665 public calls, and every one of the Node SDK's 49 service
  facades has an equivalent here.
- The `audit-logs-changelog` specification, which had never been vendored. Its
  absence is why `AuditLogs` was missing from every earlier wave.
- Grouped operations for the AI service: `Ai.Agents`, `Ai.Templates`, `Ai.Tools`,
  `Ai.Tokens`, `Ai.OAuths`, `Ai.McpServers`, `Ai.Conversations`, `Ai.Jobs` and
  `Ai.Logs`.
- `EmporixPolling.WaitForAsync` for the endpoints that answer with a job rather
  than a result — imports, reindexing, invoice generation, asynchronous chats.
  Growing interval, a ceiling on it, and a timeout distinct from cancellation
  ([ADR-0008](docs/adr/0008-long-running-jobs.md)).
- Streaming for the two endpoints that have it — `Imports.StreamEventsAsync` and
  `Ai.ChatStreamAsync`. Both hand back the response unread; `net10.0` already
  ships the parser ([ADR-0007](docs/adr/0007-streaming.md)).
- `AiPatchOperation`, the SDK's own type for the AI service's `PATCH` bodies. The
  specification leaves the operation object untitled, so the generated name is
  `Anonymous` — no name for a public signature.

### Fixed

- `Availability.CreateAsync` posted to `/availability/{tenant}/availability`, a
  path the API does not have, so it can never have worked. It now addresses the
  product and the site, as the specification requires, which changes its
  signature. Found by teaching the specification check to read the two thirds of
  the call sites it had been unable to see.

- Localized fields declared inline are now typed as `LocalizedString`. The
  pipeline only recognised the union when a specification named it and
  referenced it; the tax service spells it out on the property, so `taxClass.name`
  shipped typed as a map and **reading a tax configuration threw** against a
  tenant that stores a plain string.
- Localized fields nested inside another object or an array are found too. The
  path is now resolved through the generated code, which is the only place that
  knows NSwag called the nested class `Zone2` — `QuoteResponseItem.zone.name` was
  typed `string` and would have broken any shipping quote whose zone name is not
  translated.
- A localized property the pipeline cannot find in the generated code is now
  reported instead of silently skipped. That silence is what let the two defects
  above ship.
- Enums are annotated for string serialization on the type rather than on each
  property. NSwag leaves a `TODO` where a property is a *collection* of enums, so
  `["customer"]` could not be read into `ICollection<RequiredScopes>` and **the AI
  agent list failed entirely**. Six properties across two services were affected.
- Properties whose schema is a union of several object types are typed as
  `JsonElement`. NSwag resolves such a union to its first branch: an agent using
  `provider: openai` could not be read, because the generated type only admits
  `emporix_openai` — a value the specification's own examples contradict. Four
  properties, all provider configurations.
- Shopping-list timestamps parse. The specification declares them as
  `{epochSecond, nano}`; the API sends ISO-8601, so **no shopping list could be
  read at all**.
- `Imports` pages from zero with `page` and `size`. Every other service in the
  SDK counts from one with `pageNumber` and `pageSize`; using the usual spelling
  here skips the first page without an error.
- The AI service's `PUT` responses no longer come back empty. The specification
  declares `IdResponse` as `{ "id": … }`, but the generator emits that type
  without its property, so five calls would have reported `null` for a resource
  they had just created. The SDK declares the shape itself.
- Reads of AI tools and MCP servers hand back `JsonElement` rather than a wrong
  type. Both are four-way and two-way unions with no discriminator the generator
  can act on, and it had collapsed each to its first alternative — a Teams tool
  read as a Slack tool would have lost its configuration silently. Writes stay
  typed, one method per kind.
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

- No sequential-identifier call is ever retried. Taking the next number consumes
  it, and a retry leaves a gap in a sequence that is usually expected to have
  none.
- No payment call is ever retried. Emporix offers no idempotency key on
  authorize, capture, refund or cancel, so a retry cannot be made safe.
- `Fees.SearchByProductsAsync` takes the product ids as a single string, not a
  list. The specification types the field that way despite the plural name, and
  neither it nor the published documentation says how several ids are encoded
  inside it. Passing the value through verbatim beats guessing a separator; use
  `SearchByProductAsync`, which is fully specified.

## 0.1.0-preview.1 — never published

Written as the first prerelease and superseded before it went out: the tag was
cut locally and never pushed, so no such version exists on nuget.org. Kept
because it is the honest record of what the package looked like after wave 1, and
everything in it ships as part of 0.1.0.

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

[0.1.0]: https://github.com/viuteam/emporix-sdk-dotnet/releases/tag/v0.1.0

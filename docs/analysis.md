# Phase 0 — Analysis of the Node SDK

Reference: `/Users/dominic.fritschi/projects/viu/emporix-sdk`, package
`@viu/emporix-sdk` v3.1.1 (analysed 2026-08-31). The basis is the code, not the
brief. Where the two diverge, the code wins and the divergence is marked below.

---

## 1. Structure

### Monorepo

A pnpm workspace with five published packages plus examples and end-to-end tests:

| Package | Version | Role | Relevance to .NET |
| --- | --- | --- | --- |
| `@viu/emporix-sdk` | 3.1.1 | **The core.** Framework-agnostic, zero runtime dependencies, `fetch` only. | **Full** — this is what we port |
| `@viu/emporix-sdk-react` | 3.1.0 | React hooks over TanStack Query | None — no .NET equivalent |
| `@viu/emporix-sdk-next` | 1.0.0 | Next.js server bindings, cache tags, session without a browser token | None |
| `@viu/emporix-sdk-angular` | 0.1.0 | Angular signals over TanStack Query | None |
| `@viu/emporix-mixins` | 1.0.0 | Typed mixin resolution plus query builder, its own CLI | **Partial** — see §6 |

Only `packages/sdk` needs porting. The three framework bindings are UI state
management; .NET has no counterpart and no need for one.

### Layout of `packages/sdk`

```
src/
  core/          17 files  — HTTP, auth, errors, config, logger, pagination, storage
  services/      48 facades plus 30 type files — hand-written
  generated/     86 files, 2.5 MB — @hey-api/openapi-ts, types only
  *.ts           subpath barrels (mostly one-line re-exports)
specs/           43 vendored OpenAPI YAML files plus .sync-manifest.json
scripts/         fetch-specs, generate, spec-patches, sync-manifest, check-treeshake
tests/           158 test files
```

Of 3.4 MB in `src/`, 2.5 MB is generated. The hand-written share — core plus
facades — is around 900 KB. **That is the actual work of the port.**

### Public API surface

Three entry points:

1. **`new EmporixClient(config)`** — batteries included, instantiates all 48
   services.
2. **`createEmporixClient(config, { products: ProductService, ... })`** —
   tree-shakeable, builds only the services passed in. Two-pass resolution for
   service dependencies (today only `SegmentService` → `products`, `categories`).
3. **`createCore(config)`** — the infrastructure without services, for custom
   compositions.

Plus subpath exports (`@viu/emporix-sdk/product` and so on) for 15 high-traffic
services; everything else through the package root.

> **For .NET:** Points 2 and 3 exist purely because of browser bundle size. In
> .NET that problem does not exist — the linker/trimmer solves it at a different
> level. **Recommendation: do not port.** One `EmporixClient` with lazily
> instantiated service properties suffices.

---

## 2. Type generation

### Where the specifications come from

`scripts/fetch-specs.ts` downloads 43 OpenAPI YAML files directly from the public
GitHub repository:

```
https://raw.githubusercontent.com/emporix/api-references/refs/heads/main/<category>/<service>/api-reference/api.yml
```

Note: three specifications (`invoice`, `quote`, `session-context`) use `api.yaml`
upstream rather than `.yml`. The code comment says «five», the map shows three —
the code wins.

### Versioning and provenance

`specs/.sync-manifest.json` records per service the `url`, `specVersion`
(`info.version`, almost always empty upstream), `fetchedAt` and the **`sha256`**
of the downloaded YAML. The digest is the change signal; `diffManifest()` reports
which services drifted.

The specifications **are committed**. That is the right call and worth keeping for
.NET: a build must not depend on the availability of somebody else's GitHub
repository.

### Specification patches

`scripts/spec-patches.ts` repairs known upstream defects **before** the file is
written and hashed. Each patch is idempotent and reports itself as `stale` when it
no longer changes anything (meaning: fixed upstream, remove the patch). Exactly
one is active today:

- **`approval-service`**: the JSON Patch `op` enum ships as `ADD`/`REMOVE`/
  `REPLACE` upstream; the live API accepts only lowercase and answers 400
  otherwise. Verified against tenant `viu` on 2026-08-18.

Notably disciplined: `ai-service` and `quote` carry the same enum but are
deliberately **not** patched, because no 400 was measured there. That attitude —
repair only what is demonstrably broken — is worth keeping.

### Generator

`@hey-api/openapi-ts` with the `@hey-api/typescript` plugin, so **types only, no
runtime client**. Output goes to `src/generated/<service>/`, and every file gets a
`// AUTO-GENERATED — do not edit` banner prepended. Generated code is committed
and excluded from coverage.

**This is the central architectural decision of the Node SDK:** generated DTOs, a
hand-written client. The reason is visible in the facades — they do things no
generator supplies: an auth context per call, pagination assembled from response
headers, YRN construction, query-DSL validation, chunking, error enrichment.

---

## 3. Client layer

`src/core/http.ts` (~490 lines) is the heart. One `HttpClient` per service
instance, created by `createCore().mk(serviceName)` — each gets its own child
logger.

### Base URL and tenant

- Host defaults to `https://api.emporix.io`, overridable.
- Tenant guard: `^[a-z][a-z0-9]{2,15}$`. The comment honestly states that Emporix
  documents only «lowercase» and that the 3-to-16-character rule is an SDK-side
  assumption.
- **`Emporix-Tenant` header on every request.** Looks redundant because the tenant
  is in almost every path — it is not: Emporix validates dashboard and IAM user
  tokens against this header and answers 401 without it.

### Header order

Deliberately chosen and worth keeping:

```
Emporix-Tenant → Accept-Language → caller headers → Authorization → Content-Type
```

The caller can override tenant and language per request, `Authorization` not.
`Content-Type: application/json` is set only when a body is present and it is
**not** FormData — for multipart, `fetch` sets the boundary itself.

### Serialization and query

`JSON.stringify` with no configuration. Query parameters:
`Record<string, string|number|boolean>`, `undefined` values omitted, everything
else through `String(v)`.

### Pagination

Three tiers, most precise first (`core/paged.ts`):

1. `X-Next-Cursor` response header present → `hasNextPage = true`.
   **One-directional**: only two endpoints in the entire API emit cursors, so its
   absence says nothing.
2. `X-Total-Count` present → `pageNumber * pageSize < totalCount`.
3. Otherwise: `items.length === pageSize`.

`totalCount` is **opt-in per call** (request header `X-Total-Count: true`), because
it costs the server a second query. Malformed headers are defensively mapped to
`undefined` — a `NaN` in the arithmetic would report «last page» on every page.

Auto-paging through `iterateAll()` → `AsyncIterable<T>`, exposed on facades as
`listAll()`.

> **Divergence, docs vs. code:** `docs/pagination.md` describes only the third
> tier. The code has all three.

> **For .NET:** Emporix' documented `pageSize` default is **60**; the Node SDK's
> facades use **50**. Deliberate or not, I would take 60 and document the
> difference, or keep 50 and record it as a compatibility decision.

### Retries

- Retryable: 5xx and 429.
- **Idempotency gate:** GET/PUT/DELETE retry by default; POST/PATCH only with an
  explicit `idempotent: true`. The reason is in the code: a 5xx can arrive *after*
  the server wrote — replaying `placeOrder` would duplicate the order.
- Backoff: a numeric `Retry-After` wins, capped at 8 s (against values like
  86400). Otherwise `min(1000 * 2^(n-1), 8000)` plus up to 100 ms of jitter.
- `maxAttempts` defaults to 3.

### Timeouts

Two budgets, and the implementation is subtler than it looks:

- `connectMs` (default 10 s) bounds time-to-headers.
- `readMs` (default 60 s) bounds headers **plus** body.

The overall budget is a **rejecting promise raced against fetch and the body
read**, not merely an abort. The comment gives the reason: an abort interrupts the
connection but does not reliably unblock an already-streaming body that stalls
mid-read.

> **For .NET:** `HttpClient.Timeout` plus a `CancellationToken` cover this when
> `HttpCompletionOption.ResponseHeadersRead` is used and the body is read with the
> same token. The Node contrivance works around a `fetch` shortcoming and should
> **not** be ported.

### Other modes

- `requestRaw()` — the raw `Response` for binaries and redirects
  (`redirect: 'manual'`). **No** retry and no 401 re-auth, deliberately.
- `requestStream()` — server-sent events for the AI services. No read budget
  (streams are long-lived); the consumer breaking out aborts the fetch.

### Logging

A custom, dependency-free logger (`core/logger.ts`):

- Levels `trace|debug|info|warn|error|silent`, default `warn`.
- **Controllable per service** (48 `ServiceName` values) and through the
  environment variables `EMPORIX_LOG_LEVEL` / `EMPORIX_LOG_LEVEL_<SERVICE>`. The
  environment wins over programmatic configuration unless `force` is passed.
- **Redaction cannot be switched off.** A fixed deny list (`authorization`,
  `password`, `access_token`, `refresh_token`, `customertoken`, `saastoken`,
  `clientsecret`, …), plus a special case: an object with `kind` and `token` — an
  `AuthContext` — is reduced to `{ kind }`. Extra keys are additive only.

### Request correlation

Here is a **genuine weakness**: the request id is a process-wide counter
(`req-${++requestSeq}`), appears only in logs, and is **not** attached to
exceptions. There is no correlation id on the wire at all.

> **Do better in .NET explicitly:** a real correlation id (GUID or
> `Activity.Current?.Id`), sent as a header and carried on every exception. The
> brief asks for it anyway — this is an improvement, not a port.

---

## 4. Authentication

Four token kinds, passed as an `AuthContext` **per call** and **never stored on
the client**. This is the single most important design decision in the SDK: one
instance safely serves many concurrent shoppers (SSR, edge, multi-tenant).

| Kind | Owner | How it is obtained |
| --- | --- | --- |
| `service` | SDK | `POST /oauth/token`, `client_credentials`, from `credentials.backend` or a named `custom` set. No refresh token. |
| `anonymous` | SDK | `GET /customerlogin/auth/anonymous/{login\|refresh}?tenant&client_id`. Client id only, no secret. Carries a `sessionId`. |
| `customer` | Caller | From `POST /customer/{tenant}/login`. The wire field `accessToken` is mapped to `customerToken`. |
| `raw` | Caller | Passed through verbatim — the escape hatch for SSO and token exchange. |

### Scope handling

Scopes are **not a concept in the SDK**. An optional `scope` string per
`ServiceCredentials` is appended to the token request — that is all. There is no
scope registry and no up-front check. Missing scopes surface only as a 403.

### Token cache and concurrency

`DefaultTokenProvider`:

- **Service tokens:** `Map<credentialSet, {token, expiresAt, obtainedAt}>` plus a
  `Map<credentialSet, Promise>` as a single-flight lock. Concurrent callers share
  one in-flight request. Two expiry criteria: `expiresAt` (from `expires_in` minus
  `expirationBufferSeconds`, default 60 s) **and** an absolute
  `maxLifetimeSeconds` ceiling (default 3600 s).
- **Anonymous session:** a single slot plus a lock. On expiry with a refresh token
  present it **renews rather than logs in again**, so the `sessionId` — and with it
  the guest cart — survives. Only if the renewal fails does a fresh login follow.
- Token requests have a timeout of their own (`boundedFetch`) because they sit in
  **front** of the single-flight lock: a hung call would otherwise block every
  request for that credential set indefinitely.

### 401 behaviour — the central asymmetry

- **SDK-owned** (`service`, `anonymous`): invalidate the token, re-authenticate
  **once**, repeat the request. For anonymous, `expireAnonymousAccessToken()` is
  preferred (access token expires, refresh token stays) so the retry keeps the
  `sessionId`.
- **Caller-owned** (`customer`, `raw`): the 401 propagates as an
  `EmporixAuthError`. **Unless** a `CustomerTokenRefresher` is registered — then a
  single refresh-and-retry.

The `CustomerRefreshRegistry` is single-flight, and the reason is in the code:
**Emporix rotates the refresh token on every renewal**, so parallel renewals
would invalidate each other. This must be carried over to .NET with a
`SemaphoreSlim`.

### Session persistence

`AnonymousSessionStore` with `read()`/`write()`. Two hosts, opposite choices — and
both justified:

| Host | Persists the access token? | Why |
| --- | --- | --- |
| React (browser) | **no** | The client is long-lived and holds the token in memory. Persisting it would only put a bearer token where JavaScript can read it. |
| Next (server) | **yes**, httpOnly | The guest client lives for a single request; without it that is one billed API call per page view. |

> **For .NET:** the server case is the relevant one. The pattern (an optional
> store with `accessToken` plus `expiresAt`) is worth carrying over; the browser
> scenario falls away.

### Proactive refresh

Only through the `expirationBufferSeconds` margin, no timer. That is sufficient
and simpler than a background refresh — keep it for .NET.

---

## 5. Error handling

### Hierarchy

```
EmporixError (message, status?, body?)
├── EmporixAuthError              401
├── EmporixForbiddenError         403
│   └── EmporixInsufficientScopeError   403 plus "missing scope: <name>" in body.details
├── EmporixNotFoundError          404
├── EmporixValidationError        400, 422
├── EmporixServerError            5xx
├── EmporixTimeoutError           no status
└── EmporixNetworkError           no status
```

Everything else falls back to `EmporixError`. **429 has no type of its own** — it
lands on the base type, and `Retry-After` is read only internally for the backoff,
never handed to the caller.

### Fields carried through

Only `status` and `body` (the parsed response body). **No** error code, **no**
request id, **no** structured field errors. `toJSON()` scrubs token-like body
keys.

### Defensive parsing

`safeJson()` returns the raw text for an unparseable body instead of throwing. An
HTML error page from a proxy therefore yields a correct `EmporixServerError` with
the HTML as `body` — not a `JsonException`. Exactly what the brief asks for in
.NET as well.

### Gaps against the .NET brief

From the Emporix specification (verified against the price service schema) two
error formats are in circulation, and the Node SDK reads neither:

```jsonc
// Standard (400/403/500)
{ "code": 400, "status": "...", "message": "...", "details": ["..."], "errorCode": "..." }

// 401 (gateway format, different!)
{ "fault": { "faultstring": "...", "detail": { "errorcode": "..." } } }
```

Only `details` is touched, and only by a regular expression, to extract the
missing scope.

> **Do better in .NET** (in line with the brief):
> - Parse both formats defensively and carry `errorCode`/`message`/`details`
>   through as typed values.
> - `EmporixRateLimitException` with a parsed `RetryAfter`.
> - `EmporixValidationException` with the details from `details`.
> - A correlation id and the raw response body on every exception.

---

## 6. Services covered

**48 service facades, 649 public operations** (counted by return types
`Promise<T>` / `AsyncIterable<T>`), built on **43 vendored specifications**.

Comparing against the official Emporix «List of API Services» (41 entries):
**the Node SDK covers essentially the entire documented API surface.** The one
listed service not covered is SEPA Export, deprecated upstream and scheduled for
removal on 2026-08-24.

### Operations per service

| Size | Services |
| --- | --- |
| **30+** | shipping (45), segment (35), schema (32), price (32), iam (30) |
| **18–29** | cart (29), category (28), product (24), customer (24), ai-resources (21), imports (20), orders (19), customer-admin (19), ai (18) |
| **10–17** | fee (17), payment (15), site (14), reward-points (14), coupon (13), quote (12), pick-pack (12), media (12), vendor (11), indexing (11), webhook (10), currency (10) |
| **5–9** | unit-handling, shopping-list, availability (9), sequential-id (8), companies, catalog, approval (7), tenant-config, tax, returns, label, client-config, brand (6), locations, country, contacts (5) |
| **1–4** | session-context (4), customer-groups, ai-rag-indexer (3), invoice, checkout (2), cloud-functions (1) |

### Two patterns in the facades

**Flat** (the rule): `client.brands.listBrands()`, `client.brands.getBrand(id)`.

**Nested** (9 services): `client.iam.users.list()`, `client.price.lists.create()`.
Affected: `category.assignments`, `customer.addresses`,
`iam.{users,groups,accessControls,scopes}`, `payment.{modes,transactions}`,
`price.{models,lists}`, `product.templates`, `segment.{customers,items}`,
`schema.references`, `site.mixins`.

> **For .NET:** the nested pattern maps to a nested class exposed through a
> `readonly` property (`client.Iam.Users.ListAsync(...)`). Directly transferable
> and idiomatic — the Node SDK's anonymous object literals are a JavaScript
> convenience.

### Deliberately not covered

- **`oauth-service`** — the specification is vendored but never imported. Token
  handling is entirely hand-written in `core/auth.ts`. Correct as is.
- **`shopping-list`** — specification vendored, but the facade uses hand-written
  types.
- **Legacy RBAC in IAM** (roles/permissions/resources/templates) — marked
  deprecated in the code and deliberately not wrapped.
- **SEPA Export** — deprecated upstream.

### Hand-written DTOs instead of generated types

Five facades: `ai-resources`, `client-config`, `cloud-functions`,
`shopping-list`, `tenant-config`. For the port that means there is no
specification source here; the types have to be written by hand.

### `@viu/emporix-mixins`

A standalone toolkit: resolves Emporix mixins (customer-specific JSON-Schema
fields) into typed values and builds entity-gated query filters from them
(`mixinQuery`). The SDK core couples to it **structurally rather than by import**
(`core/query.ts`, interface `BuiltQuery`) to avoid a circular reference.

The query capability gate matters: a filter built with `or()` throws when the
target service does not support `compoundLogicalQuery` — rather than sending a
query the backend cannot execute.

> **Scope proposal: no mixin code generation in v1.** The `q` string and the
> `BuiltQuery` abstraction belong in the core; the schema-to-type generator is a
> project of its own. In .NET it would be a source generator — feasible, but not
> for v1.

---

## 7. Tests, build, release

### Tests

- **158 test files**, vitest, Node environment.
- HTTP stubbing throughout with **msw** (`setupServer`) against real URLs. No
  handler mocking, no interface doubles — the tests run through the real
  `HttpClient`.
- Coverage thresholds: **80 % lines, 80 % branches**. Excluded:
  `src/generated/**`, `src/index.ts` and the pure re-export barrels.
- Test kinds, legible from the names: `*-wiring.test.ts` (service correctly wired
  onto the client), `*-types.test.ts` (type round-trips),
  `facade-coverage.test.ts` (facade completeness), plus targeted core tests for
  retry, auth refresh, SSE, cancellation, headers, spec patches and the sync
  manifest.
- **End-to-end:** a separate Playwright package against the example apps.
- **No integration tests against a real tenant.** Verifications against tenant
  `viu` were done by hand and recorded as dated comments in the code (for example
  in `spec-patches.ts`, `config.ts`) — an honest practice, but not automated.

> **For .NET:** msw has no direct equivalent. **WireMock.Net** comes closest and
> allows the same testing philosophy (real `HttpClient`, stubbed server). An
> `HttpMessageHandler` stub is the lighter alternative for the core tests.
> Existing msw fixtures are useful as a reference for expected payloads but not
> directly reusable.

### Build

- **tsup** (esbuild), ESM plus CJS, `.d.ts`, source maps, tree shaking.
- 16 entry points (root plus 15 subpath exports).
- `check:treeshake` verifies that unused services fall out.
- Zero runtime dependencies in the core package.

### Versioning and release

- **Changesets.** Every PR carries a changeset file; `changeset-check.yml`
  enforces it.
- `@viu/emporix-sdk` and `@viu/emporix-sdk-react` are **linked** (a shared version
  line).
- Release: push to `main` → `changesets/action` opens a version PR → on merge
  `pnpm run release` (build plus `changeset publish`).
- **npm provenance enabled** (`publishConfig.provenance: true`) — OIDC-signed
  origin. The .NET equivalent is NuGet Trusted Publishing.
- A GitHub App token instead of `GITHUB_TOKEN`, because events from
  `GITHUB_TOKEN` do not trigger downstream workflows, which would leave the
  required checks stuck on «Expected».

### CI (7 workflows)

| Workflow | Purpose |
| --- | --- |
| `pr-check.yml` | Node matrix 20/22/24: build, dist guards, typecheck, lint, test, two Angular AOT builds |
| `release.yml` | Changesets release |
| `api-sync.yml` | **Daily at 06:00 UTC**: re-fetch specifications, regenerate on drift, smoke-test with treeshake, open a PR |
| `changeset-check.yml` | Enforces a changeset per PR |
| `dependabot-changeset.yml` | Changeset for Dependabot PRs |
| `e2e.yml` | Playwright |
| `pages.yml` | Demo deployment |

**`api-sync.yml` is the strongest piece in the repository** and worth adopting
directly. One detail deserves imitation: `fetch:specs` always rewrites the
manifest timestamps, so the manifest cannot signal real drift. The workflow
therefore inspects only the `*.yml` specifications and discards the manifest
change when nothing substantive moved.

---

## 8. Feature parity matrix, Node → .NET

Legend: **V1** = first prerelease · **V1.x** = after v1, before 1.0 · **Later** ·
**Dropped** = no .NET equivalent, or deliberately discarded.

### Infrastructure (core)

| Node | .NET counterpart | Scope | Note |
| --- | --- | --- | --- |
| `EmporixClient` | `EmporixClient` | **V1** | 1:1 |
| `createEmporixClient` (tree-shakeable) | — | **Dropped** | Bundle size is not a .NET problem |
| `createCore` | internal composition | **V1** | need not be public |
| Subpath exports | — | **Dropped** | as above |
| `EmporixConfig` plus `validateConfig` | `EmporixOptions` plus `IValidateOptions` | **V1** | fail fast at startup |
| `HttpClient` | typed client through `IHttpClientFactory` | **V1** | |
| `AuthContext` per call | `AuthContext` as the last parameter | **V1** | **core principle, non-negotiable** |
| `DefaultTokenProvider` (service tokens) | `ITokenProvider` plus `SemaphoreSlim` | **V1** | |
| Anonymous session plus renewal keeping `sessionId` | same | **V1** | |
| `CustomerRefreshRegistry` (single-flight) | same | **V1** | refresh-token rotation forces it |
| `AnonymousSessionStore` | `IAnonymousSessionStore` | **V1** | server variant only |
| Retry plus idempotency gate | own handler or Polly | **V1** | decided in Phase 1 |
| Timeouts (connect/read) | `HttpClient.Timeout` plus `CancellationToken` | **V1** | Node contrivance dropped |
| Pagination (3 tiers) | `PaginatedItems<T>` | **V1** | |
| `iterateAll` / `listAll` | `IAsyncEnumerable<T>` | **V1** | more idiomatic than in Node |
| SSE (`requestStream`) | `IAsyncEnumerable<SseEvent>` | **V1.x** | AI services only |
| `requestRaw` (binary/redirect) | returns `HttpResponseMessage` | **V1** | media needs it |
| Error hierarchy | exception hierarchy | **V1** | **plus** rate limit, field errors, correlation id |
| Logger (custom) | `ILogger` plus redaction | **V1** | no home-grown abstraction |
| Env-controlled log levels | — | **Dropped** | `ILogger` configuration covers it |
| `productIdFromYrn` / `productYrn` | static helpers | **V1** | |
| `resolveQuery` plus capability gate | same | **V1** | |
| Browser storage adapters | — | **Dropped** | |
| `query-keys` / `customer-session-store` | — | **Dropped** | React infrastructure only |

### Services

Proposed in three waves. **Full parity across all 649 operations is not a v1
goal** — by experience that is several weeks of pure facade work with no new
insight after the third service.

| Wave | Services | Ops (approx.) | Rationale |
| --- | --- | --- | --- |
| **V1** | products, categories, prices, carts, checkout, customers, orders, media, availability, catalogs, brands, labels | ~180 | The storefront path. Covers every read and the complete purchase. |
| **V1.x** | payments, coupons, taxes, shipping, fees, quotes, invoices, returns, segments, rewardPoints, companies, contacts, locations, customerGroups, customerAdmin, approvals | ~215 | B2B, loyalty and fulfilment. |
| **Later** | iam, schemas, webhooks, imports, indexing, ai, aiResources, ragIndexer, sequentialIds, units, countries, currencies, vendors, pickPack, shoppingLists, sites, sessionContext, tenantConfig, clientConfig, cloudFunctions | ~275 | Administration and platform. Little demand from a storefront perspective. |

The plan for the 36 that remain is in [roadmap.md](roadmap.md).

### Actual coverage (2026-08-31)

The twelve V1 services carry the Node SDK's full operation set. Measured by
counting public `Promise<T>` / `AsyncIterable<T>` returns in the Node facades
against public operations on the .NET services:

| Service | Node | .NET | Note |
| --- | ---: | ---: | --- |
| product | 24 | 23 | one variant listing covers both Node calls |
| category | 28 | 27 | ditto for the two assignment filters |
| brand | 6 | 6 | |
| label | 6 | 6 | |
| catalog | 7 | 7 | |
| cart | 28 | 28 | |
| customer | 24 | 23 | anonymous sessions live in the token provider |
| price | 31 | 31 | |
| availability | 9 | 9 | |
| checkout | 2 | 2 | |
| orders | 19 | 20 | split into storefront and administrative |
| media | 12 | 11 | create and update split by blob versus link |
| **Total** | **196** | **193** | |

The remaining differences are shape, not coverage: where Node has two calls
that differ only in how the result is filtered or paged, the .NET side has one.

Against the whole Node SDK that is **12 of 48 services**. The 36 absent
services are the deliberate part of the gap; closing them costs facade work
only, since the generated types are already there.

Four defects surfaced while closing the operation gap, each of them a call
that could never have worked:

| Defect | Was | Is |
| --- | --- | --- |
| Order paths | `/order/{tenant}/salesorders` | `/order-v2/{tenant}/…` |
| Order status | `PUT …/status/{status}` | `POST …/transitions` |
| Checkout | `/checkout/{tenant}/order` | `/checkout/{tenant}/checkouts/order` |
| Coupons | `…/coupons/{code}` | `…/discounts`, code in the body |
| Customer profile and address | `PUT` | `PATCH` |
| Category tree | `/categories/{id}/subcategories` | `/category-trees/{rootId}` |

Every one of them compiled and had a passing test, because the test asserted
the same wrong call the code built. `SpecPathTests` now reads the vendored
specifications and fails when a service builds a method-and-address pair none
of them declares.

The generation pipeline downloads and generates **all 43 specifications** from the
start — only the hand-written facades are staged. A later wave then costs only
the facade, never generator work again.

### Tooling and process

| Node | .NET | Scope |
| --- | --- | --- |
| `fetch-specs.ts` plus sync manifest (sha256) | `dotnet run --project tools/SpecSync` | **V1** |
| `spec-patches.ts` (idempotent, stale detection) | the same concept, ported | **V1** |
| `@hey-api/openapi-ts` (types only) | generator decision → Phase 1 ADR | **V1** |
| `api-sync.yml` (daily) | GitHub Action, identical | **V1** |
| msw tests | WireMock.Net / `HttpMessageHandler` | **V1** |
| Coverage 80/80 | same | **V1** |
| Changesets | MinVer/Nerdbank plus `CHANGELOG.md` | **V1** |
| npm provenance | NuGet Trusted Publishing (OIDC) | **V1**, if available |
| `check:treeshake` | `EnablePackageValidation` | **V1** |
| Playwright end-to-end | — | **Dropped** |
| `@viu/emporix-mixins` code generation | source generator | **Later** |
| React/Next/Angular bindings | — | **Dropped** |

---

## 9. Assessment: what to keep and what not to

### Keep — these are the good ideas

1. **`AuthContext` per call, never on the client.** It makes one instance
   thread-safe for many users and fits a DI singleton in ASP.NET Core perfectly.
2. **Generated types, hand-written client.** The generator supplies DTOs, the
   facade supplies behaviour. Full control over the public surface.
3. **The idempotency gate on retries.** Prevents duplicate orders. Non-negotiable.
4. **Single-flight locks on every token renewal.** Refresh-token rotation makes it
   mandatory.
5. **Committed specifications with a sha256 manifest and a daily sync PR.**
6. **Specification patches with stale detection** and the discipline to patch only
   demonstrated defects.
7. **Redaction that cannot be switched off.**
8. **Renew rather than re-login for anonymous sessions**, to preserve the
   `sessionId`.

### Deliberately different

| Node | .NET | Why |
| --- | --- | --- |
| Tree-shakeable factory plus subpath exports | one client, one package | Bundle size does not exist as a problem in .NET |
| Custom `Logger` abstraction | `ILogger` | A home-grown abstraction with no use case |
| Environment variables for log levels | `ILogger` configuration | The framework already does this |
| Timeout race against the body read | `ResponseHeadersRead` plus `CancellationToken` | Works around a `fetch` shortcoming |
| Request id as a process counter, log only | a real correlation id on every exception | The Node solution is useless in production |
| 429 with no type, `Retry-After` discarded | `EmporixRateLimitException` with `RetryAfter` | The caller must be able to back off |
| Error body forwarded raw | typed `errorCode`/`message`/`details` | Parse both Emporix error formats |
| Nested object literals | nested classes on a `readonly` property | Idiomatic in C# |
| `Record<string, string\|number>` for queries | typed parameter objects | C# has no reason to be stringly typed |

### Where the Node SDK is weak

- **No correlation id on the wire and none on exceptions.** The biggest
  operational gap.
- **No rate-limit signal for the caller.**
- **Error payloads are not parsed**, even though Emporix specifies a clean error
  schema.
- **Scopes exist only as an opaque string.** A 403 reveals what is missing only at
  runtime.
- **No automated integration tests.** Verifications against tenant `viu` are dated
  comments — honestly documented, but not repeatable.
- **The `TENANT_RE` guard is an assumption presented as a rule.** A 17-character
  tenant would be rejected by the SDK even though Emporix accepts it. For .NET I
  would check only for lowercase and drop the length assumption.

---

## 10. Assumptions and open points

### Assumptions made

1. Only `packages/sdk` is to be ported; the three framework bindings are dropped
   without replacement.
2. The browser is not a target. Everything that exists only for bundle size or
   `localStorage` falls away.
3. Full parity across all 649 operations is a goal for 1.0, not for the first
   prerelease.
4. The generation pipeline covers all 43 specifications from v1 onwards, including
   services whose facade comes later.

### Not conclusively verified

- I was able to read the official Emporix service list; a complete
  endpoint-by-endpoint comparison between specification and facade was **not**
  done. The 649 operations are counted, not validated against the specifications.
- Whether NuGet Trusted Publishing via OIDC is available to the organisation is
  open.

### Questions for you (blocking for Phase 1)

From your own «to be clarified» list — answers are needed before the ADRs make
sense:

1. **PackageId and root namespace** — proposal: `Viu.Emporix` / `Viu.Emporix.Sdk`.
2. **GitHub repository and visibility** — public, I assume, as with the Node SDK?
3. **License** — MIT, as with the Node SDK?
4. **Target frameworks** — `net10.0` only, or additionally `net8.0` (LTS)?
5. **Native AOT** — required? It decides source generation and the choice of
   generator.
6. **Services in scope for v1** — does the wave proposal from §8 work?
7. **Sandbox tenant for integration tests** — available?
8. **Trademark question around «Emporix»** — has it been agreed with Emporix that
   the name may appear in the package name? The Node SDK already uses it under the
   `@viu/` scope; NuGet has no comparable scope mechanism, so the name alone
   carries the distinction less clearly.

### Additional recommendation

`pageSize` default: Emporix documents 60, the Node SDK uses 50. I suggest taking
**60** (following the specification) and noting the difference in the migration
section of the README — or deliberately staying at 50 if behavioural equivalence
matters more. Your call.

---

**Phase 0 is complete. No code written.** Next step after your feedback: the ADRs
from Phase 1.

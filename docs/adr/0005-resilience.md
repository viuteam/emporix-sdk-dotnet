# ADR-0005 — Resilience: retry, backoff, timeouts

**Status:** Proposed · **Date:** 2026-08-31 · Affects: [ADR-0004](0004-aot-trimming.md)

## Context

The Node SDK implements retry itself, in roughly 40 lines in `core/http.ts`. The
logic is not generic; it knows Emporix:

- Retryable: **5xx and 429**.
- **Idempotency gate:** GET/PUT/DELETE retry by default, POST/PATCH only with an
  explicit `idempotent: true`. The reason is in the code: a 5xx can arrive
  *after* the server committed the write — replaying a POST could duplicate an
  order.
- A numeric `Retry-After` wins, **capped at 8 s** (against values like 86400).
- Otherwise `min(1000 · 2^(n-1), 8000)` plus up to 100 ms of jitter.
- An empty `Retry-After` header must not become `Number(null) === 0` and thereby
  an instant retry — a pitfall explicitly commented in the Node code.

Alongside this runs 401 handling, which is not a retry in the classic sense:
SDK-owned tokens are invalidated and the request repeated once, caller-owned 401s
propagate.

## Options

| Option | For | Against |
| --- | --- | --- |
| **Own retry logic** in a `DelegatingHandler` | No dependencies. The idempotency gate and the 401 path live where they belong anyway. Directly testable. | ~80 lines of C# we maintain |
| `Microsoft.Extensions.Http.Resilience` (Polly) | Battle-tested, circuit breaker and rate limiter thrown in | **30 transitive packages** (measured) |

### The measurement

`dotnet list package --include-transitive` for
`Microsoft.Extensions.Http.Resilience` 9.x on `net10.0`:

> 30 transitive packages, among them `Microsoft.Extensions.Telemetry`,
> `Microsoft.Extensions.Compliance.Abstractions`,
> `Microsoft.Extensions.Hosting.Abstractions`,
> `Microsoft.Extensions.DependencyInjection.AutoActivation`, `Polly.Core`,
> `Polly.Extensions`, `Polly.RateLimiting`, `System.Threading.RateLimiting`.

For comparison: the Node SDK has **no** runtime dependencies, and the brief names
«as few transitive dependencies as possible» as an explicit goal.

On top of that, every one of those 30 dependencies has to be trim-clean under
`TreatWarningsAsErrors` ([ADR-0004](0004-aot-trimming.md)). A single trim warning
anywhere in that chain blocks our build with no way for us to fix it.

## Decision

**Own retry logic in a `DelegatingHandler`.**

Polly pulls 30 packages for ~80 lines of code we would have to parameterise for
Emporix regardless — the idempotency gate and the `Retry-After` cap are not
standard policies but knowledge about this API.

Timeouts through `HttpClient.Timeout` plus a `CancellationToken`, with
`HttpCompletionOption.ResponseHeadersRead`. The Node contrivance — racing a
rejecting promise against the body read — works around a `fetch` shortcoming and
is dropped.

## Consequences

**Good:** No dependencies. Retry, 401 re-authentication and the correlation id
live in the same handler stack and can coordinate rather than communicate across
a policy boundary. The handler is directly testable with an
`HttpMessageHandler` stub, without Polly test infrastructure.

**Cost:** We maintain the code. No circuit breaker and no client-side rate
limiter — the Node SDK lacks both as well and has not missed them. Anyone needing
them can put their own `DelegatingHandler` in front of ours; that stays possible
because we use `IHttpClientFactory`.

**Explicitly better than Node:** `Retry-After` is not only read internally for the
backoff but passed to the caller on an `EmporixRateLimitException`. In the Node
SDK that information disappears — the caller cannot back off on their own.

**Revisitable:** Should a genuine need for circuit breaking appear, moving to
Polly is an internal change to the handler stack with no effect on the public API.

# Roadmap: the remaining 36 services

Twelve of Emporix's 48 services are implemented, with 193 of the operations the
API offers for them. This plans the other 36.

Measured against the Node SDK's public operations, not estimated:

| | Services | Operations |
| --- | ---: | ---: |
| Implemented | 12 | 193 |
| **Remaining** | **36** | **433** |

The generated types for all of them already ship in the package — 43
specifications, ~55,000 lines. A remaining service costs a hand-written facade
and its tests, never generator work.

## What the first twelve taught us

Worth stating before planning more of the same, because it changes what the plan
should optimise for.

**The facades were not the expensive part.** Writing the 109 new operations was
mechanical once the patterns existed. What cost time was fifteen defects, and
not one of them was found by a unit test:

| Found by | Defects |
| --- | ---: |
| Reading the specification against the code | 7 |
| Calling the live API once | 7 |
| Building a sample that consumes the package | 1 |
| **Unit tests** | **0** |

That is not an argument against the 375 unit tests — they pin behaviour the next
change could break, and several were written precisely to pin a defect once it
was understood. It is an argument that **a facade is not done when its tests
pass.** Six calls shipped pointing at endpoints Emporix does not have, each with
a green test asserting the same wrong address as the code.

Two guards came out of that and now apply to every service added:

- [`SpecPathTests`](../tests/Viu.Emporix.Tests/SpecPathTests.cs) fails the build
  when a service builds a method-and-address pair no specification declares.
  This is free for new services — it scans whatever is there.
- [`samples/Viu.Emporix.SmokeTest`](../samples/Viu.Emporix.SmokeTest) walks a
  real tenant. It found the seven the specifications could not.

**Three specifications disagree with the API.** Repaired in the patch pipeline,
with stale detection so they announce themselves when Emporix fixes them. Expect
more: at three findings per twelve services, the remaining 36 should surface
roughly a further nine. Budget for them rather than treating each as a surprise.

## Grouping

Ordered by who is blocked without them, not by size. A storefront can already
browse, price and order; everything below extends that.

### Wave 2 — completing a real checkout (7 services, 104 operations)

The purchase path works, but only for the simple case. These are what a
production storefront needs before it can take money.

| Service | Ops | Why here |
| --- | ---: | --- |
| `payment` | 15 | Cannot charge anybody without it. 11 operations are nested under modes and transactions. |
| `shipping` | 45 | The largest single facade. Zones, methods, quotes — a cart cannot be delivered without one. |
| `tax` | 6 | Small, and every total is wrong without it. |
| `coupon` | 13 | Carts already apply coupons; nothing can create or validate one. |
| `fee` | 17 | Fees already appear on carts and orders as read-only values. |
| `returns` | 6 | The other half of an order. |
| `invoice` | 2 | Two operations, and the accounting side expects them. |

New infrastructure: none. Payment's nested groups follow the pattern already
built for `Prices.Models` and `Prices.Lists`.

### Wave 3 — B2B (8 services, 92 operations)

`OrderService.ListForLegalEntityAsync` already exists and has nothing to point
at. These make the legal entity a real thing.

| Service | Ops | Why here |
| --- | ---: | --- |
| `companies` | 7 | The legal entity itself. |
| `contacts` | 5 | Who acts for it. |
| `locations` | 5 | Where it receives. |
| `customer-groups` | 3 | Pricing and approvals key off these. |
| `customer-admin` | 19 | Managing customers rather than being one. |
| `approval` | 7 | B2B carts wait for approval; the spec patch for its enum is already in place. |
| `quote` | 12 | `Checkout.PlaceOrderFromQuoteAsync` exists and has nothing to create a quote with. |
| `segment` | 34 | Customer segments, 16 operations nested under customers and items. |

Note: `companies`, `contacts` and `locations` all come out of
`customer-management.yml`, so one specification serves three facades.

### Wave 4 — platform and administration (12 services, 133 operations)

Nothing here blocks a storefront. It blocks whoever has to operate the tenant.

| Service | Ops |
| --- | ---: |
| `iam` | 30 |
| `schema` | 24 |
| `site` | 14 |
| `vendor` | 11 |
| `currency` | 10 |
| `webhook` | 10 |
| `unit-handling` | 9 |
| `sequential-id` | 8 |
| `country` | 5 |
| `client-config` · `tenant-config` | 8 |
| `session-context` | 4 |

`iam` is 30 operations of which all 30 are nested — users, groups, access
controls, scopes. It is the widest facade in the SDK and wants its own review.
`schema` needs multipart upload, already built for media. `client-config` and
`tenant-config` share `configuration.yml`.

### Wave 5 — needs a decision first (9 services, 104 operations)

Not last because they matter least, but because each raises a question the other
waves do not.

| Service | Ops | The question |
| --- | ---: | --- |
| `ai` | 17 | Streams responses. Needs `IAsyncEnumerable<SseEvent>` and an SSE reader the SDK does not have. |
| `ai-resources` | 21 | Sub-resources of the AI service; scope depends on what `ai` becomes. |
| `ai-rag-indexer` | 3 | Same. |
| `imports` | 19 | Also streams, for progress. Long-running jobs want a polling helper rather than 19 bare calls. |
| `indexing` | 11 | Long-running in the same way. |
| `pick-pack` | 12 | Fulfilment; needs a warehouse to test against, which we do not have. |
| `shopping-list` | 7 | The Node facade is hand-written against no specification — verify before porting. |
| `reward-points` | 14 | Loyalty. No open question, but no caller either until someone asks. |

`cloud-functions` (one operation, counted above): a generic invoke with caller-supplied
request and response types. Under the no-reflection rule the caller has to pass
`JsonTypeInfo<T>`, which makes it the only service whose signature is dictated
by the AOT constraint. Worth deciding deliberately rather than in passing.

## The three decisions to take before coding

1. **SSE.** `ai` and `imports` stream.
   [The analysis](analysis.md#infrastructure-core) put
   `IAsyncEnumerable<SseEvent>` in scope for V1.x, but nothing was built and no
   ADR covers it. Needed before either service: where the reader lives, how
   cancellation behaves, whether a dropped stream is an exception or an end, and
   whether it reconnects at all.
2. **Long-running jobs.** `imports`, `indexing` and product recalculation all
   return a job and expect polling. `Products.GetRecalculationJobAsync` already
   exists as a bare call. Either every service polls by hand, or there is one
   `WaitForAsync` helper. Deciding once is cheaper than three times.
3. **`cloud-functions`.** A generic invoke with no generated types. Either the
   caller passes `JsonTypeInfo<T>` — honest but unusual — or the service is left
   out and callers use the `HttpClient` directly. Leaving it out is defensible.

## Per-service checklist

What «done» means, derived from the twelve. Every item on it caught something
real at least once.

1. Read the vendored specification for the paths and verbs. Not the Node facade:
   it has the two PUT-instead-of-PATCH defects this SDK inherited.
2. Write the facade. Nested groups as a `readonly` property returning an
   operations class, as `Prices.Lists` does.
3. Register the types in a **per-service** `JsonSerializerContext`. Never a
   shared one — Emporix reuses type names across specifications and a shared
   context fails with `SYSLIB1031`.
4. Guard the auth context where the wrong kind fails quietly rather than loudly.
   Price matching with a service token returns an empty list; that class of
   failure is worth an exception.
5. Mark a `POST` that only reads as `Idempotent`. Never mark one that creates.
6. Wire into DI and `EmporixClient`, run `scripts/update-public-api.sh`.
7. Tests: the paths, the auth guards, the idempotency decisions, and anything
   where a defect would be silent.
8. **Call it against a live tenant.** Add a step to the smoke test for anything a
   storefront touches. This is the step that found seven of eight defects.
9. `dotnet build` (0 warnings), `dotnet test`, AOT publish.

## What not to do

- **Not one release per wave.** The waves are an order of work, not a versioning
  scheme. Ship when a coherent capability is complete.
- **Not full parity as a goal in itself.** 433 operations includes plenty that
  nobody will call. A facade with no caller is untested code with a maintenance
  cost. Prefer a service someone asked for over the next one in the list.
- **Not skipping the live call because the tests pass.** That is the one lesson
  the first twelve paid for.

## Status

| Wave | Services | Operations | State |
| --- | ---: | ---: | --- |
| 1 | 12 | 193 | done |
| 2 — checkout | 7 | 104 | planned |
| 3 — B2B | 8 | 92 | planned |
| 4 — platform | 12 | 133 | planned |
| 5 — needs decisions | 9 | 104 | blocked on the three ADRs |

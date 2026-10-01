# Cart command chains — Design

- **Date:** 2026-10-01
- **Status:** Approved 2026-10-01 → [implementation plan](../plans/2026-10-01-cart-command-chain.md)
- **Affects:** `Viu.Emporix` — `CartService`, `JsonContexts.cs`, two new files;
  `specs/cart.yml` and `Generated/Cart.cs` through the sync; the smoke test
- **Related:** [ADR-0001](../../adr/0001-type-generation.md) type generation,
  [ADR-0004](../../adr/0004-aot-trimming.md) AOT and trimming, the Node SDK's
  [viuteam/emporix-sdk#361](https://github.com/viuteam/emporix-sdk/pull/361)

## Upstream

Emporix changelog, 2026-09-30: *Cart Service – command chain endpoint for cart
operations*. The Emporix documentation connector did not serve that entry yet
on 2026-10-01, nor the tutorial section the specification links to; the API
reference page «Execute» and the specification itself did.

`POST /cart/{tenant}/carts/{cartId}/execute` (`POST-cart-execute`) runs up to
ten existing cart operations on one cart, in order, in one request. The REST
endpoints stay as they are; this is an additional way to call them.

| Part | Content |
|---|---|
| Body | `{ commands }`, 1–10 entries. Each has a `type` — `AddCartItem`, `UpdateCartItem`, `DeleteCartItem`, `DeleteCartItems`, `GetCart`, `AddCartItemsBatch`, `UpdateCartItemsBatch`, `UpdateCart`, `ApplyCartDiscount`, `GetCartDiscounts`, `DeleteCartDiscounts`, `DeleteCartDiscount`, `RefreshCart`, `ValidateCart` — an optional `data` (the REST request body) and optional `options` (the remaining path and query parameters: `itemId`, `partial`, `expandCalculation`, `zipCode`, `countryCode`, `resourceVersion`, `codes`, `discountIndex`) |
| Query | `onError`: `fail` (default) stops after the first non-2xx command, `resume` runs every command. `versioning`: `skip` (default), `explicit` (every participating write sends `options.resourceVersion`, checked before anything runs) or `follow` (seeded by the first participating write) |
| Headers | `session-id`, `legal-entity-id`, optional, shared by every command |
| `207` | `{ results: [{ index, type, code, status, data?, headers? }] }`, for all-2xx chains **and** for partial failures. `data` is the REST response body, or the REST error body; absent where REST answers `204` |
| `400` | the whole request is invalid — **no command ran** |
| Security | a customer token, or OAuth2 `cart.cart_manage`; `cart.cart_manage_external_prices` when a command carries an external price, product, fee or discount |

The request takes about as long as its commands together; Emporix asks clients
and gateways to size their timeouts for the whole chain.

## Verified groundwork

Measured in this repository on 2026-10-01 unless a line says otherwise.

### The vendored specification does not have the endpoint yet

- The last scheduled sync ran on 2026-09-30 at 11:38 UTC and became #51, which
  moved `site-settings-service` only. The cart change landed upstream after it.
- `dotnet run --project tools/Viu.Emporix.SpecSync -- fetch` ends with
  `Changed: cart`. The diff is **+482 lines and nothing removed**: one tag, one
  path, five schemas — `executeRequest`, `executeCommand`,
  `executeCommandOptions`, `executeResponse`, `executeCommandResult`.
- `generate` then touches `Generated/Cart.cs` only, +357 lines.
- `SpecPathTests` afterwards reports exactly one operation without a facade
  beyond the five known gaps: `POST /cart/{}/carts/{}/execute`.

**Consequence for delivery.** The scheduled workflow runs the tests before it
opens a pull request, so its next run fails at «Build and test» and opens
nothing. The vendoring therefore travels in the feature pull request, as the
sync skill prescribes for a person closing a gap. The Node SDK could let its bot
vendor first; this repository cannot.

### What the generator makes of the five schemas

```csharp
public partial class ExecuteCommand
{
    public ExecuteCommandType Type { get; set; }          // 14 members, strict converter
    public object? Data { get; set; }                     // oneOf [object, array]
    public ExecuteCommandOptions? Options { get; set; }
}

public partial class ExecuteCommandOptions
{
    public bool? Partial { get; set; } = false;           // generated default
    public bool? ExpandCalculation { get; set; } = true;  // generated default
    // ItemId, ZipCode, CountryCode, ResourceVersion, Codes, DiscountIndex
}

public partial class ExecuteCommandResult
{
    public int Index { get; set; }
    public string Type { get; set; }                      // a string, not the enum
    public int Code { get; set; }
    public string Status { get; set; }
    public object? Data { get; set; }
    public IDictionary<string, string>? Headers { get; set; }
}

public enum OnError { Fail, Resume }                      // [JsonStringEnumMemberName] "fail", "resume"
public enum Versioning { Skip, Explicit, Follow }         // "skip", "explicit", "follow"
```

Four consequences, each a known trap of this repository:

1. **`ExecuteCommand.Data` cannot be written.** An `object`-typed property is
   only serialisable by a source-generated context when its runtime type is
   registered there, and a boxed `JsonElement` was probed and threw
   `NotSupportedException` (facade standard). Reading the same shape on
   `ExecuteCommandResult.Data` should work — System.Text.Json materialises
   `object` as a `JsonElement` — but that is not measured yet; the reader tests
   are what prove it.
2. **The generated defaults would be sent.** `DefaultIgnoreCondition =
   WhenWritingNull` skips nulls only, so a command whose options were built
   with `new ExecuteCommandOptions { ItemId = … }` also sends
   `"partial":false,"expandCalculation":true`.
3. **The query enums need explicit wire values.** `ToString` yields `Fail`, the
   specification wants `fail`.
4. **A caller cannot serialise the bodies.** `CartJsonContext` is internal, so
   putting a `CartItemRequest` into `data` would take the caller's own context or
   reflection — what an AOT library exists to avoid.

### A generator rule is not the fix here

Among schema properties, a `oneOf` with an inline object and an inline array
branch occurs twice in `cart` and twice elsewhere: ai-service's
`ChatStreamToolResultData.output`, read from an SSE stream, and configuration's
`BaseConfiguration.value`, which admits a string as well. Teaching
`LocalizedProperties.ReadUnions` the pattern would reach those too. For
`BaseConfiguration.value` that might even repair a write — it is generated as
`object Value`, and `ConfigurationJsonContext` registers no `JsonElement` — but
both are type changes for their callers and belong in their own change.
The repository already has the precedent for a body the generated type cannot
write: `MediaPatchOperation` replaces a generated `object? Value` with
`JsonElement?`.

### What each command returns

From the REST operations in `specs/cart.yml`, which the `data` description of
`executeCommandResult` repeats:

| Command | REST answer | Generated type |
|---|---|---|
| `AddCartItem` | `201` | `CreatedCartItem` (`itemId`, `yrn`) |
| `AddCartItemsBatch` | `200` | `BatchResponse` (`Collection<SingleBatchResponse>`) |
| `UpdateCartItemsBatch` | `207` | `CartItemsBatchUpdateResponse` |
| `ApplyCartDiscount` | `201` | `AppliedDiscount` |
| `GetCartDiscounts` | `200` | `List<DiscountResponse>` |
| `GetCart` | `200` | `Cart` |
| `ValidateCart` | `200` | `CartValidationResult` |
| every other command | `204` | — |

### The Node SDK shipped the same endpoint today

viuteam/emporix-sdk#361, merged 2026-10-01 05:33 UTC. Its decisions, adopted
here where they apply: the first non-2xx result under `fail` is thrown as the
error the REST call would have produced; the call is never retried; the
`session-id` and `legal-entity-id` headers are left out, as on every REST cart
call; there is no client-side check of the 10-command limit.

It was verified on tenant `viu` with an anonymous throwaway cart and **no
writes**: `GetCart` + `ValidateCart` answered `207` with `200`, `200`; an
unknown item under `DeleteCartItem` threw a not-found error naming the command
under `fail`, and produced `404`, `200` under `resume`. No write command has
been seen live by anyone.

## Design — three units

### Unit 1 — `CartCommand` (`src/Viu.Emporix/CartCommand.cs`)

A hand-written replacement for the generated `ExecuteCommand`, for the reason
in the groundwork:

```csharp
public sealed class CartCommand
{
    [JsonPropertyName("type")]
    public required ExecuteCommandType Type { get; init; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("options")]
    public ExecuteCommandOptions? Options { get; init; }

    public static CartCommand AddCartItem(CartItemRequest item, int? resourceVersion = null);
    // … one factory per command type
}
```

- **`Type` is `required`.** A non-nullable generated enum defaults to its first
  member, so a command built without a type would silently mean `AddCartItem`.
- **One factory per command type, named after it** — `CartCommand.GetCart()`
  produces `type: GetCart`. This departs from the first proposal, which borrowed
  the REST method names (`RemoveItem` for `DeleteCartItem`): the command names
  are what the documentation, the result's `Type` and every error message use,
  and a 1:1 mapping needs no table.

| Factory | `data` | `options` |
|---|---|---|
| `AddCartItem(CartItemRequest item, int? resourceVersion = null)` | the item | `resourceVersion` |
| `UpdateCartItem(string itemId, UpdateCartItem changes, bool? partial = null, int? resourceVersion = null)` | the changes | `itemId`, `partial`, `resourceVersion` |
| `DeleteCartItem(string itemId)` | — | `itemId` |
| `DeleteCartItems()` | — | — |
| `GetCart(bool? expandCalculation = null, string? zipCode = null, string? countryCode = null)` | — | the three, zip and country both or neither |
| `AddCartItemsBatch(IEnumerable<CartItemRequest> items)` | `CartItemsBatchRequest` | — |
| `UpdateCartItemsBatch(IEnumerable<CartItemRequest> items, bool? partial = null)` | `CartItemsBatchUpdateRequest` | `partial` |
| `UpdateCart(UpdateCart cart, int? resourceVersion = null)` | the cart | `resourceVersion` |
| `ApplyCartDiscount(Discount discount, int? resourceVersion = null)` | the discount | `resourceVersion` |
| `GetCartDiscounts()` | — | — |
| `DeleteCartDiscounts(IEnumerable<string>? codes = null)` | — | `codes`; all discounts when null, an empty list rejected |
| `DeleteCartDiscount(int discountIndex)` | — | `discountIndex`, formatted invariant |
| `RefreshCart()` | — | — |
| `ValidateCart()` | — | — |

- **`data` is serialised by the factory** through `CartJsonContext` —
  `JsonSerializer.SerializeToElement(item, CartJsonContext.Default.CartItemRequest)`
  — the same type info the REST method uses, so both paths send the same JSON.
- **Options are built by one private helper that sets all eight properties**,
  `null` unless the caller gave a value, and the factory passes `null` options
  when there is nothing to send. That is what keeps the generated
  `Partial = false` and `ExpandCalculation = true` off the wire.
- **Argument checks** follow the REST methods: `ThrowIfNullOrWhiteSpace` on
  ids, `ThrowIfNull` on bodies, `ThrowIfNegative` on the discount index, an
  empty batch rejected, zip and country only together. An empty `codes` list
  is rejected rather than sent: the server reads a missing filter as «remove
  every discount», and a caller whose filter came out empty by accident must
  not get that.
- **A command type the SDK has not mapped stays usable.** After a sync brings a
  fifteenth `ExecuteCommandType`, `new CartCommand { Type = …, Options = … }`
  reaches it at once; only a body needs a factory.

### Unit 2 — `CartService.ExecuteAsync`

```csharp
public async Task<ExecuteResponse> ExecuteAsync(
    string cartId,
    IEnumerable<CartCommand> commands,
    AuthContext auth,
    OnError? onError = null,
    Versioning? versioning = null,
    CancellationToken cancellationToken = default)
```

The parameter order follows `SearchAsync`, the cart method that already has
optional parameters after a required `auth`.

1. **Path** `$"{BasePath}/{Uri.EscapeDataString(cartId)}/execute"` — one
   interpolated string, so `SpecPathTests` sees it.
2. **`onError` and `versioning` go into the query string**, mapped by a
   `switch` to `fail`/`resume` and `skip`/`explicit`/`follow`, and are absent
   when null. The body is `{ commands }` only: an internal `CartCommandChain`
   type with nothing else on it.
3. **`RequireCartAuth(auth)`** — a customer or anonymous context, like every
   shopper operation in the class; a service context throws
   `EmporixConfigurationException` before the request leaves. Decided on
   2026-10-01. The specification also admits a service token with
   `cart.cart_manage`; loosening the guard later is not a breaking change,
   tightening it would be.
4. **Never retried.** No `Idempotent` flag: a replay would apply the writes
   twice, and the worst command in a chain decides.
5. **Argument checks**: `ThrowIfNullOrWhiteSpace(cartId)`, `ThrowIfNull` on the
   commands and on each element, `ThrowIfZero` on the count. The upper bound
   stays the server's, which answers `400`.
6. **Failures.** Unless `onError` is `Resume`, the first result whose `Code` is
   not 2xx is thrown through
   `EmporixErrorParser.CreateException((HttpStatusCode)code, description, body)`
   — the same class and message parsing the REST call would have produced, so an
   unknown item is an `EmporixNotFoundException`. The description names the
   command: `POST /cart/acme/carts/c1/execute, command 1 (DeleteCartItem)`. The
   commands before it **have been applied**, and the XML docs say so; the
   exception does not carry their results — a caller who needs them passes
   `Resume`. Under `Resume` the method returns every result, and the caller
   reads `Code`.
7. **No body is no results.** `SendAsync` returns null without a body; the
   method returns an empty `ExecuteResponse` rather than a nullable one.

XML docs carry what the signature cannot: the partial-failure semantics, that
a `GetCart` before a later write returns a cart that is already stale, the
timeout (the whole chain against the client's timeout, and a timeout does not
mean nothing was written), `resourceVersion` under `explicit` and `follow`, and
the external-price scope.

### Unit 3 — reading results (`src/Viu.Emporix/CartCommandResult.cs`)

A `partial` of the generated `ExecuteCommandResult`, in `Viu.Emporix.CartModels`
as `EmporixProduct.cs` does for the product types:

| Reader | For | Returns |
|---|---|---|
| `ReadCart()` | `GetCart` | `Cart?` |
| `ReadCreatedItem()` | `AddCartItem` | `CreatedCartItem?` |
| `ReadAddedItems()` | `AddCartItemsBatch` | `IReadOnlyList<SingleBatchResponse>` |
| `ReadUpdatedItems()` | `UpdateCartItemsBatch` | `IReadOnlyList<UpdateCartItemsBatchEntryResponse>` |
| `ReadAppliedDiscount()` | `ApplyCartDiscount` | `AppliedDiscount?` |
| `ReadDiscounts()` | `GetCartDiscounts` | `IReadOnlyList<DiscountResponse>` |
| `ReadValidation()` | `ValidateCart` | `CartValidationResult?` |

- **A reader called on the wrong command throws** `InvalidOperationException`
  — `ReadCart()` on a `ValidateCart` result would otherwise deserialise into a
  mostly empty `Cart` and say nothing.
- **A failed command reads as `null`** (an empty list for the list readers):
  its `data` is the error body, not the type.
- Readers deserialise `Data` — a `JsonElement` at runtime — through
  `CartJsonContext`.

### Serialization

`CartJsonContext` gains `CartCommandChain`, `ExecuteResponse`,
`CreatedCartItem` and `AppliedDiscount`. `CartCommand` and
`ExecuteCommandOptions` come in through the chain; everything else a factory or
reader needs is registered already for the REST methods.

The generated `ExecuteRequest` and `ExecuteCommand` stay unused, as
`AgentTrigger` does.

## Testing

`tests/Viu.Emporix.Tests/CartServiceTests.cs`, `StubHttpMessageHandler`:

- verb and path, with an id that needs escaping: `c 1` → `/cart/acme/carts/c%201/execute`;
- the body holds `commands` and nothing else; `onError` and `versioning` in the
  query with their wire values, and absent when unset;
- not idempotent;
- a service context throws before any request; an empty list, a null list and a
  null element throw before any request;
- per factory: `type`, `data` and `options` on the wire — and **no** `partial`
  or `expandCalculation` unless given;
- `GetCart` with a zip code and no country throws;
- `fail` and the default: a `207` whose second result is `404` throws
  `EmporixNotFoundException` naming `command 1 (DeleteCartItem)`;
- `Resume`: the same `207` comes back whole;
- readers: each one reads its type; a failed result reads as `null`; a reader on
  the wrong command throws.

Each rule is broken once on purpose — the factory's `null` for `Partial`, the
query mapping, the auth guard, the throw — to show the right test fails. The
existing `SpecPathTests` turns from one gap to none.

## Live verification

The smoke test's anonymous storefront flow already creates a throwaway cart,
adds an item and deletes the cart at the end. Three steps go between them, all
on that cart:

1. `AddCartItem` + `GetCart`: the created item id reads back, and the cart holds
   the item;
2. `UpdateCartItem` (partial, a new quantity) on that item + `GetCart`: the
   quantity reads back changed. With step 1 the first live proof of bodies
   sent through `data`;
3. `DeleteCartItem` with an unknown id: throws not-found under `fail`, and
   `404`, `200` with a following `ValidateCart` under `Resume`.

Nothing leaves the cart, which the existing last step deletes. Service tokens,
`versioning` and the discount commands stay **unverified live**, and the pull
request says so per method.

## Delivery

One branch, `feat/cart-command-chain`, one pull request, subject
`feat: run a chain of cart commands in one request`. It carries the vendored
`specs/cart.yml`, the regenerated `Generated/Cart.cs` and the manifest. If
`fetch` names another service by then, stop and decide with the user rather
than restore that file: the manifest would record its new hash, and the next
run would consider a spec current that was never vendored. The public API
surface comes from `./scripts/update-public-api.sh`. Also updated: the
`client.Carts` row in `README.md` and the call count in `CLAUDE.md`. The AOT
check is `dotnet publish samples/Viu.Emporix.Sample -c Release`.

## Assumptions

- No `session-id` or `legal-entity-id` header, as on every REST cart call.
- No client-side check of the 10-command maximum; the server owns the limit.
- The generated names `OnError` and `Versioning` stay; they live in
  `Viu.Emporix.CartModels`.
- No fluent builder: a collection expression of factories reads as well, and
  the SDK has no builder anywhere else.

## Follow-up work, not in this change

1. **`AddItemAsync` returns the wrong type.** The specification answers
   `POST …/items` with `201 createdCartItem` (`itemId`, `yrn`); the method
   deserialises it into `CartItemResponse`, so the new item's id lands in
   `AdditionalProperties` and `Id` stays empty. The smoke test only checks for
   non-null. Fixing it changes a public return type.
2. **A REST gap the chain fills:** `ApplyCouponAsync` sends a code and nothing
   else, so an external discount is out of reach over REST; `ApplyCartDiscount`
   takes the whole `Discount`.
3. **A generator rule for inline object-or-array unions**, together with
   ai-service's `ChatStreamToolResultData.output` and configuration's
   `BaseConfiguration.value` — the latter possibly unwritable today with an
   object or array as its value, which a unit test would show first.

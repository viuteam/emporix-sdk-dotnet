# Cart Command Chain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap `POST /cart/{tenant}/carts/{cartId}/execute` as `CartService.ExecuteAsync`, with one `CartCommand` factory per command type and typed readers for the results, vendoring the cart specification in the same pull request.

**Architecture:** The sync brings five generated types. The generated command declares its body as `object? Data`, which no source-generated context can write, so a hand-written `CartCommand` with `JsonElement? Data` replaces it; its factories serialise each body through `CartJsonContext`. `ExecuteAsync` sends `{ commands }`, maps `onError` and `versioning` into the query, refuses service tokens and, unless the caller resumes, throws the first failed command as the exception its REST call would have raised. Readers on a `partial` of the generated `ExecuteCommandResult` deserialise a result's body into the type its command returns.

**Tech Stack:** .NET 10, C# 14, System.Text.Json source generation, NSwag through `tools/Viu.Emporix.SpecSync`, xUnit, Microsoft.CodeAnalysis.PublicApiAnalyzers.

**Spec:** `docs/superpowers/specs/2026-10-01-cart-command-chain-design.md`

## Global Constraints

- Do not invent endpoints, fields or scopes. Verify against `specs/cart.yml` or the Node SDK at `../emporix-sdk`, which shipped the same endpoint in viuteam/emporix-sdk#361.
- Never edit `src/Viu.Emporix/Generated/` by hand. It comes from `tools/Viu.Emporix.SpecSync`.
- `TreatWarningsAsErrors`, `AnalysisLevel` `latest-recommended`, `GenerateDocumentationFile` and `IsAotCompatible` are on: a warning fails the build, a `cref` to a member that does not exist yet fails it too, and nothing may use reflection.
- Every cart type goes into `CartJsonContext` in `src/Viu.Emporix/JsonContexts.cs`, as a fully qualified `[JsonSerializable]` entry. No new context.
- A request path is one interpolated string assigned as `Path = $"…"`. `SpecPathTests` sees nothing else.
- `ExecuteAsync` takes `AuthContext auth` without a default and passes it through `RequireCartAuth`: customer or anonymous only. Decided 2026-10-01.
- `ExecuteAsync` is not idempotent. Never set `Idempotent = true`.
- New public symbols need `./scripts/update-public-api.sh`; RS0016 is a build error. If you change a signature you added on this branch, reset `src/Viu.Emporix/PublicAPI.Unshipped.txt` to the branch point before re-running the script.
- English for code, comments, docs and commit messages; comments say why. No nested parentheses anywhere in a commit body. Every commit ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never print a token or a secret, and never `cat ~/.emporix-smoke.env`. Source it: `set -a; . ~/.emporix-smoke.env; set +a`.
- Work on `feat/cart-command-chain`, which already holds the design and this plan. Never push to `chore/spec-sync`, never merge, never touch the `release-please--branches--main` pull request.
- On this machine a shell variable set earlier in the same command line can expand empty, so the commands below use literal paths. The AOT publish needs `export LIBRARY_PATH=/opt/homebrew/lib`.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `specs/cart.yml`, `specs/sync-manifest.json` | by `fetch` | the vendored specification |
| `src/Viu.Emporix/Generated/Cart.cs` | by `generate` | the five `Execute*` types and the two query enums |
| `src/Viu.Emporix/CartCommand.cs` | create | `CartCommand`, its 14 factories, the options helper, the internal body `CartCommandChain` |
| `src/Viu.Emporix/CartService.cs` | modify | `ExecuteAsync` |
| `src/Viu.Emporix/CartCommandResult.cs` | create | the seven readers, a `partial` of the generated `ExecuteCommandResult` |
| `src/Viu.Emporix/JsonContexts.cs` | modify | four entries on `CartJsonContext` |
| `src/Viu.Emporix/PublicAPI.Unshipped.txt` | by script | the public surface |
| `tests/Viu.Emporix.Tests/CartServiceTests.cs` | modify | the unit tests |
| `samples/Viu.Emporix.SmokeTest/Program.cs` | modify | three live steps |
| `README.md`, `CLAUDE.md` | modify | the service row, a sample, the call count |

---

### Task 1: Vendor the cart specification

**Files:**
- Modify, by tool: `specs/cart.yml`, `specs/sync-manifest.json`, `src/Viu.Emporix/Generated/Cart.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, all in `Viu.Emporix.CartModels`:
  - `enum ExecuteCommandType` with the members `AddCartItem`, `UpdateCartItem`, `DeleteCartItem`, `DeleteCartItems`, `GetCart`, `AddCartItemsBatch`, `UpdateCartItemsBatch`, `UpdateCart`, `ApplyCartDiscount`, `GetCartDiscounts`, `DeleteCartDiscounts`, `DeleteCartDiscount`, `RefreshCart`, `ValidateCart`, serialised as those names;
  - `class ExecuteCommandOptions` with `string? ItemId`, `bool? Partial` (generated default `false`), `bool? ExpandCalculation` (generated default `true`), `string? ZipCode`, `string? CountryCode`, `int? ResourceVersion`, `ICollection<string>? Codes`, `string? DiscountIndex`;
  - `class ExecuteResponse` with `ICollection<ExecuteCommandResult> Results`;
  - `class ExecuteCommandResult` with `int Index`, `string Type`, `int Code`, `string Status`, `object? Data`, `IDictionary<string, string>? Headers`;
  - `enum OnError { Fail, Resume }` and `enum Versioning { Skip, Explicit, Follow }`;
  - `ExecuteRequest` and `ExecuteCommand`, which stay unused.

- [ ] **Step 1: Start from a current branch**

```bash
git fetch origin --prune
git switch feat/cart-command-chain
git log --oneline HEAD..origin/main
```

Expected: no output. If `main` moved, run `git rebase origin/main` first.

- [ ] **Step 2: Download the specifications**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch 2>&1 | tail -1
git diff --stat -- specs/
```

Expected: `fetch` ends with `Changed: cart`, and the diff lists `specs/cart.yml` with 482 insertions and no deletions, plus `specs/sync-manifest.json`. **If `fetch` names any other service, stop and ask the user.** Restoring that file would leave its new hash in the manifest, and the next sync would consider a specification current that was never vendored.

- [ ] **Step 3: Regenerate the types**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- generate 2>&1 | grep "cart →"
git diff --stat -- src/Viu.Emporix/Generated/
grep -cE "public (partial class|enum) (ExecuteRequest|ExecuteCommand|ExecuteCommandOptions|ExecuteResponse|ExecuteCommandResult|ExecuteCommandType|OnError|Versioning)( |$)" src/Viu.Emporix/Generated/Cart.cs
grep -c "public object? Data" src/Viu.Emporix/Generated/Cart.cs
```

Expected: only `src/Viu.Emporix/Generated/Cart.cs` changed, about 357 insertions; the first count is `8`, the second `2`. Those two `object? Data` properties are why Task 2 writes its own command type. If the generated diff touches any other file, the generator itself moved: stop and ask.

- [ ] **Step 4: Build and measure**

```bash
dotnet build
dotnet test --no-build --filter "SpecPathTests"
```

Expected: the build succeeds. Of the three tests, `Every_operation_a_specification_declares_has_a_facade` fails with `"POST /cart/{}/carts/{}/execute"` in its `Actual:` list next to the five known availability gaps; the other two pass. This failure is the feature's acceptance test, and Task 3 turns it green.

- [ ] **Step 5: Commit**

```bash
git add specs/cart.yml specs/sync-manifest.json src/Viu.Emporix/Generated/Cart.cs
git commit -F - <<'EOF'
fix: sync the cart specification with upstream

Emporix added a command chain endpoint to the cart service on 2026-09-30.
The vendored specification gains one tag, one path and five schemas and
loses nothing. The regenerated types are the request, command, options,
response and result of a chain, and the two enums for its onError and
versioning query parameters. No facade wraps the path yet, which is what
SpecPathTests now reports.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: `CartCommand` — one factory per command type

**Files:**
- Create: `src/Viu.Emporix/CartCommand.cs`
- Modify: `src/Viu.Emporix/JsonContexts.cs`, the attribute list above `internal sealed partial class CartJsonContext : JsonSerializerContext;`
- Modify, by script: `src/Viu.Emporix/PublicAPI.Unshipped.txt`
- Test: `tests/Viu.Emporix.Tests/CartServiceTests.cs`

**Interfaces:**
- Consumes: Task 1's `ExecuteCommandType` and `ExecuteCommandOptions`.
- Produces:
  - `public sealed class Viu.Emporix.CartCommand` with `required ExecuteCommandType Type { get; init; }`, `JsonElement? Data { get; init; }`, `ExecuteCommandOptions? Options { get; init; }` and these static factories, each returning `CartCommand`:
    `AddCartItem(CartItemRequest item, int? resourceVersion = null)`,
    `UpdateCartItem(string itemId, UpdateCartItem changes, bool? partial = null, int? resourceVersion = null)`,
    `DeleteCartItem(string itemId)`, `DeleteCartItems()`,
    `GetCart(bool? expandCalculation = null, string? zipCode = null, string? countryCode = null)`,
    `AddCartItemsBatch(IEnumerable<CartItemRequest> items)`,
    `UpdateCartItemsBatch(IEnumerable<CartItemRequest> items, bool? partial = null)`,
    `UpdateCart(UpdateCart cart, int? resourceVersion = null)`,
    `ApplyCartDiscount(Discount discount, int? resourceVersion = null)`,
    `GetCartDiscounts()`, `DeleteCartDiscounts(IEnumerable<string>? codes = null)`,
    `DeleteCartDiscount(int discountIndex)`, `RefreshCart()`, `ValidateCart()`;
  - `internal sealed class Viu.Emporix.CartCommandChain` with `required IReadOnlyList<CartCommand> Commands { get; init; }`, serialised through `CartJsonContext.Default.CartCommandChain`;
  - test helpers in `CartServiceTests`: `static JsonElement Wire(CartCommand command)` and `static List<string> Names(JsonElement element)`.

- [ ] **Step 1: Write the failing tests**

Add `using System.Text.Json;` to `tests/Viu.Emporix.Tests/CartServiceTests.cs`, so the usings read:

```csharp
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viu.Emporix.CartModels;
```

Then add this region just before the closing brace of the class `CartServiceTests`:

```csharp
    // ---------- Command chains: the commands ----------

    /// <summary>One command as it goes over the wire, through the cart context.</summary>
    private static JsonElement Wire(CartCommand command)
    {
        string json = JsonSerializer.Serialize(
            new CartCommandChain { Commands = [command] },
            CartJsonContext.Default.CartCommandChain);

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("commands")[0].Clone();
    }

    private static List<string> Names(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];

    [Fact]
    public void Each_command_factory_names_its_command_type()
    {
        (CartCommand Command, string Type)[] cases =
        [
            (CartCommand.AddCartItem(new CartItemRequest()), "AddCartItem"),
            (CartCommand.UpdateCartItem("i1", new UpdateCartItem()), "UpdateCartItem"),
            (CartCommand.DeleteCartItem("i1"), "DeleteCartItem"),
            (CartCommand.DeleteCartItems(), "DeleteCartItems"),
            (CartCommand.GetCart(), "GetCart"),
            (CartCommand.AddCartItemsBatch([new CartItemRequest()]), "AddCartItemsBatch"),
            (CartCommand.UpdateCartItemsBatch([new CartItemRequest()]), "UpdateCartItemsBatch"),
            (CartCommand.UpdateCart(new UpdateCart()), "UpdateCart"),
            (CartCommand.ApplyCartDiscount(new Discount { Code = "SUMMER" }), "ApplyCartDiscount"),
            (CartCommand.GetCartDiscounts(), "GetCartDiscounts"),
            (CartCommand.DeleteCartDiscounts(), "DeleteCartDiscounts"),
            (CartCommand.DeleteCartDiscount(0), "DeleteCartDiscount"),
            (CartCommand.RefreshCart(), "RefreshCart"),
            (CartCommand.ValidateCart(), "ValidateCart"),
        ];

        foreach ((CartCommand command, string type) in cases)
        {
            Assert.Equal(type, Wire(command).GetProperty("type").GetString());
        }
    }

    [Fact]
    public void A_command_body_is_sent_as_the_rest_call_sends_it()
    {
        JsonElement add = Wire(CartCommand.AddCartItem(new CartItemRequest
        {
            ItemYrn = "urn:yaas:saasag:caasproduct:product:acme;p1",
            Quantity = 2,
        }));

        Assert.Equal(
            "urn:yaas:saasag:caasproduct:product:acme;p1",
            add.GetProperty("data").GetProperty("itemYrn").GetString());
        Assert.Equal(2, add.GetProperty("data").GetProperty("quantity").GetDouble());

        // A batch command sends an array, a single-item command an object.
        JsonElement batch = Wire(CartCommand.AddCartItemsBatch([new CartItemRequest(), new CartItemRequest()]));
        Assert.Equal(2, batch.GetProperty("data").GetArrayLength());

        JsonElement discount = Wire(CartCommand.ApplyCartDiscount(new Discount { Code = "SUMMER" }));
        Assert.Equal("SUMMER", discount.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public void A_command_sends_only_the_options_it_was_given()
    {
        // The generated options start with partial = false and
        // expandCalculation = true. Neither may travel unless asked for.
        Assert.Equal(["itemId"], Names(Wire(CartCommand.DeleteCartItem("i1")).GetProperty("options")));

        JsonElement update = Wire(CartCommand.UpdateCartItem(
            "i1",
            new UpdateCartItem { Quantity = 3 },
            partial: true,
            resourceVersion: 7)).GetProperty("options");

        Assert.Equal(["itemId", "partial", "resourceVersion"], Names(update));
        Assert.True(update.GetProperty("partial").GetBoolean());
        Assert.Equal(7, update.GetProperty("resourceVersion").GetInt32());
    }

    [Fact]
    public void A_command_without_options_or_body_sends_neither()
    {
        Assert.Equal(["type"], Names(Wire(CartCommand.GetCart())));
    }

    [Fact]
    public void Reading_the_cart_takes_zip_and_country_together()
    {
        JsonElement options = Wire(CartCommand.GetCart(
            expandCalculation: false,
            zipCode: "8001",
            countryCode: "CH")).GetProperty("options");

        Assert.Equal(["expandCalculation", "zipCode", "countryCode"], Names(options));
        Assert.False(options.GetProperty("expandCalculation").GetBoolean());

        Assert.Throws<ArgumentException>(() => CartCommand.GetCart(zipCode: "8001"));
        Assert.Throws<ArgumentException>(() => CartCommand.GetCart(countryCode: "CH"));
    }

    [Fact]
    public void Discounts_are_removed_by_code_by_index_or_all_at_once()
    {
        Assert.Equal(["type"], Names(Wire(CartCommand.DeleteCartDiscounts())));

        JsonElement codes = Wire(CartCommand.DeleteCartDiscounts(["A", "B"]))
            .GetProperty("options")
            .GetProperty("codes");
        Assert.Equal(["A", "B"], codes.EnumerateArray().Select(code => code.GetString()));

        // An empty filter would read as «remove every discount».
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartDiscounts([]));
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartDiscounts(["A", " "]));

        Assert.Equal(
            "2",
            Wire(CartCommand.DeleteCartDiscount(2)).GetProperty("options").GetProperty("discountIndex").GetString());
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.DeleteCartDiscount(-1));
    }

    [Fact]
    public void Command_arguments_are_checked_when_the_command_is_built()
    {
        Assert.Throws<ArgumentNullException>(() => CartCommand.AddCartItem(null!));
        Assert.Throws<ArgumentException>(() => CartCommand.DeleteCartItem(" "));
        Assert.Throws<ArgumentNullException>(() => CartCommand.UpdateCartItem("i1", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.AddCartItemsBatch([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartCommand.UpdateCartItemsBatch([]));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -m 3 "error CS"
```

Expected: errors such as `CS0103: The name 'CartCommand' does not exist in the current context` and `CS0246` for `CartCommandChain`.

- [ ] **Step 3: Write `CartCommand`**

Create `src/Viu.Emporix/CartCommand.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viu.Emporix.CartModels;

namespace Viu.Emporix;

/// <summary>
/// One operation of a cart command chain, run by <c>CartService.ExecuteAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written because the generated <see cref="ExecuteCommand"/> declares its
/// body as <c>object? Data</c>, which no source-generated serializer context can
/// write unless the runtime type inside happens to be registered — and a boxed
/// <see cref="JsonElement"/> is not. Here the body is a <see cref="JsonElement"/>,
/// which writes itself verbatim.
/// </para>
/// <para>
/// Build commands with the factories, one per command type and named after it.
/// They serialise each body with the type information the REST methods use, so
/// both paths send the same JSON, and they set every option explicitly: the
/// generated <see cref="ExecuteCommandOptions"/> starts with
/// <c>Partial = false</c> and <c>ExpandCalculation = true</c>, which would
/// otherwise travel with every command. A command type a later specification
/// adds is reachable through the initializer before it has a factory.
/// </para>
/// </remarks>
public sealed class CartCommand
{
    /// <summary>The operation to run.</summary>
    /// <remarks>
    /// Required, because the enum's default is its first member: a command built
    /// without a type would silently mean <see cref="ExecuteCommandType.AddCartItem"/>.
    /// </remarks>
    [JsonPropertyName("type")]
    public required ExecuteCommandType Type { get; init; }

    /// <summary>The request body of the equivalent REST operation, if it takes one.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>The remaining path and query parameters of the equivalent REST operation.</summary>
    /// <remarks>
    /// Set every property when you build one yourself: see the remarks on the
    /// class for the two defaults the generated type starts with.
    /// </remarks>
    [JsonPropertyName("options")]
    public ExecuteCommandOptions? Options { get; init; }

    /// <summary>Adds an item, as <see cref="CartService.AddItemAsync"/> does.</summary>
    /// <param name="item">The item. Its product reference must be a YRN — see <see cref="ProductYrn"/>.</param>
    /// <param name="resourceVersion">
    /// The cart version to check the write against: needed on every
    /// participating write under <see cref="Versioning.Explicit"/>, and on the
    /// first one under <see cref="Versioning.Follow"/>.
    /// </param>
    public static CartCommand AddCartItem(CartItemRequest item, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new CartCommand
        {
            Type = ExecuteCommandType.AddCartItem,
            Data = JsonSerializer.SerializeToElement(item, CartJsonContext.Default.CartItemRequest),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Changes an item, as <see cref="CartService.UpdateItemAsync"/> does.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="changes">The new values.</param>
    /// <param name="partial">
    /// <see langword="true"/> to change only the fields <paramref name="changes"/>
    /// sets. Emporix's default, <see langword="false"/>, replaces the whole item.
    /// </param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand UpdateCartItem(
        string itemId,
        CartModels.UpdateCartItem changes,
        bool? partial = null,
        int? resourceVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(changes);

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCartItem,
            Data = JsonSerializer.SerializeToElement(changes, CartJsonContext.Default.UpdateCartItem),
            Options = OptionsOrNull(itemId: itemId, partial: partial, resourceVersion: resourceVersion),
        };
    }

    /// <summary>Removes an item, as <see cref="CartService.RemoveItemAsync"/> does.</summary>
    /// <param name="itemId">The item id.</param>
    public static CartCommand DeleteCartItem(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartItem,
            Options = OptionsOrNull(itemId: itemId),
        };
    }

    /// <summary>Removes every item, as <see cref="CartService.ClearAsync"/> does.</summary>
    public static CartCommand DeleteCartItems()
        => new() { Type = ExecuteCommandType.DeleteCartItems };

    /// <summary>Reads the cart, as <see cref="CartService.GetAsync"/> does.</summary>
    /// <param name="expandCalculation">
    /// <see langword="false"/> for the cart without its calculation. Emporix
    /// calculates it by default.
    /// </param>
    /// <param name="zipCode">The shipping zip code to calculate with; give the country with it.</param>
    /// <param name="countryCode">The two-letter shipping country to calculate with; give the zip code with it.</param>
    /// <remarks>
    /// A <c>GetCart</c> followed by another write returns a cart that is already
    /// stale: put it last to read what the chain left behind.
    /// </remarks>
    /// <exception cref="ArgumentException">Only one of <paramref name="zipCode"/> and <paramref name="countryCode"/> is given.</exception>
    public static CartCommand GetCart(
        bool? expandCalculation = null,
        string? zipCode = null,
        string? countryCode = null)
    {
        if ((zipCode is null) != (countryCode is null))
        {
            throw new ArgumentException(
                "Give the zip code and the country code together, or neither.",
                zipCode is null ? nameof(zipCode) : nameof(countryCode));
        }

        return new CartCommand
        {
            Type = ExecuteCommandType.GetCart,
            Options = OptionsOrNull(
                expandCalculation: expandCalculation,
                zipCode: zipCode,
                countryCode: countryCode),
        };
    }

    /// <summary>Adds several items, as <see cref="CartService.AddItemsAsync"/> does.</summary>
    /// <param name="items">The items; at least one.</param>
    public static CartCommand AddCartItemsBatch(IEnumerable<CartItemRequest> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchRequest batch = [.. items];
        ArgumentOutOfRangeException.ThrowIfZero(batch.Count, nameof(items));

        return new CartCommand
        {
            Type = ExecuteCommandType.AddCartItemsBatch,
            Data = JsonSerializer.SerializeToElement(batch, CartJsonContext.Default.CartItemsBatchRequest),
        };
    }

    /// <summary>Changes several items, as <see cref="CartService.UpdateItemsAsync"/> does.</summary>
    /// <param name="items">The items in their new state; at least one.</param>
    /// <param name="partial"><see langword="true"/> to change only the fields each entry sets.</param>
    public static CartCommand UpdateCartItemsBatch(IEnumerable<CartItemRequest> items, bool? partial = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        CartItemsBatchUpdateRequest batch = [.. items];
        ArgumentOutOfRangeException.ThrowIfZero(batch.Count, nameof(items));

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCartItemsBatch,
            Data = JsonSerializer.SerializeToElement(batch, CartJsonContext.Default.CartItemsBatchUpdateRequest),
            Options = OptionsOrNull(partial: partial),
        };
    }

    /// <summary>Changes the cart itself, as <see cref="CartService.UpdateAsync"/> does.</summary>
    /// <param name="cart">The new values.</param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand UpdateCart(CartModels.UpdateCart cart, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(cart);

        return new CartCommand
        {
            Type = ExecuteCommandType.UpdateCart,
            Data = JsonSerializer.SerializeToElement(cart, CartJsonContext.Default.UpdateCart),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Applies a discount: a coupon code, or an external discount.</summary>
    /// <param name="discount">
    /// The discount. A coupon is <c>new Discount { Code = "…" }</c>, which is what
    /// <see cref="CartService.ApplyCouponAsync"/> sends.
    /// </param>
    /// <param name="resourceVersion">The cart version to check the write against — see <see cref="AddCartItem"/>.</param>
    public static CartCommand ApplyCartDiscount(Discount discount, int? resourceVersion = null)
    {
        ArgumentNullException.ThrowIfNull(discount);

        return new CartCommand
        {
            Type = ExecuteCommandType.ApplyCartDiscount,
            Data = JsonSerializer.SerializeToElement(discount, CartJsonContext.Default.Discount),
            Options = OptionsOrNull(resourceVersion: resourceVersion),
        };
    }

    /// <summary>Lists the cart's discounts, as <see cref="CartService.ListDiscountsAsync"/> does.</summary>
    public static CartCommand GetCartDiscounts()
        => new() { Type = ExecuteCommandType.GetCartDiscounts };

    /// <summary>
    /// Removes the discounts with these codes, or every discount, as
    /// <see cref="CartService.RemoveAllDiscountsAsync"/> does without codes.
    /// </summary>
    /// <param name="codes">The codes to remove; <see langword="null"/> removes every discount.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="codes"/> is empty or holds a blank code. Emporix reads a
    /// missing filter as «remove every discount», and a filter that came out
    /// empty by accident must not mean that.
    /// </exception>
    public static CartCommand DeleteCartDiscounts(IEnumerable<string>? codes = null)
    {
        List<string>? filter = codes is null ? null : [.. codes];

        if (filter is { Count: 0 } || filter?.Exists(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException(
                "Give at least one code, none of them blank, or null to remove every discount.",
                nameof(codes));
        }

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartDiscounts,
            Options = OptionsOrNull(codes: filter),
        };
    }

    /// <summary>Removes one discount, as <see cref="CartService.RemoveDiscountAsync"/> does.</summary>
    /// <param name="discountIndex">The index Emporix reports on the discount; read it before removing.</param>
    public static CartCommand DeleteCartDiscount(int discountIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discountIndex);

        return new CartCommand
        {
            Type = ExecuteCommandType.DeleteCartDiscount,

            // The specification types this option as a string; the REST path
            // segment it stands for is a number.
            Options = OptionsOrNull(discountIndex: discountIndex.ToString(CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>Recalculates the cart, as <see cref="CartService.RefreshAsync"/> does.</summary>
    public static CartCommand RefreshCart()
        => new() { Type = ExecuteCommandType.RefreshCart };

    /// <summary>Validates the cart, as <see cref="CartService.ValidateAsync"/> does.</summary>
    public static CartCommand ValidateCart()
        => new() { Type = ExecuteCommandType.ValidateCart };

    /// <summary>
    /// The options of a command, or <see langword="null"/> when there is nothing
    /// to send.
    /// </summary>
    /// <remarks>
    /// Sets all eight properties, the unused ones to <see langword="null"/>: the
    /// generated type starts with <c>Partial = false</c> and
    /// <c>ExpandCalculation = true</c>, and <c>WhenWritingNull</c> leaves out
    /// only what is null.
    /// </remarks>
    private static ExecuteCommandOptions? OptionsOrNull(
        string? itemId = null,
        bool? partial = null,
        bool? expandCalculation = null,
        string? zipCode = null,
        string? countryCode = null,
        int? resourceVersion = null,
        ICollection<string>? codes = null,
        string? discountIndex = null)
    {
        ExecuteCommandOptions options = new()
        {
            ItemId = itemId,
            Partial = partial,
            ExpandCalculation = expandCalculation,
            ZipCode = zipCode,
            CountryCode = countryCode,
            ResourceVersion = resourceVersion,
            Codes = codes,
            DiscountIndex = discountIndex,
        };

        return options is
        {
            ItemId: null,
            Partial: null,
            ExpandCalculation: null,
            ZipCode: null,
            CountryCode: null,
            ResourceVersion: null,
            Codes: null,
            DiscountIndex: null
        }
            ? null
            : options;
    }
}

/// <summary>The body of an execute request: the commands, and nothing else.</summary>
/// <remarks>
/// <c>onError</c> and <c>versioning</c> are query parameters, so they have no
/// place here.
/// </remarks>
internal sealed class CartCommandChain
{
    /// <summary>The commands, in the order they run.</summary>
    [JsonPropertyName("commands")]
    public required IReadOnlyList<CartCommand> Commands { get; init; }
}
```

The parameter types `CartModels.UpdateCartItem` and `CartModels.UpdateCart` are qualified because the factories carry the same names.

- [ ] **Step 4: Register the body type**

In `src/Viu.Emporix/JsonContexts.cs`, add one line directly above `internal sealed partial class CartJsonContext : JsonSerializerContext;`:

```csharp
[JsonSerializable(typeof(Viu.Emporix.CartCommandChain))]
```

`CartCommand`, `ExecuteCommandOptions` and `ExecuteCommandType` come in through it.

- [ ] **Step 5: Record the public surface and build**

```bash
dotnet build 2>&1 | grep -E "error [A-Z]+[0-9]+" | grep -v "RS0016" | head
./scripts/update-public-api.sh
dotnet build
```

Expected: the first command prints nothing, because the only errors are RS0016 for the new public symbols. The script appends `Viu.Emporix.CartCommand`, its constructor, the three properties and the fourteen factories to `PublicAPI.Unshipped.txt`. The final build succeeds. If it reports `CA1024` on `GetCartDiscounts`, add `using System.Diagnostics.CodeAnalysis;` and, directly above that factory, `[SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "A factory named after its Emporix command; every call builds a new command.")]`, then build again.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --no-build --filter "FullyQualifiedName~CartServiceTests"
```

Expected: every `CartServiceTests` test passes, the seven new ones included. `SpecPathTests` still fails and is outside this filter.

- [ ] **Step 7: Commit**

```bash
git add src/Viu.Emporix/CartCommand.cs src/Viu.Emporix/JsonContexts.cs src/Viu.Emporix/PublicAPI.Unshipped.txt tests/Viu.Emporix.Tests/CartServiceTests.cs
git commit -F - <<'EOF'
feat: build cart commands with one factory per command type

A command chain carries the request body of each cart operation, and the
generated command declares that body as an object, which no source-generated
serializer context can write. CartCommand holds it as a JsonElement instead.
Its fourteen factories are named after the command types, serialise each body
with the type information the REST methods use, and set every option
explicitly, so the generated defaults for partial and expandCalculation never
travel unasked. A command type a later specification adds is reachable
through the initializer until it has a factory.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: `CartService.ExecuteAsync`

**Files:**
- Modify: `src/Viu.Emporix/CartService.cs` — two usings, and the new method directly above the comment block that begins `/// <summary>` / `/// Replaces the entry of one address type and keeps every other one.`
- Modify: `src/Viu.Emporix/JsonContexts.cs`
- Modify, by script: `src/Viu.Emporix/PublicAPI.Unshipped.txt`
- Test: `tests/Viu.Emporix.Tests/CartServiceTests.cs`

**Interfaces:**
- Consumes: Task 1's `ExecuteResponse`, `ExecuteCommandResult`, `OnError`, `Versioning`; Task 2's `CartCommand`, `CartCommandChain`, `Names(JsonElement)`.
- Produces: `public async Task<ExecuteResponse> CartService.ExecuteAsync(string cartId, IEnumerable<CartCommand> commands, AuthContext auth, OnError? onError = null, Versioning? versioning = null, CancellationToken cancellationToken = default)`; `CartJsonContext.Default.ExecuteResponse`; the test constants `NoResults` and `FailedSecond`.

- [ ] **Step 1: Write the failing tests**

Add this region to `CartServiceTests`, below the region from Task 2:

```csharp
    // ---------- Command chains: the request ----------

    private const string NoResults = """{"results":[]}""";

    private const string FailedSecond = """
        {"results":[
          {"index":0,"type":"GetCart","code":200,"status":"OK","data":{"id":"c1"}},
          {"index":1,"type":"DeleteCartItem","code":404,"status":"Not Found",
           "data":{"code":404,"status":"Not Found","message":"Cart item not found"}}
        ]}
        """;

    [Fact]
    public async Task A_command_chain_posts_to_the_execute_path_of_the_escaped_cart()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c 1", [CartCommand.GetCart()], Shopper);

        Assert.Equal(HttpMethod.Post, handler.RequestMethods[0]);
        Assert.Equal("/cart/acme/carts/c%201/execute", Uri(handler));
    }

    [Fact]
    public async Task A_command_chain_sends_its_commands_and_nothing_else_in_the_body()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync(
            "c1",
            [CartCommand.DeleteCartItem("i1"), CartCommand.GetCart()],
            Shopper,
            OnError.Resume,
            Versioning.Follow);

        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(["commands"], Names(body.RootElement));
        Assert.Equal(2, body.RootElement.GetProperty("commands").GetArrayLength());
    }

    [Theory]
    [InlineData(OnError.Fail, Versioning.Skip, "?onError=fail&versioning=skip")]
    [InlineData(OnError.Resume, Versioning.Explicit, "?onError=resume&versioning=explicit")]
    [InlineData(OnError.Resume, Versioning.Follow, "?onError=resume&versioning=follow")]
    public async Task Error_mode_and_versioning_reach_the_query_as_the_specification_spells_them(
        OnError onError,
        Versioning versioning,
        string query)
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper, onError, versioning);

        Assert.Equal("/cart/acme/carts/c1/execute" + query, Uri(handler));
    }

    [Fact]
    public async Task Without_an_error_mode_or_versioning_the_query_stays_empty()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper);

        Assert.Equal("/cart/acme/carts/c1/execute", Uri(handler));
    }

    [Fact]
    public async Task A_command_chain_is_never_retried()
    {
        // A replay would apply every write in the chain a second time.
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await carts.ExecuteAsync("c1", [CartCommand.RefreshCart()], Shopper);

        Assert.False(handler.LastRequest!.Options.TryGetValue(EmporixRequestOptions.Idempotent, out _));
    }

    [Fact]
    public async Task A_command_chain_refuses_a_service_token_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<EmporixConfigurationException>(async () =>
            await carts.ExecuteAsync("c1", [CartCommand.GetCart()], AuthContext.Service()));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_empty_or_broken_chain_is_refused_before_any_request()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, NoResults);
        CartService carts = Create(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await carts.ExecuteAsync("c1", [], Shopper));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await carts.ExecuteAsync("c1", null!, Shopper));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await carts.ExecuteAsync("c1", [null!], Shopper));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await carts.ExecuteAsync(" ", [CartCommand.GetCart()], Shopper));

        Assert.Equal(0, handler.CallCount);
    }

    // ---------- Command chains: failures ----------

    [Theory]
    [InlineData(null)]
    [InlineData(OnError.Fail)]
    public async Task Unless_resumed_the_first_failed_command_is_thrown_as_its_rest_error(OnError? onError)
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, FailedSecond);
        CartService carts = Create(handler);

        EmporixNotFoundException exception = await Assert.ThrowsAsync<EmporixNotFoundException>(async () =>
            await carts.ExecuteAsync(
                "c1",
                [CartCommand.GetCart(), CartCommand.DeleteCartItem("gone")],
                Shopper,
                onError));

        Assert.Contains("command 1 (DeleteCartItem)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Cart item not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resumed_every_result_comes_back_and_nothing_is_thrown()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, FailedSecond);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync(
            "c1",
            [CartCommand.GetCart(), CartCommand.DeleteCartItem("gone")],
            Shopper,
            OnError.Resume);

        Assert.Equal([200, 404], response.Results.Select(result => result.Code));
    }

    [Fact]
    public async Task A_chain_answered_without_a_body_reads_as_no_results()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, string.Empty);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync("c1", [CartCommand.GetCart()], Shopper);

        Assert.Empty(response.Results);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -m 3 "error CS"
```

Expected: `CS1061: 'CartService' does not contain a definition for 'ExecuteAsync'`.

- [ ] **Step 3: Write `ExecuteAsync`**

In `src/Viu.Emporix/CartService.cs`, change the usings at the top to:

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Viu.Emporix.CartModels;
```

Then insert this method directly above the comment block that starts `/// <summary>` and continues `/// Replaces the entry of one address type and keeps every other one.`:

```csharp
    /// <summary>Runs several cart operations, in order, in one request.</summary>
    /// <param name="cartId">The cart id; Emporix copies it onto every command.</param>
    /// <param name="commands">
    /// One to ten commands, built with the <see cref="CartCommand"/> factories.
    /// Emporix refuses a longer chain as a whole.
    /// </param>
    /// <param name="auth">A customer or anonymous context. Required.</param>
    /// <param name="onError">
    /// <see cref="OnError.Resume"/> to run every command whatever happened to the
    /// ones before. Emporix's default, <see cref="OnError.Fail"/>, stops after the
    /// first command that fails.
    /// </param>
    /// <param name="versioning">
    /// How writes are checked against the cart version. Emporix's default,
    /// <see cref="Versioning.Skip"/>, checks nothing. <see cref="Versioning.Explicit"/>
    /// needs a <c>resourceVersion</c> on every participating write and refuses the
    /// whole chain without one; <see cref="Versioning.Follow"/> needs it on the first.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// One result per command that ran, in order. Read a body with the reader that
    /// matches its command, such as <c>ReadCart</c>.
    /// </returns>
    /// <exception cref="EmporixApiException">
    /// Unless <paramref name="onError"/> is <see cref="OnError.Resume"/>: the first
    /// command that failed, as the exception its REST call would have raised, with
    /// the command's index and type in the message.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A failed command does not undo the ones before it: they have been applied.
    /// The exception names the failed command and nothing else; a caller who needs
    /// every result passes <see cref="OnError.Resume"/> and reads <c>Code</c> on each.
    /// </para>
    /// <para>
    /// It saves round trips, not work. The request takes about as long as its
    /// commands together, so the client's timeout has to cover the whole chain,
    /// and a timeout does not mean nothing was written. For the same reason it is
    /// never retried: a replay would apply the writes twice.
    /// </para>
    /// <para>
    /// Emporix also accepts a service token with <c>cart.cart_manage</c>; this
    /// method refuses one, like every shopper operation here. A command carrying an
    /// external price, product, fee or discount needs
    /// <c>cart.cart_manage_external_prices</c>.
    /// </para>
    /// </remarks>
    public async Task<ExecuteResponse> ExecuteAsync(
        string cartId,
        IEnumerable<CartCommand> commands,
        AuthContext auth,
        OnError? onError = null,
        Versioning? versioning = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartId);
        ArgumentNullException.ThrowIfNull(commands);

        List<CartCommand> chain = [.. commands];
        ArgumentOutOfRangeException.ThrowIfZero(chain.Count, nameof(commands));

        if (chain.Exists(command => command is null))
        {
            throw new ArgumentException("A command chain cannot hold null.", nameof(commands));
        }

        // Neither enum converter reaches a query string: ToString would send
        // «Resume» where the specification wants «resume».
        List<KeyValuePair<string, string?>> query = [];

        if (onError is { } mode)
        {
            query.Add(new("onError", mode switch
            {
                OnError.Fail => "fail",
                OnError.Resume => "resume",
                _ => throw new ArgumentOutOfRangeException(nameof(onError), mode, null),
            }));
        }

        if (versioning is { } check)
        {
            query.Add(new("versioning", check switch
            {
                Versioning.Skip => "skip",
                Versioning.Explicit => "explicit",
                Versioning.Follow => "follow",
                _ => throw new ArgumentOutOfRangeException(nameof(versioning), check, null),
            }));
        }

        ExecuteResponse response = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/execute",
                Auth = RequireCartAuth(auth),
                Query = query,
                Content = EmporixJsonContent.Create(
                    new CartCommandChain { Commands = chain },
                    CartJsonContext.Default.CartCommandChain),
            },
            CartJsonContext.Default.ExecuteResponse,
            cancellationToken).ConfigureAwait(false) ?? new ExecuteResponse();

        if (onError is not OnError.Resume
            && response.Results.FirstOrDefault(result => result.Code is < 200 or > 299) is { } failed)
        {
            // The same exception, and the same message parsing, as the REST call:
            // the failed command's data is that call's error body.
            throw EmporixErrorParser.CreateException(
                (HttpStatusCode)failed.Code,
                $"POST {BasePath}/{Uri.EscapeDataString(cartId)}/execute, command {failed.Index} ({failed.Type})",
                failed.Data is JsonElement body ? body.GetRawText() : null);
        }

        return response;
    }
```

- [ ] **Step 4: Register the response type**

In `src/Viu.Emporix/JsonContexts.cs`, add directly above `internal sealed partial class CartJsonContext : JsonSerializerContext;`:

```csharp
[JsonSerializable(typeof(Viu.Emporix.CartModels.ExecuteResponse))]
```

- [ ] **Step 5: Record the public surface and build**

```bash
dotnet build 2>&1 | grep -E "error [A-Z]+[0-9]+" | grep -v "RS0016" | head
./scripts/update-public-api.sh
dotnet build
```

Expected: nothing from the first command; the script appends the `ExecuteAsync` entry; the build succeeds.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --no-build --filter "FullyQualifiedName~CartServiceTests|FullyQualifiedName~SpecPathTests"
```

Expected: all pass. `SpecPathTests` passes three of three: `Every_operation_a_specification_declares_has_a_facade`, which failed since Task 1, now finds the new path.

- [ ] **Step 7: Commit**

```bash
git add src/Viu.Emporix/CartService.cs src/Viu.Emporix/JsonContexts.cs src/Viu.Emporix/PublicAPI.Unshipped.txt tests/Viu.Emporix.Tests/CartServiceTests.cs
git commit -F - <<'EOF'
feat: run a chain of cart commands in one request

CartService.ExecuteAsync wraps POST /cart/{tenant}/carts/{cartId}/execute,
which Emporix added on 2026-09-30: up to ten cart operations on one cart, in
order, answered with one result per command. The usual chain adds an item and
reads the calculated cart in the same request.

The error mode and the versioning go into the query string as the
specification spells them, and the body carries the commands only. Like every
shopper operation of the cart service, it takes a customer or anonymous
context and refuses a service token. It is never retried, since a replay would
apply the writes twice. Unless the caller resumes, the first failed command is
thrown as the exception its REST call would have raised, with the index and
type of the command in the message.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Readers on `ExecuteCommandResult`

**Files:**
- Create: `src/Viu.Emporix/CartCommandResult.cs`
- Modify: `src/Viu.Emporix/JsonContexts.cs`
- Modify, by script: `src/Viu.Emporix/PublicAPI.Unshipped.txt`
- Test: `tests/Viu.Emporix.Tests/CartServiceTests.cs`

**Interfaces:**
- Consumes: Task 1's `ExecuteCommandResult` members `Index`, `Type`, `Code`, `Data`; Task 3's `ExecuteAsync`.
- Produces, on `Viu.Emporix.CartModels.ExecuteCommandResult`: `Cart? ReadCart()`, `CreatedCartItem? ReadCreatedItem()`, `IReadOnlyList<SingleBatchResponse> ReadAddedItems()`, `IReadOnlyList<UpdateCartItemsBatchEntryResponse> ReadUpdatedItems()`, `AppliedDiscount? ReadAppliedDiscount()`, `IReadOnlyList<DiscountResponse> ReadDiscounts()`, `CartValidationResult? ReadValidation()`, each throwing `InvalidOperationException` on another command's result; `CartJsonContext.Default.CreatedCartItem` and `.AppliedDiscount`; the test helper `static ExecuteCommandResult Result(string type, int code, string data)`.

- [ ] **Step 1: Write the failing tests**

Add this region to `CartServiceTests`, below the region from Task 3:

```csharp
    // ---------- Command chains: reading results ----------

    private static ExecuteCommandResult Result(string type, int code, string data)
    {
        using JsonDocument document = JsonDocument.Parse(data);

        return new ExecuteCommandResult
        {
            Index = 0,
            Type = type,
            Code = code,
            Status = "OK",
            Data = document.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task A_chain_ending_in_GetCart_reads_back_the_created_item_and_the_cart()
    {
        // Through the wire on purpose: Data is declared as object, and this is
        // what shows it arrives as a JsonElement the readers can use.
        StubHttpMessageHandler handler = new(HttpStatusCode.MultiStatus, """
            {"results":[
              {"index":0,"type":"AddCartItem","code":201,"status":"Created",
               "data":{"itemId":"i9","yrn":"urn:yaas:saasag:caascart:cartItem:acme;i9"}},
              {"index":1,"type":"GetCart","code":200,"status":"OK",
               "data":{"id":"c1","items":[{"id":"i9"}]}}
            ]}
            """);
        CartService carts = Create(handler);

        ExecuteResponse response = await carts.ExecuteAsync(
            "c1",
            [CartCommand.AddCartItem(new CartItemRequest()), CartCommand.GetCart()],
            Shopper);

        List<ExecuteCommandResult> results = [.. response.Results];
        Assert.Equal("i9", results[0].ReadCreatedItem()?.ItemId);

        Cart? cart = results[1].ReadCart();
        Assert.Equal("c1", cart?.Id);
        Assert.Equal("i9", Assert.Single(cart!.Items!).Id);
    }

    [Fact]
    public void Each_reader_reads_the_body_of_its_command()
    {
        Assert.Equal(
            201,
            Assert.Single(Result("AddCartItemsBatch", 200, """[{"index":0,"status":201}]""").ReadAddedItems()).Status);
        Assert.Equal(
            200,
            Assert.Single(Result("UpdateCartItemsBatch", 207, """[{"index":0,"code":200}]""").ReadUpdatedItems()).Code);
        Assert.Equal(
            "d1",
            Result("ApplyCartDiscount", 201, """{"yrn":"y","discountId":"d1","discountIndex":0}""")
                .ReadAppliedDiscount()?.DiscountId);
        Assert.Equal(
            "SUMMER",
            Assert.Single(Result("GetCartDiscounts", 200, """[{"code":"SUMMER"}]""").ReadDiscounts()).Code);
        Assert.True(Result("ValidateCart", 200, """{"isValid":true}""").ReadValidation()?.IsValid);
    }

    [Fact]
    public void A_failed_command_reads_as_nothing()
    {
        // Its data is the REST error body, not the command's type.
        const string Error = """{"code":404,"status":"Not Found","message":"gone"}""";

        Assert.Null(Result("GetCart", 404, Error).ReadCart());
        Assert.Empty(Result("GetCartDiscounts", 404, Error).ReadDiscounts());
    }

    [Fact]
    public void A_reader_refuses_the_result_of_another_command()
    {
        // Read as a cart, a validation result would come back as a mostly empty
        // Cart, and nothing would say so.
        ExecuteCommandResult validation = Result("ValidateCart", 200, """{"isValid":true}""");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => validation.ReadCart());

        Assert.Contains("ValidateCart", exception.Message, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build tests/Viu.Emporix.Tests 2>&1 | grep -m 3 "error CS"
```

Expected: `CS1061: 'ExecuteCommandResult' does not contain a definition for 'ReadCreatedItem'`, and likewise for the other readers.

- [ ] **Step 3: Write the readers**

Create `src/Viu.Emporix/CartCommandResult.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Viu.Emporix.CartModels;

/// <remarks>
/// <para>
/// <see cref="Data"/> carries the REST response body of whichever command ran,
/// so what it holds depends on <see cref="Type"/>. Each reader belongs to one
/// command type and refuses a result of another: reading a validation result as
/// a cart would fill a mostly empty <see cref="Cart"/> and say nothing.
/// </para>
/// <para>
/// A command that failed reads as <see langword="null"/>, or as an empty list:
/// its data is the REST error body, not the command's type. Check
/// <see cref="Code"/> to tell a failure from a command that returned nothing.
/// </para>
/// </remarks>
public partial class ExecuteCommandResult
{
    /// <summary>The cart a <c>GetCart</c> command read.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public Cart? ReadCart()
        => Read(nameof(ExecuteCommandType.GetCart), CartJsonContext.Default.Cart);

    /// <summary>The id and YRN of the item an <c>AddCartItem</c> command created.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public CreatedCartItem? ReadCreatedItem()
        => Read(nameof(ExecuteCommandType.AddCartItem), CartJsonContext.Default.CreatedCartItem);

    /// <summary>The outcome per item of an <c>AddCartItemsBatch</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<SingleBatchResponse> ReadAddedItems()
        => Read(nameof(ExecuteCommandType.AddCartItemsBatch), CartJsonContext.Default.BatchResponse) ?? [];

    /// <summary>The outcome per item of an <c>UpdateCartItemsBatch</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<UpdateCartItemsBatchEntryResponse> ReadUpdatedItems()
        => Read(
            nameof(ExecuteCommandType.UpdateCartItemsBatch),
            CartJsonContext.Default.CartItemsBatchUpdateResponse) ?? [];

    /// <summary>The discount an <c>ApplyCartDiscount</c> command applied.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public AppliedDiscount? ReadAppliedDiscount()
        => Read(nameof(ExecuteCommandType.ApplyCartDiscount), CartJsonContext.Default.AppliedDiscount);

    /// <summary>The discounts a <c>GetCartDiscounts</c> command listed.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public IReadOnlyList<DiscountResponse> ReadDiscounts()
        => Read(nameof(ExecuteCommandType.GetCartDiscounts), CartJsonContext.Default.ListDiscountResponse) ?? [];

    /// <summary>The verdict of a <c>ValidateCart</c> command.</summary>
    /// <exception cref="InvalidOperationException">The result belongs to another command.</exception>
    public CartValidationResult? ReadValidation()
        => Read(nameof(ExecuteCommandType.ValidateCart), CartJsonContext.Default.CartValidationResult);

    private T? Read<T>(string command, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!string.Equals(Type, command, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Result {Index} belongs to a {Type} command; this reader is for {command}.");
        }

        return Code is >= 200 and < 300
            && Data is JsonElement { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } body
            ? body.Deserialize(typeInfo)
            : null;
    }
}
```

- [ ] **Step 4: Register the two response types the context lacks**

In `src/Viu.Emporix/JsonContexts.cs`, add directly above `internal sealed partial class CartJsonContext : JsonSerializerContext;`:

```csharp
[JsonSerializable(typeof(Viu.Emporix.CartModels.CreatedCartItem))]
[JsonSerializable(typeof(Viu.Emporix.CartModels.AppliedDiscount))]
```

`Cart`, `BatchResponse`, `CartItemsBatchUpdateResponse`, `List<DiscountResponse>` and `CartValidationResult` are registered already for the REST methods.

- [ ] **Step 5: Record the public surface and build**

```bash
dotnet build 2>&1 | grep -E "error [A-Z]+[0-9]+" | grep -v "RS0016" | head
./scripts/update-public-api.sh
dotnet build
```

Expected: nothing from the first command. The script appends the seven readers and also the type `Viu.Emporix.CartModels.ExecuteCommandResult` with its constructor: the type now has a declaration outside `Generated/`, as `DynamicVariantProductWithId` has in `PublicAPI.Shipped.txt`. The build succeeds.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --no-build --filter "FullyQualifiedName~CartServiceTests|FullyQualifiedName~SpecPathTests"
```

Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Viu.Emporix/CartCommandResult.cs src/Viu.Emporix/JsonContexts.cs src/Viu.Emporix/PublicAPI.Unshipped.txt tests/Viu.Emporix.Tests/CartServiceTests.cs
git commit -F - <<'EOF'
feat: read each cart command's result as its type

A result's data is the REST response body of whichever command ran, so the
generated result can only offer it untyped. Seven readers on a partial of
ExecuteCommandResult deserialise it into the type the command returns: the
cart, the created item, both batch outcomes, the applied discount, the
discount list and the validation verdict. A reader refuses the result of
another command rather than fill a mostly empty object, and a failed command
reads as nothing, since its data is the error body.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Documentation and the full check

**Files:**
- Modify: `README.md` — the `client.Carts` row and the storefront sample
- Modify: `CLAUDE.md:10`

**Interfaces:**
- Consumes: everything from Tasks 2 to 4.
- Produces: no code.

- [ ] **Step 1: Update the service row**

In `README.md`, replace

```markdown
| `client.Carts` | carts, items, coupons, validation |
```

with

```markdown
| `client.Carts` | carts, items, coupons, validation, command chains |
```

- [ ] **Step 2: Add the chain to the storefront sample**

In `README.md`, directly below the line `await client.Carts.AddItemAsync(cart!.Id, item, shopper);`, add:

```csharp

// Several cart operations in one request: the write and the calculated cart.
var chain = await client.Carts.ExecuteAsync(
    cart.Id,
    [CartCommand.AddCartItem(item), CartCommand.GetCart()],
    shopper);
var calculated = chain.Results.Last().ReadCart();
```

- [ ] **Step 3: Update the call count**

In `CLAUDE.md`, line 10, replace `over 676 public calls.` with `over 677 public calls.`

- [ ] **Step 4: Run the full check**

```bash
dotnet build
dotnet test --no-build
export LIBRARY_PATH=/opt/homebrew/lib
dotnet publish samples/Viu.Emporix.Sample --configuration Release
```

Expected: the build succeeds; every test passes, 641 before this branch plus the new ones — note the count for the pull request; the AOT publish succeeds without a trim or AOT warning.

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md
git commit -F - <<'EOF'
docs: show the cart command chain in the readme

The carts row names command chains, the storefront sample adds an item and
reads the calculated cart in one request, and the call count in CLAUDE.md
includes ExecuteAsync.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

- [ ] **Step 6: Break each rule once on purpose**

For every row: make the change, run `dotnet test --filter "FullyQualifiedName~CartServiceTests"`, see the named test fail, then restore with `git checkout -- <file>`.

| # | File | Change | Must fail |
|---|---|---|---|
| 1 | `src/Viu.Emporix/CartCommand.cs` | in `OptionsOrNull`, delete the line `Partial = partial,` | `A_command_sends_only_the_options_it_was_given`, `A_command_without_options_or_body_sends_neither` |
| 2 | `src/Viu.Emporix/CartService.cs` | in `ExecuteAsync`, change `OnError.Resume => "resume",` to `OnError.Resume => "Resume",` | `Error_mode_and_versioning_reach_the_query_as_the_specification_spells_them`, two of its three cases |
| 3 | `src/Viu.Emporix/CartService.cs` | in `ExecuteAsync`, on the line directly below `Path = $"{BasePath}/{Uri.EscapeDataString(cartId)}/execute",`, change `Auth = RequireCartAuth(auth),` to `Auth = auth,` | `A_command_chain_refuses_a_service_token_before_any_request` |
| 4 | `src/Viu.Emporix/CartService.cs` | change `if (onError is not OnError.Resume` to `if (onError is OnError.Fail` | `Unless_resumed_the_first_failed_command_is_thrown_as_its_rest_error`, its `null` case |
| 5 | `src/Viu.Emporix/CartCommandResult.cs` | delete the `if (!string.Equals(Type, command, StringComparison.Ordinal))` block with its `throw` | `A_reader_refuses_the_result_of_another_command` |

Then:

```bash
git status --porcelain
dotnet test --filter "FullyQualifiedName~CartServiceTests"
```

Expected: no output from `git status`, and every test passes again. Note the five results for the pull request.

---

### Task 6: Walk a chain in the smoke test

**Files:**
- Modify: `samples/Viu.Emporix.SmokeTest/Program.cs` — three steps after «add an item to the cart»

**Interfaces:**
- Consumes: `client.Carts.ExecuteAsync`, the `CartCommand` factories, `ReadCreatedItem`, `ReadCart`; in `Program.cs` the existing `cartId`, `pricedProduct`, `shopper`, `configuration` and `runner`.
- Produces: three smoke-test steps named `add an item and read the cart in one chain`, `change that item and read the cart in one chain` and `a failed command under fail and under resume`.

- [ ] **Step 1: Add the steps**

In `samples/Viu.Emporix.SmokeTest/Program.cs`, find the end of the step «add an item to the cart»:

```csharp
    return item is null
        ? Step.Failed("the item did not come back")
        : Step.Ok("added");
});
```

Directly below it, before `await runner.RunAsync("read the current cart", …`, insert:

```csharp

// A command chain on the same throwaway cart. The last step of this pass
// deletes the cart, so nothing written here outlives the run.
string? chainedItemId = await runner.RunAsync("add an item and read the cart in one chain", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("no cart");
    }

    if (pricedProduct is not { } match)
    {
        return Step.Skipped("no price was matched, and a cart item needs one");
    }

    Viu.Emporix.CartModels.ExecuteResponse chain = await client.Carts.ExecuteAsync(
        cartId,
        [
            CartCommand.AddCartItem(new Viu.Emporix.CartModels.CartItemRequest
            {
                ItemYrn = ProductYrn.Create(configuration.Tenant, match.Product),
                Quantity = 1,
                Price = new Viu.Emporix.CartModels.PriceRowItem
                {
                    PriceId = match.PriceId,
                    Currency = match.Currency ?? configuration.Currency ?? "CHF",
                    OriginalAmount = match.Original ?? 0,
                    EffectiveAmount = match.Effective ?? match.Original ?? 0,
                },
            }),
            CartCommand.GetCart(),
        ],
        shopper);

    List<Viu.Emporix.CartModels.ExecuteCommandResult> results = [.. chain.Results];
    string codes = string.Join(", ", results.Select(result => result.Code));
    string? itemId = results.Count > 0 ? results[0].ReadCreatedItem()?.ItemId : null;
    Viu.Emporix.CartModels.Cart? cart = results.Count > 1 ? results[1].ReadCart() : null;

    if (itemId is not { Length: > 0 })
    {
        return Step.Failed($"no item id came back; codes {codes}");
    }

    return cart?.Items?.Any(item => item.Id == itemId) == true
        ? Step.Ok($"codes {codes}; the cart read back in the same request holds the item", itemId)
        : Step.Failed($"codes {codes}; the cart read back does not hold item {itemId}");
});

await runner.RunAsync("change that item and read the cart in one chain", async () =>
{
    if (cartId is null || chainedItemId is null)
    {
        return Step.Skipped("no item from the chain");
    }

    Viu.Emporix.CartModels.ExecuteResponse chain = await client.Carts.ExecuteAsync(
        cartId,
        [
            CartCommand.UpdateCartItem(
                chainedItemId,
                new Viu.Emporix.CartModels.UpdateCartItem { Quantity = 3 },
                partial: true),
            CartCommand.GetCart(),
        ],
        shopper);

    List<Viu.Emporix.CartModels.ExecuteCommandResult> results = [.. chain.Results];
    string codes = string.Join(", ", results.Select(result => result.Code));
    double? quantity = (results.Count > 1 ? results[1].ReadCart() : null)?.Items?
        .FirstOrDefault(item => item.Id == chainedItemId)?.Quantity;

    // A 2xx proves nothing on its own: Emporix has accepted and discarded
    // writes before, so the quantity is read back.
    return quantity == 3
        ? Step.Ok($"codes {codes}; the quantity read back is 3")
        : Step.Failed(
            $"codes {codes}; the quantity read back is "
            + (quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"));
});

await runner.RunAsync("a failed command under fail and under resume", async () =>
{
    if (cartId is null)
    {
        return Step.Skipped("no cart");
    }

    const string Missing = "smoke-test-no-such-item";

    try
    {
        await client.Carts.ExecuteAsync(cartId, [CartCommand.DeleteCartItem(Missing)], shopper);
        return Step.Failed("deleting an unknown item did not fail");
    }
    catch (EmporixNotFoundException exception)
        when (exception.Message.Contains("command 0 (DeleteCartItem)", StringComparison.Ordinal))
    {
        // Expected: the failed command, thrown as its REST call would have been.
    }

    Viu.Emporix.CartModels.ExecuteResponse resumed = await client.Carts.ExecuteAsync(
        cartId,
        [CartCommand.DeleteCartItem(Missing), CartCommand.ValidateCart()],
        shopper,
        onError: Viu.Emporix.CartModels.OnError.Resume);

    string codes = string.Join(", ", resumed.Results.Select(result => result.Code));

    return codes == "404, 200"
        ? Step.Ok("fail threw not-found naming the command; resume answered 404, 200")
        : Step.Failed($"resume answered {codes}");
});
```

- [ ] **Step 2: Build the smoke test**

```bash
dotnet build samples/Viu.Emporix.SmokeTest --configuration Release
```

Expected: the build succeeds. Never run the smoke test after a failed build: `--no-build` would run the previous binary.

- [ ] **Step 3: Run it against the tenant**

```bash
set -a; . ~/.emporix-smoke.env; set +a; dotnet run --project samples/Viu.Emporix.SmokeTest --configuration Release --no-build
```

Expected: the three new steps report OK. The known baseline otherwise: `importtool` and `changelog` report SCOPE, direct media upload reports SCOPE, nothing else fails. Copy the three step lines for the pull request.

If a new step fails, that is a live finding, not a test to adjust. Read the codes in its detail, fix the SDK where the SDK is wrong — with a unit test that fails first — and keep the sequence of what the API answered for the pull request.

- [ ] **Step 4: Commit**

```bash
git add samples/Viu.Emporix.SmokeTest/Program.cs
git commit -F - <<'EOF'
test: walk a cart command chain in the smoke test

Three steps on the anonymous throwaway cart, which the last step of the pass
deletes: add an item and read the cart in one chain, change that item's
quantity partially and read it back, and delete an unknown item under both
error modes. The first two are the first live check of bodies sent through a
command, which no unit test can give: a stub answers whatever its author
expected.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Open the pull request

**Files:**
- Create, outside the repository: `pr-body.md` in the session's scratchpad directory

**Interfaces:**
- Consumes: the branch and the results noted in Tasks 5 and 6.
- Produces: the pull request URL.

- [ ] **Step 1: Make sure the branch is current and clean**

```bash
git fetch origin --prune
git log --oneline HEAD..origin/main
git status --porcelain
```

Expected: no output from either. If `main` moved, `git rebase origin/main` and repeat Task 5, Step 4.

- [ ] **Step 2: Check upstream once more**

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch 2>&1 | tail -1
git checkout -- specs/sync-manifest.json
git status --porcelain
```

Expected: `No content changes.`, then a clean tree once the manifest's new timestamps are discarded. A `Changed:` line means upstream moved again: stop and ask the user.

- [ ] **Step 3: Verify the upstream reference**

```bash
gh pr view 524 --repo emporix/api-references --json title,state,mergedAt
```

The Node SDK's design names `emporix/api-references#524` as the source of the change. Link it in the body only if its title is about the cart command chain.

- [ ] **Step 4: Write the body**

Write this to `pr-body.md` in the scratchpad, filling in the count from Task 5, Step 4 and the three lines from Task 6, Step 3:

````markdown
Emporix added a command chain endpoint to the cart service on 2026-09-30 ([changelog](https://developer.emporix.io/changelog#cart-service-command-chain-endpoint-for-cart-operations)). `POST /cart/{tenant}/carts/{cartId}/execute` runs up to ten cart operations on one cart, in order, and answers `207` with one result per command. This wraps it, vendors the specification in the same pull request, and matches what the Node SDK shipped in viuteam/emporix-sdk#361.

Design: `docs/superpowers/specs/2026-10-01-cart-command-chain-design.md` · plan: `docs/superpowers/plans/2026-10-01-cart-command-chain.md`

## Specs that moved

| Spec | Diff | New operations |
|---|---|---|
| `cart` | +482 / −0: one tag, one path, five schemas | 1 |

The vendoring is here rather than in a `chore/spec-sync` pull request because the scheduled sync tests before it opens anything, and with an operation no facade wraps it fails at «Build and test». It stays red until this merges.

## Measurement

```bash
dotnet test --filter "SpecPathTests"
```

After the sync commit, `Every_operation_a_specification_declares_has_a_facade` failed on `POST /cart/{}/carts/{}/execute`. It passes now; the five known availability gaps are unchanged.

## Added

| Verb | Path | Auth | Facade |
|---|---|---|---|
| `POST` | `/cart/{tenant}/carts/{cartId}/execute` | customer or anonymous token. Emporix also accepts a service token with `cart.cart_manage`, which the SDK refuses like every shopper operation | `CartService.ExecuteAsync` |

- `CartCommand`, one factory per command type and named after it. Hand-written: the generated command declares its body as `object? Data`, which no source-generated context can write.
- Seven readers on `ExecuteCommandResult`: `ReadCart`, `ReadCreatedItem`, `ReadAddedItems`, `ReadUpdatedItems`, `ReadAppliedDiscount`, `ReadDiscounts`, `ReadValidation`. Each refuses another command's result.
- Unless `onError` is `Resume`, the first failed command is thrown as its REST exception: an unknown item is an `EmporixNotFoundException` naming `command 1 (DeleteCartItem)`. The commands before it have been applied.

## What arrived without a diff

Nothing else in `cart` changed. Two traps of the generated types are handled and tested: `ExecuteCommandOptions` starts with `partial = false` and `expandCalculation = true`, and without an explicit mapping the query enums would go out as `Fail` and `Resume`.

## Left out

- The `session-id` and `legal-entity-id` headers, as on every REST cart call.
- A client-side check of the ten-command maximum; Emporix answers `400`.
- **Unverified against a live tenant:** `versioning`, `UpdateCart`, `RefreshCart`, `DeleteCartItems`, both batch commands and all four discount commands.
- Found on the way and not changed here: `AddItemAsync` reads the `201 createdCartItem` into `CartItemResponse`, so the new item's id comes back empty; and `BaseConfiguration.Value` is generated as `object`, which suggests that a configuration value holding an object or array cannot be written.

## Verification

- `dotnet build`: clean, warnings are errors.
- `dotnet test`: COUNT passed.
- `dotnet publish samples/Viu.Emporix.Sample -c Release`: the AOT publish succeeded.
- Broken on purpose, each failing the test it should: the explicit null for `partial`, the wire value `resume`, the auth guard, the throw under the default error mode, and a reader's command check.
- Smoke test against tenant `viu`, on its anonymous throwaway cart:

| Step | Result |
|---|---|
| add an item and read the cart in one chain | LINE |
| change that item and read the cart in one chain | LINE |
| a failed command under fail and under resume | LINE |

🤖 Generated with [Claude Code](https://claude.com/claude-code)
````

Replace `COUNT` and each `LINE` with the noted values before saving.

- [ ] **Step 5: Push and open the pull request**

```bash
git push -u origin feat/cart-command-chain
gh pr create --base main --head feat/cart-command-chain --title "feat: run a chain of cart commands in one request" --body-file <scratchpad>/pr-body.md
```

`<scratchpad>` is the session's scratchpad directory; use its literal path.

- [ ] **Step 6: Report and stop**

Report the URL in a `<pr-created>` tag on its own line. Check CI once with `gh pr checks <number>` if asked; never poll. Merging, publishing and the release pull request are the user's calls.

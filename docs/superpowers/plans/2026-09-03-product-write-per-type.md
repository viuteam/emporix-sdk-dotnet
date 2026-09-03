# Typed Product Writing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a caller create, replace and bulk-write a bundle, variant, parent variant or dynamic variant as its own generated type, and send `PATCH` the schema the specification actually declares.

**Architecture:** Three marker interfaces attached to the fifteen generated write types by partial declaration, three `JsonConverter`s whose `Write` dispatches on **exact** runtime type, and five changed signatures on `ProductService`. The exactness check lives in one shared helper rather than fifteen branches, because that check is the safety property.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` source generation, xunit.

**Spec:** [`docs/superpowers/specs/2026-09-03-product-write-per-type-design.md`](../specs/2026-09-03-product-write-per-type-design.md)

## Global Constraints

- **Never invent endpoints, fields or scopes.** Verify against `specs/product.yml` or the Node SDK at `../emporix-sdk`. About half of this SDK's known defects came from reading specifications against the code; none came from a unit test.
- **Never edit `src/Viu.Emporix/Generated/**`.** The next spec sync overwrites it. Everything here goes into hand-written files.
- `TreatWarningsAsErrors` is on for every project. A warning fails the build.
- **No reflection.** `IsAotCompatible` is on with both analyzers. Every `JsonSerializer.Serialize` call takes a concrete `JsonTypeInfo`, never a `Type`.
- **One `JsonSerializerContext` per service.** Everything goes into the existing `ProductJsonContext` — do not add a second context for products.
- After any public API change, run `./scripts/update-public-api.sh` or the build fails on `RS0016`.
- Two public overloads that both have optional parameters trigger `RS0026`. This plan adds no overloads; keep it that way.
- Code, comments and commit messages in **English**. Comments explain why, not what.
- **No nested parentheses in a commit body.** Release Please drops a commit it cannot parse. A code fence does not protect them.
- Every pull request is squashed, so the pull-request title becomes the commit subject and must be a valid conventional commit. This work is a **`feat!`**.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/Viu.Emporix/EmporixProductWrite.cs` | **Create.** The three interfaces and the fifteen `partial` declarations that attach them |
| `src/Viu.Emporix/EmporixProductWriteConverters.cs` | **Create.** The shared exact-type dispatch helper and the three converters |
| `src/Viu.Emporix/JsonContexts.cs` | **Modify.** Register the three converters and nineteen types |
| `src/Viu.Emporix/ProductService.cs` | **Modify.** Five signatures at lines 401, 433, 463, 528, 719 |
| `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs` | **Create.** The converters are pure functions — this is where the real coverage is |
| `tests/Viu.Emporix.Tests/ProductServiceTests.cs` | **Modify.** One line, plus one new service-level test |
| `README.md` | **Modify.** Per-type write examples and the migration note |

The interfaces and the converters are separate files because they change for different reasons: the interfaces when the specification adds a product type, the converters when the dispatch rules change.

**Build order.** Task 1 defines the interfaces everything else references. Task 2's converters need them and cannot be registered without them. Task 3 changes the signatures and needs both. Task 4 documents what exists.

## Reference: the fifteen types and what distinguishes each

Read off `src/Viu.Emporix/Generated/Product.cs`. The tests use these fields; do not substitute others.

| Family | Type | A field that only this type has, or that is always present on it |
| --- | --- | --- |
| Creation | `BasicProductCreation` | none of the below — it is the plain shape |
| | `BundleProductCreation` | `BundledProducts`, non-nullable with `= new BundledProducts()`, so `"bundledProducts"` is **always** in its JSON |
| | `ParentVariantProductCreation` | `VariantAttributes` and `Template`, both non-nullable with defaults, so both keys are always present |
| | `VariantProductCreation` | `ParentVariantId`, a `string?` — set it in the test |
| | `DynamicVariantProductCreation` | `DynamicVariantType`, a `string?` — set it in the test |
| Update | `BasicProductUpdate` | plain |
| | `BundleProductUpdate` | inherits `BundledProducts` from `BundleProduct`, always present |
| | `ParentVariantProductUpdate` | inherits `VariantAttributes`, always present |
| | `VariantProductUpdate` | `ParentVariantId` |
| | `DynamicVariantProductUpdate` | `DynamicVariantType` |
| Bulk update | the five `*BulkUpdate` | each adds `string Id`, non-nullable, so `"id"` is always present |

Two traps this plan exists to handle, both from the generated hierarchy:

- `BasicProductBulkUpdate : BasicProductUpdate` — a bulk type **is** an `IEmporixProductUpdate`
- `BasicProductWithId : BasicProductCreation` — four response types **are** an `IEmporixProductCreation`. `DynamicVariantProductWithId` is the exception, having no base

Also note the two enums are not the same type: the `*Creation` types carry `ProductType?`, the `*Update` types carry `ProductTypeUpdate?`. Do not try to assign one to the other.

---

## Task 1: The three interfaces across the fifteen types

**Files:**
- Create: `src/Viu.Emporix/EmporixProductWrite.cs`
- Test: `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: three empty public interfaces in namespace `Viu.Emporix.ProductModels` — `IEmporixProductCreation`, `IEmporixProductUpdate`, `IEmporixProductBulkUpdate`. Implemented by the five `*Creation`, the five `*Update` and the five `*BulkUpdate` types respectively.

**Why a hand-written file rather than a generator change:** `partial` declarations in the same assembly attach the interfaces without touching `Generated/`. A consumer cannot do this from their own project — `partial` does not cross assemblies, and the declaration would create a colliding new type.

- [ ] **Step 1: Write the failing test**

Create `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs`:

```csharp
using System.Text.Json;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// Writing a product as the type the specification's oneOf names.
/// </summary>
/// <remarks>
/// Four of the five write bodies in specs/product.yml are a oneOf over five
/// type-specific schemas, and the SDK sent the basic shape for all of them. The
/// converters here are pure functions — object in, JSON out, no HTTP — which is
/// why these tests carry weight. What they cannot establish is whether Emporix
/// accepts the bodies; that needs the smoke test.
/// </remarks>
public class EmporixProductWriteConverterTests
{
    [Fact]
    public void The_five_creation_types_carry_the_creation_interface()
    {
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BasicProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BundleProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(ParentVariantProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(VariantProductCreation)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(DynamicVariantProductCreation)));
    }

    [Fact]
    public void The_five_update_types_carry_the_update_interface()
    {
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BasicProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BundleProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(ParentVariantProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(VariantProductUpdate)));
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(DynamicVariantProductUpdate)));
    }

    [Fact]
    public void The_five_bulk_types_carry_the_bulk_interface()
    {
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(BasicProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(BundleProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(ParentVariantProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(VariantProductBulkUpdate)));
        Assert.True(typeof(IEmporixProductBulkUpdate).IsAssignableFrom(typeof(DynamicVariantProductBulkUpdate)));
    }

    [Fact]
    public void The_generated_hierarchy_lets_the_wrong_type_satisfy_an_interface()
    {
        // Not a wish, a fact about the generated code — and the whole reason the
        // converters dispatch on exact runtime type. Asserted so that a spec
        // sync which happened to break these inheritances would show up here,
        // where the comment explains why anyone cared.
        //
        // A bulk update derives from its plain update:
        Assert.True(typeof(IEmporixProductUpdate).IsAssignableFrom(typeof(BasicProductBulkUpdate)));

        // And four response types derive from their creation type:
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BasicProductWithId)));
        Assert.True(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(BundleProductWithId)));

        // DynamicVariantProductWithId is the exception: it has no base at all,
        // which is also why the read side needed explicit members for it.
        Assert.False(typeof(IEmporixProductCreation).IsAssignableFrom(typeof(DynamicVariantProductWithId)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductWriteConverterTests"`
Expected: compile failure — `IEmporixProductCreation` does not exist.

- [ ] **Step 3: Write the interfaces and the declarations**

Create `src/Viu.Emporix/EmporixProductWrite.cs`:

```csharp
namespace Viu.Emporix.ProductModels;

/// <summary>
/// A product in one of the five shapes <c>POST /products</c> accepts.
/// </summary>
/// <remarks>
/// <para>
/// <c>specs/product.yml</c> declares the creation body as a <c>oneOf</c> over
/// <c>basicProductCreation</c>, <c>bundleProductCreation</c>,
/// <c>parentVariantProductCreation</c>, <c>variantProductCreation</c> and
/// <c>dynamicVariantProductCreation</c>. This interface is the parameter type
/// that lets a caller pass any of them.
/// </para>
/// <para>
/// Deliberately without members. Nothing reads through this — the caller holds
/// the concrete type and <c>EmporixProductCreationConverter</c> dispatches on
/// it. Members would be forwarding for nobody.
/// </para>
/// <para>
/// <b>Implementing this outside the SDK does not work.</b> The converter
/// dispatches on exact type and throws for anything it does not know. The five
/// types below are the whole set.
/// </para>
/// </remarks>
public interface IEmporixProductCreation;

/// <summary>
/// A product in one of the five shapes <c>PUT /products/{productId}</c> accepts.
/// </summary>
/// <remarks>
/// The specification's <c>productUpdateBody</c> is a <c>oneOf</c> over the five
/// <c>*Update</c> schemas. Note that <c>PATCH</c> is <b>not</b> one of these:
/// it declares a single flat <c>productPartialUpdate</c>, which is why
/// <see cref="Viu.Emporix.ProductService"/> takes
/// <see cref="ProductPartialUpdate"/> there and this interface here.
/// </remarks>
public interface IEmporixProductUpdate;

/// <summary>
/// A product in one of the five shapes <c>PUT /products/bulk</c> accepts.
/// </summary>
/// <remarks>
/// The specification declares an array whose <c>items</c> are a <c>oneOf</c>,
/// so one call may carry a mix of shapes. That is what this interface plus its
/// converter deliver; a per-type method could not.
/// </remarks>
public interface IEmporixProductBulkUpdate;

// Attached through the generated classes' own partial declarations. Editing
// Generated/ would work until the next spec sync overwrote it.
//
// No explicit members anywhere below: the interfaces have none. All five
// creation types reach ProductCore, including DynamicVariantProductCreation
// through DynamicVariantProduct — unlike DynamicVariantProductWithId on the
// read side, which has no base and needed forwarding.

public partial class BasicProductCreation : IEmporixProductCreation;

public partial class BundleProductCreation : IEmporixProductCreation;

public partial class ParentVariantProductCreation : IEmporixProductCreation;

public partial class VariantProductCreation : IEmporixProductCreation;

public partial class DynamicVariantProductCreation : IEmporixProductCreation;

public partial class BasicProductUpdate : IEmporixProductUpdate;

public partial class BundleProductUpdate : IEmporixProductUpdate;

public partial class ParentVariantProductUpdate : IEmporixProductUpdate;

public partial class VariantProductUpdate : IEmporixProductUpdate;

public partial class DynamicVariantProductUpdate : IEmporixProductUpdate;

// These five derive from the *Update types above, so they satisfy
// IEmporixProductUpdate as well. Nothing can be done about that from here —
// it is why the update converter checks exact types.
public partial class BasicProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class BundleProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class ParentVariantProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class VariantProductBulkUpdate : IEmporixProductBulkUpdate;

public partial class DynamicVariantProductBulkUpdate : IEmporixProductBulkUpdate;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductWriteConverterTests"`
Expected: 4 passed.

If `The_generated_hierarchy_lets_the_wrong_type_satisfy_an_interface` fails, the generated hierarchy has changed since this plan was written. **Stop and re-read the spec's section 1** rather than adjusting the test — the converters' design depends on those facts.

- [ ] **Step 5: Record the public API**

Run: `./scripts/update-public-api.sh && dotnet build`
Expected: 0 warnings. About 33 new entries — the three interfaces plus the fifteen types and their implicit constructors, which enter the baseline because a partial declaration in a hand-written file takes them out of the analyzer's generated-code exemption. The same thing happened on the read side.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix/EmporixProductWrite.cs tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs src/Viu.Emporix/PublicAPI.Unshipped.txt
git commit -m "feat: give the product write types three shared interfaces

Four of the five write bodies in the specification are a oneOf over the five
type-specific schemas. These interfaces are the parameter types that let a
caller pass any of them.

Marker interfaces without members, unlike the read side's IEmporixProduct:
nothing reads through them, since the caller holds the concrete type and the
converters dispatch on it.

One test asserts something nobody wants — that a bulk update satisfies the
plain update interface, and that four response types satisfy the creation
interface. Both follow from the generated hierarchy, and both are why the
converters have to compare exact types.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: The three write converters

**Files:**
- Create: `src/Viu.Emporix/EmporixProductWriteConverters.cs`
- Modify: `src/Viu.Emporix/JsonContexts.cs`
- Modify: `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs`

**Interfaces:**
- Consumes: the three interfaces from Task 1.
- Produces:
  - `internal static class ProductWriteDispatch` with
    `TryWrite<T>(Utf8JsonWriter writer, object value, Type type, JsonTypeInfo<T> typeInfo)` → `bool`
    and `Unsupported(Type type, string body, string permitted)` → `NotSupportedException`
  - `internal sealed class EmporixProductCreationConverter : JsonConverter<IEmporixProductCreation>`
  - `internal sealed class EmporixProductUpdateConverter : JsonConverter<IEmporixProductUpdate>`
  - `internal sealed class EmporixProductBulkUpdateConverter : JsonConverter<IEmporixProductBulkUpdate>`
  - On `ProductJsonContext`: the three converters registered, and `IEmporixProductCreation`, `List<IEmporixProductCreation>`, `IEmporixProductUpdate`, `IEmporixProductBulkUpdate`, `List<IEmporixProductBulkUpdate>`, the twelve non-basic write types, `BasicProductBulkUpdate` as a single type and `ProductPartialUpdate` as serializable

- [ ] **Step 1: Write the failing tests**

Append to `EmporixProductWriteConverterTests.cs`, inside the class:

```csharp
    // ---------- Each type reaches the wire in its own shape ----------

    [Fact]
    public void Each_creation_type_serializes_as_itself()
    {
        // The distinguishing fields come from the generated code: a bundle's
        // bundledProducts and a parent variant's variantAttributes are
        // non-nullable with default instances, so their keys are always
        // present; the other two are set here explicitly.
        Assert.DoesNotContain(
            "bundledProducts",
            Write<IEmporixProductCreation>(
                new BasicProductCreation { Code = "plain" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "bundledProducts",
            Write<IEmporixProductCreation>(
                new BundleProductCreation { Code = "gift" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductCreation>(
                new ParentVariantProductCreation { Code = "shirt" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductCreation>(
                new VariantProductCreation { Code = "red-m", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductCreation>(
                new DynamicVariantProductCreation { Code = "dyn", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductCreation),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_update_type_serializes_as_itself()
    {
        Assert.DoesNotContain(
            "bundledProducts",
            Write<IEmporixProductUpdate>(
                new BasicProductUpdate { Code = "plain" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "bundledProducts",
            Write<IEmporixProductUpdate>(
                new BundleProductUpdate { Code = "gift" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductUpdate>(
                new ParentVariantProductUpdate { Code = "shirt" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductUpdate>(
                new VariantProductUpdate { Code = "red-m", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductUpdate>(
                new DynamicVariantProductUpdate { Code = "dyn", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductUpdate),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Each_bulk_type_serializes_as_itself_and_keeps_its_id()
    {
        // The id is what says which product to change, and it exists only on
        // the bulk types. Losing it is the failure mode this whole design
        // guards against.
        Assert.Contains(
            "\"id\":\"b1\"",
            Write<IEmporixProductBulkUpdate>(
                new BasicProductBulkUpdate { Id = "b1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        string bundle = Write<IEmporixProductBulkUpdate>(
            new BundleProductBulkUpdate { Id = "g1" },
            ProductJsonContext.Default.IEmporixProductBulkUpdate);

        Assert.Contains("\"id\":\"g1\"", bundle, StringComparison.Ordinal);
        Assert.Contains("bundledProducts", bundle, StringComparison.Ordinal);

        Assert.Contains(
            "variantAttributes",
            Write<IEmporixProductBulkUpdate>(
                new ParentVariantProductBulkUpdate { Id = "p1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"parentVariantId\":\"parent-42\"",
            Write<IEmporixProductBulkUpdate>(
                new VariantProductBulkUpdate { Id = "v1", ParentVariantId = "parent-42" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);

        Assert.Contains(
            "\"dynamicVariantType\":\"H1_L1\"",
            Write<IEmporixProductBulkUpdate>(
                new DynamicVariantProductBulkUpdate { Id = "d1", DynamicVariantType = "H1_L1" },
                ProductJsonContext.Default.IEmporixProductBulkUpdate),
            StringComparison.Ordinal);
    }

    // ---------- No field is lost on the way out ----------

    [Fact]
    public void A_bundle_survives_the_round_trip_through_the_interface()
    {
        BundleProductCreation original = new()
        {
            Code = "gift",
            BundledProducts = { new Anonymous { ProductId = "p1", Amount = 2 } },
        };

        string json = Write<IEmporixProductCreation>(
            original, ProductJsonContext.Default.IEmporixProductCreation);

        // Back through the concrete type: if the converter had serialized the
        // value through a base contract, the bundled products would be gone
        // and this would be an empty collection rather than a failure.
        BundleProductCreation? read = JsonSerializer.Deserialize(
            json, ProductJsonContext.Default.BundleProductCreation);

        Assert.Equal("gift", read?.Code);
        Assert.Single(read!.BundledProducts);
        Assert.Equal("p1", read.BundledProducts[0].ProductId);
        Assert.Equal(2, read.BundledProducts[0].Amount);
    }

    [Fact]
    public void Unset_properties_are_not_written_as_null()
    {
        // What makes a partial write safe: a body that sent every untouched
        // property as null would clear fields the caller never mentioned.
        // DefaultIgnoreCondition on ProductJsonContext is what guarantees it.
        string json = Write<IEmporixProductCreation>(
            new BasicProductCreation { Code = "plain" },
            ProductJsonContext.Default.IEmporixProductCreation);

        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\"", json, StringComparison.Ordinal);
    }

    // ---------- The two traps ----------

    [Fact]
    public void A_bulk_update_passed_as_a_plain_update_is_refused()
    {
        // BasicProductBulkUpdate derives from BasicProductUpdate, so this
        // compiles. A pattern match in the converter would serialize it
        // through the base contract and drop the id — the field that says
        // which product to change — with no error at all. Exact-type dispatch
        // turns that silence into this exception.
        IEmporixProductUpdate wrong = new BasicProductBulkUpdate { Id = "b1" };

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => Write(wrong, ProductJsonContext.Default.IEmporixProductUpdate));

        Assert.Contains("BasicProductBulkUpdate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_product_that_was_read_cannot_be_created()
    {
        // BasicProductWithId derives from BasicProductCreation, so this
        // compiles too. The message has to say why, or the exception reads as
        // nonsense to someone who just passed a product they fetched.
        IEmporixProductCreation wrong = new BasicProductWithId { Id = "b1", Code = "plain" };

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => Write(wrong, ProductJsonContext.Default.IEmporixProductCreation));

        Assert.Contains("BasicProductWithId", error.Message, StringComparison.Ordinal);
        Assert.Contains("derives from", error.Message, StringComparison.Ordinal);
    }

    // ---------- One array, five shapes ----------

    [Fact]
    public void A_mixed_bulk_array_writes_each_element_in_its_own_shape()
    {
        // The capability no per-type method could offer: the specification
        // declares PUT /products/bulk as an array whose items are a oneOf.
        List<IEmporixProductBulkUpdate> products =
        [
            new BasicProductBulkUpdate { Id = "b1" },
            new BundleProductBulkUpdate { Id = "g1" },
            new ParentVariantProductBulkUpdate { Id = "p1" },
            new VariantProductBulkUpdate { Id = "v1", ParentVariantId = "parent-42" },
            new DynamicVariantProductBulkUpdate { Id = "d1", DynamicVariantType = "H1_L1" },
        ];

        string json = JsonSerializer.Serialize(
            products, ProductJsonContext.Default.ListIEmporixProductBulkUpdate);

        Assert.Contains("\"id\":\"b1\"", json, StringComparison.Ordinal);
        Assert.Contains("bundledProducts", json, StringComparison.Ordinal);
        Assert.Contains("variantAttributes", json, StringComparison.Ordinal);
        Assert.Contains("\"parentVariantId\":\"parent-42\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dynamicVariantType\":\"H1_L1\"", json, StringComparison.Ordinal);
    }

    // ---------- Reading is refused ----------

    [Fact]
    public void Reading_through_the_write_interfaces_is_refused()
    {
        // These are request bodies; nothing in the API returns them. The read
        // side's converter throws on Write for the mirror-image reason.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"code":"plain"}""", ProductJsonContext.Default.IEmporixProductCreation));

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"code":"plain"}""", ProductJsonContext.Default.IEmporixProductUpdate));

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            """{"id":"b1"}""", ProductJsonContext.Default.IEmporixProductBulkUpdate));
    }

    private static string Write<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductWriteConverterTests"`
Expected: compile failure — `ProductJsonContext.Default.IEmporixProductCreation` does not exist.

- [ ] **Step 3: Write the dispatch helper and the three converters**

Create `src/Viu.Emporix/EmporixProductWriteConverters.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix;

/// <summary>
/// The one place a write converter decides that a value is exactly some type.
/// </summary>
/// <remarks>
/// <para>
/// Shared by all three converters so that the exactness check exists once
/// rather than fifteen times. That check is the safety property here: the
/// generated write hierarchy is not disjoint — a bulk update derives from its
/// plain update, and four response types derive from their creation type — so
/// a <c>value is BasicProductUpdate</c> pattern match would accept a derived
/// value and serialize it through the base contract, dropping whatever the
/// derived type adds with no error at all.
/// </para>
/// <para>
/// One branch out of fifteen written as a pattern match would reintroduce that,
/// and no test of the other fourteen would notice.
/// </para>
/// </remarks>
internal static class ProductWriteDispatch
{
    /// <summary>
    /// Serializes <paramref name="value"/> when it is exactly
    /// <typeparamref name="T"/>, and reports whether it did.
    /// </summary>
    public static bool TryWrite<T>(
        Utf8JsonWriter writer,
        object value,
        Type type,
        JsonTypeInfo<T> typeInfo)
    {
        if (type != typeof(T))
        {
            return false;
        }

        JsonSerializer.Serialize(writer, (T)value, typeInfo);
        return true;
    }

    /// <summary>
    /// The exception for a type no branch matched, saying why it compiled.
    /// </summary>
    /// <remarks>
    /// The «derives from» sentence is the whole point of this message. Someone
    /// who passes a product they just fetched into a create call needs to be
    /// told that the response type inherits the creation type, or the refusal
    /// reads as a bug in the SDK.
    /// </remarks>
    public static NotSupportedException Unsupported(Type type, string body, string permitted)
        => new(
            $"{type.Name} cannot be written as a {body}. The specification permits exactly: {permitted}. "
            + "A type outside that list can reach here because the generated hierarchy is not disjoint — "
            + $"{type.Name} derives from one of them, which is why the call compiled. Serializing it "
            + "through its base type would silently drop the fields it adds, so it is refused instead.");
}

/// <summary>
/// Writes a product as whichever of the five creation shapes it is.
/// </summary>
internal sealed class EmporixProductCreationConverter : JsonConverter<IEmporixProductCreation>
{
    private const string Permitted =
        "BasicProductCreation, BundleProductCreation, ParentVariantProductCreation, "
        + "VariantProductCreation, DynamicVariantProductCreation";

    public override IEmporixProductCreation? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A creation type is a request body; no Emporix endpoint returns one. "
            + "Read a product through ProductService or its AnyType group instead.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductCreation value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductCreation)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductCreation))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product creation", Permitted);
    }
}

/// <summary>
/// Writes a product as whichever of the five update shapes it is.
/// </summary>
/// <remarks>
/// For <c>PUT</c> only. <c>PATCH</c> declares a single flat
/// <c>productPartialUpdate</c> and needs no converter.
/// </remarks>
internal sealed class EmporixProductUpdateConverter : JsonConverter<IEmporixProductUpdate>
{
    private const string Permitted =
        "BasicProductUpdate, BundleProductUpdate, ParentVariantProductUpdate, "
        + "VariantProductUpdate, DynamicVariantProductUpdate";

    public override IEmporixProductUpdate? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "An update type is a request body; no Emporix endpoint returns one. "
            + "Read a product through ProductService or its AnyType group instead.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductUpdate value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductUpdate))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product update", Permitted);
    }
}

/// <summary>
/// Writes a product as whichever of the five bulk-update shapes it is.
/// </summary>
/// <remarks>
/// This is what lets one bulk call carry a mix of shapes, which the
/// specification's array of <c>oneOf</c> permits and a per-type method could
/// not offer.
/// </remarks>
internal sealed class EmporixProductBulkUpdateConverter : JsonConverter<IEmporixProductBulkUpdate>
{
    private const string Permitted =
        "BasicProductBulkUpdate, BundleProductBulkUpdate, ParentVariantProductBulkUpdate, "
        + "VariantProductBulkUpdate, DynamicVariantProductBulkUpdate";

    public override IEmporixProductBulkUpdate? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A bulk update type is a request body; the bulk endpoint answers with BulkResponse entries.");

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProductBulkUpdate value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        Type type = value.GetType();

        if (ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BasicProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.BundleProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.ParentVariantProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.VariantProductBulkUpdate)
            || ProductWriteDispatch.TryWrite(writer, value, type, ProductJsonContext.Default.DynamicVariantProductBulkUpdate))
        {
            return;
        }

        throw ProductWriteDispatch.Unsupported(type, "product bulk update", Permitted);
    }
}
```

- [ ] **Step 4: Register everything on the product context**

In `src/Viu.Emporix/JsonContexts.cs`, the `ProductJsonContext` declaration already carries
`Converters = [typeof(EmporixProductConverter)]` from the read side. Extend that list and add
the entries. Read the existing `JsonSourceGenerationOptions` off the file rather than copying
from here, and change only the `Converters` line:

```csharp
    Converters = [
        typeof(EmporixProductConverter),
        typeof(EmporixProductCreationConverter),
        typeof(EmporixProductUpdateConverter),
        typeof(EmporixProductBulkUpdateConverter),
    ])]
```

Then add, beside the existing product entries:

```csharp
// The write bodies. Four of the five are a oneOf in the specification, so the
// parameter type is an interface and the concrete types below are what the
// converters serialize as.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductCreation))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProductCreation>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProductBulkUpdate))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProductBulkUpdate>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductCreation))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BasicProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductBulkUpdate))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductBulkUpdate))]
// PATCH /products/{productId} declares this single flat schema — not a oneOf.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ProductPartialUpdate))]
```

`BasicProductCreation` and `BasicProductUpdate` are already registered; do not add them twice.

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductWriteConverterTests"`
Expected: 13 passed.

If `Each_creation_type_serializes_as_itself` reports the basic shape's JSON containing
`bundledProducts`, the dispatch matched the wrong branch — check that `TryWrite` compares with
`!=` on `typeof(T)` and not with `is`.

- [ ] **Step 6: Verify the AOT bar**

Run: `dotnet publish samples/Viu.Emporix.Sample --configuration Release`
Expected: 0 warnings. An `IL2026` or `IL3050` means something resolves a type at runtime — every
`Serialize` call must take a `JsonTypeInfo`.

- [ ] **Step 7: Run the whole suite and record the API**

```bash
dotnet test
./scripts/update-public-api.sh && dotnet build
```

Expected: everything green, 0 warnings. The converters and the helper are `internal`, so the
baseline should report no new symbols.

- [ ] **Step 8: Commit**

```bash
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: write a product as the type its shape names

Three converters over the write interfaces, each dispatching to the concrete
type's own JsonTypeInfo. The bulk one is what lets a single call carry a mix of
shapes, which the specification's array of oneOf permits.

The exactness check lives in one shared helper rather than in fifteen branches.
It is the safety property here: the generated hierarchy is not disjoint, so a
pattern match would accept a derived value and serialize it through the base
contract, dropping whatever the derived type adds without an error. One branch
out of fifteen written the wrong way would reintroduce that, and no test of the
other fourteen would notice.

Two tests cover exactly that: a bulk update passed as a plain update is
refused, and so is a product that was read being passed to a create. Both
compile, because of the inheritance, and the exception message says so.

Reading through the write interfaces throws. These are request bodies and no
endpoint returns them, which mirrors the read side's converter refusing to
write.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: The five signatures

**Files:**
- Modify: `src/Viu.Emporix/ProductService.cs` — `CreateAsync:401`, `UpdateAsync:433`, `ReplaceAsync:463`, `CreateManyAsync:528`, `UpdateManyAsync:719`
- Modify: `tests/Viu.Emporix.Tests/ProductServiceTests.cs:368`

**Interfaces:**
- Consumes: the three interfaces from Task 1, the context entries from Task 2.
- Produces: the five public signatures below. Nothing later depends on them.

**This is the breaking task.** Four of the five keep existing callers compiling; `UpdateAsync` does not, and that is deliberate — see the spec's section 3.

- [ ] **Step 1: Change `CreateAsync`**

At `ProductService.cs:401`, change the parameter type and the `JsonTypeInfo`, and leave the
verb, path, query, auth default and return type alone:

```csharp
    public async Task<ResourceLocation?> CreateAsync(
        IEmporixProductCreation product,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Post,
                Path = BasePath,
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    product,
                    ProductJsonContext.Default.IEmporixProductCreation),
            },
            ProductJsonContext.Default.ResourceLocation,
            cancellationToken).ConfigureAwait(false);
    }
```

Add to its XML documentation, above the existing `<exception>` entries:

```csharp
    /// <remarks>
    /// Pass whichever of the five creation types fits — the specification
    /// declares this body as a <c>oneOf</c> over all five, and the SDK sends
    /// whichever one it receives.
    /// </remarks>
```

- [ ] **Step 2: Change `UpdateAsync` to the schema the specification declares**

At `ProductService.cs:433`. This is the defect fix, not a widening:

```csharp
    /// <summary>Changes individual fields of a product.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="changes">The fields to change.</param>
    /// <param name="options">Fine-tuning for the write.</param>
    /// <param name="auth">What to authorise with; a service token when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// <para>
    /// Replaces only what is stated. For a full exchange see <see cref="ReplaceAsync"/>.
    /// </para>
    /// <para>
    /// <b>Not per product type.</b> The specification declares this body as one
    /// flat <c>productPartialUpdate</c> rather than a <c>oneOf</c>, and
    /// <see cref="ProductPartialUpdate"/> carries the union of the
    /// type-specific fields — <c>BundledProducts</c>, <c>VariantAttributes</c>
    /// and <c>Template</c> among them. So a bundle's contents are patched
    /// through this one type, with no bundle-specific alternative to choose.
    /// </para>
    /// </remarks>
    public Task UpdateAsync(
        string productId,
        ProductPartialUpdate changes,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(changes);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Patch,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    changes,
                    ProductJsonContext.Default.ProductPartialUpdate),
            },
            cancellationToken);
    }
```

- [ ] **Step 3: Change `ReplaceAsync`**

At `ProductService.cs:463`:

```csharp
    public Task ReplaceAsync(
        string productId,
        IEmporixProductUpdate product,
        ProductWriteOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(product);

        return _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Put,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Service(auth),
                Query = WriteQuery(options),
                Content = EmporixJsonContent.Create(
                    product,
                    ProductJsonContext.Default.IEmporixProductUpdate),
            },
            cancellationToken);
    }
```

- [ ] **Step 4: Change the two bulk methods**

At `ProductService.cs:528` and `:719`, change only the element type of the collection parameter
and the `JsonTypeInfo` passed to `EmporixJsonContent.Create`:

- `CreateManyAsync(IReadOnlyCollection<BasicProductCreation> products, …)` becomes
  `IReadOnlyCollection<IEmporixProductCreation>`, serialized with
  `ProductJsonContext.Default.ListIEmporixProductCreation`
- `UpdateManyAsync(IEnumerable<BasicProductBulkUpdate> products, …)` becomes
  `IEnumerable<IEmporixProductBulkUpdate>`, serialized with
  `ProductJsonContext.Default.ListIEmporixProductBulkUpdate`

Both bodies build a `List<>` from the parameter before serializing; change that local's element
type to match and leave the empty-collection short circuits, the chunking and the
`Idempotent` flags exactly as they are. **Read the two method bodies before editing** — they
differ from each other, and this plan does not reproduce them.

Add to each method's `<remarks>`:

```csharp
    /// <para>
    /// One call may carry a mix of product types: the specification declares
    /// the body as an array whose items are a <c>oneOf</c>.
    /// </para>
```

- [ ] **Step 5: Fix the one existing test line**

In `tests/Viu.Emporix.Tests/ProductServiceTests.cs:368`:

```csharp
        await Create(patchHandler).UpdateAsync("p1", new ProductPartialUpdate());
```

The line below it, `ReplaceAsync("p1", new BasicProductUpdate())`, needs **no** change —
`BasicProductUpdate` converts to `IEmporixProductUpdate` implicitly. The seven `CreateAsync`
and `CreateManyAsync` call sites in that file also need none: `IReadOnlyCollection<out T>` and
`IEnumerable<out T>` are covariant.

If any other line fails to compile, do not widen a signature to accommodate it — read what the
test is passing and decide whether the test or the expectation is wrong.

- [ ] **Step 6: Add the test for the capability that did not exist**

Append to `ProductServiceTests.cs`, in the writes region:

```csharp
    [Fact]
    public async Task Update_can_patch_a_bundles_contents()
    {
        // The capability the old signature withheld: BasicProductUpdate has no
        // property for bundledProducts, so this body could not be built at all.
        // The specification declares PATCH as productPartialUpdate, which
        // carries the union of the type-specific fields.
        StubHttpMessageHandler handler = new(HttpStatusCode.NoContent, "");
        ProductService products = Create(handler);

        await products.UpdateAsync(
            "g1",
            new ProductPartialUpdate
            {
                // Assigned, not initialized into. See the note below this test.
                BundledProducts = [new Anonymous { ProductId = "p1", Amount = 3 }],
            });

        Assert.Equal(HttpMethod.Patch, handler.RequestMethods[0]);
        Assert.Equal("/product/acme/products/g1", Uri(handler));
        Assert.Contains("\"productId\":\"p1\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"amount\":3", handler.RequestBodies[0], StringComparison.Ordinal);
    }
```

**Why assigned rather than initialized into.** `ProductPartialUpdate.BundledProducts` is
declared `BundledProducts? … = default!`, so it starts as `null`. The collection-initializer
form `BundledProducts = { item }` compiles into `BundledProducts.Add(item)` against that null
and throws a `NullReferenceException` at runtime — a failure that looks like a bug in the SDK.
The bundle types are the opposite: `BundleProduct.BundledProducts` is non-nullable with
`= new BundledProducts()`, so the initializer form is correct there and is what Task 2's tests
and the README examples use. Only this one type needs the assignment.

`BundledProducts` derives from `Collection<Anonymous>`, so the collection expression `[…]` binds
to its constructor. If it does not compile, use `new BundledProducts { new Anonymous { … } }`
— never the bare initializer form on this property.

- [ ] **Step 7: Run everything**

```bash
dotnet test
dotnet publish samples/Viu.Emporix.Sample --configuration Release
dotnet publish samples/Viu.Emporix.Storefront --configuration Release
```

Expected: all green, 0 AOT warnings. `SpecPathTests` must still pass without change — the
verbs and paths did not move.

- [ ] **Step 8: Record the public API**

Run: `./scripts/update-public-api.sh && dotnet build`

Expected: five removals and five additions. The removals get `*REMOVED*` markers, which
`scripts/promote-public-api.sh` handles — that gap was fixed for exactly this case. Check the
diff and confirm the five removed entries are the old signatures and nothing else.

- [ ] **Step 9: Commit**

```bash
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat!: write products as their own type, and patch the declared schema

CreateAsync, ReplaceAsync, CreateManyAsync and UpdateManyAsync now take the
write interfaces, so a bundle or a variant can be created and replaced as
itself. A bulk call may carry a mix of shapes.

UpdateAsync now takes ProductPartialUpdate, which is the schema the
specification declares for PATCH. The type it took before had no property for
bundledProducts, variantAttributes or template, so a bundle's contents could
not be patched at all, and it carried productType, which that body does not
declare.

BREAKING CHANGE: UpdateAsync takes ProductPartialUpdate rather than
BasicProductUpdate. The two are unrelated classes, so every PATCH call needs
editing. The old signature sent a schema the specification does not declare,
and keeping it beside the new one would trip RS0026 and need a renamed
sibling, so the break is taken rather than carried forward.

The other four signatures keep existing callers compiling: a concrete type
converts to its interface implicitly, and the two collection interfaces are
covariant.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Document it and close the loop

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-03-product-write-per-type-design.md`
- Modify: `samples/Viu.Emporix.SmokeTest/Program.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: no API.

- [ ] **Step 1: Update the README's write examples**

The `## Products` section's code block ends with three write lines:

```csharp
// Writes.
var created = await client.Products.CreateAsync(newProduct);
await client.Products.UpdateAsync("p1", changes);
await client.Products.DeleteAsync("p1", force: true);
```

Leave that block as it is — it still compiles — and add a subsection immediately after
«Bundles, variants and the other product shapes»:

````markdown
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

If you are coming from an earlier version, this is the one write call whose
signature broke: it used to take `BasicProductUpdate`, which had no property
for any of those fields.
````

- [ ] **Step 2: Record the write gap in the smoke test**

In `samples/Viu.Emporix.SmokeTest/Program.cs`, beside the comment the read-side work left at
the «fetch one product» step, add:

```csharp
// Also not covered: whether Emporix accepts a mixed array on PUT /products/bulk,
// and whether PATCH rejects productType or ignores it. Both are writes, and the
// seller-side pass here is read-only by design — establishing them needs an
// opt-in write step against a scratch tenant.
```

- [ ] **Step 3: Mark the spec's open questions as still open**

In the design spec's «Open questions» section, append:

```markdown
As of the implementation both remain open. The smoke test carries a comment at
the product section naming them, and follow-up 1 is the opt-in write step that
would settle them.
```

- [ ] **Step 4: Full verification**

```bash
dotnet build                                                           # 0 warnings
dotnet test                                                            # all green
dotnet publish samples/Viu.Emporix.Sample --configuration Release      # 0 AOT warnings
dotnet publish samples/Viu.Emporix.Storefront --configuration Release  # 0 AOT warnings
./scripts/update-public-api.sh                                         # no missing entries
```

- [ ] **Step 5: Commit**

```bash
git add README.md docs/superpowers/specs samples/Viu.Emporix.SmokeTest
git commit -m "docs: document typed product writing

A README subsection under Products with the per-type creation calls, the mixed
bulk array and the PATCH exception, plus the migration line for the one
signature that broke.

The smoke test gains a comment naming the two questions this work could not
settle: whether Emporix accepts a mixed bulk array, and whether PATCH rejects
productType or ignores it. Both need a write, and the seller-side pass is
read-only by design.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## After the plan

Three follow-ups from the spec stay untouched: the opt-in smoke-test write step, the
`GeneratedCodeFixer` rule for unknown enum values, and the `AnyType` counterparts for
`ListAllAsync` and `ListVariantsAsync`.

The fourth is worth restating because this work changes its weight. `BundledProducts` is a
`Collection<Anonymous>`, so a caller creating a bundle writes
`new Anonymous { ProductId = …, Amount = … }`. Before this plan that type was something a
bundle *reader* saw; now it is the first thing a bundle *writer* types. Renaming it in a
`GeneratedCodeFixer` rule moves from cosmetic to worth doing.

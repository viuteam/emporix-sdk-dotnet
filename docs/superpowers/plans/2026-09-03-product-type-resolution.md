# Product Type Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a caller read a bundle, variant, parent variant or dynamic variant as its own generated type instead of always receiving `BasicProductWithId`.

**Architecture:** An interface across the five generated product types, a `JsonConverter` that reads `productType` and defers to the concrete type's own `JsonTypeInfo`, and a nested `AnyType` operation group on `ProductService`. The seven existing methods keep their signatures; the four private cores they already share become generic over the element type, so the path and query logic exists once rather than twice.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` source generation, `Utf8JsonReader` for the discriminator peek, xunit.

**Spec:** [`docs/superpowers/specs/2026-09-03-product-type-resolution-design.md`](../specs/2026-09-03-product-type-resolution-design.md)

## Global Constraints

- **Never invent endpoints, fields or scopes.** Verify against `specs/product.yml` or the Node SDK at `../emporix-sdk`. About half of this SDK's known defects came from reading specifications against the code; none came from a unit test.
- **Never edit `src/Viu.Emporix/Generated/**`.** The next spec sync overwrites it. Everything this plan adds goes into hand-written files.
- `TreatWarningsAsErrors` is on for every project. A warning fails the build.
- **No reflection.** `IsAotCompatible` is on with both analyzers. The converter must call `JsonSerializer.Deserialize` with a concrete `JsonTypeInfo`, never a `Type`.
- **One `JsonSerializerContext` per service.** Everything here goes into the existing `ProductJsonContext` — do not add a second context for products.
- After any public API change, run `./scripts/update-public-api.sh` or the build fails on `RS0016`.
- Two public overloads that both have optional parameters trigger `RS0026`. The design avoids it by putting the new methods on a different class; keep it that way.
- Code, comments and commit messages in **English**. Comments explain why, not what.
- **No nested parentheses in a commit body.** Release Please drops a commit it cannot parse. A code fence does not protect them.
- Every pull request is squashed, so the pull-request title becomes the commit subject and must be a valid conventional commit.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/Viu.Emporix/EmporixProduct.cs` | **Create.** `IEmporixProduct` and the five `partial` declarations that attach it |
| `src/Viu.Emporix/EmporixProductConverter.cs` | **Create.** The `productType` peek and the switch to a concrete type |
| `src/Viu.Emporix/JsonContexts.cs` | **Modify.** Register the converter and the four types that never travelled before |
| `src/Viu.Emporix/ProductService.cs` | **Modify.** Four private cores become generic; add the `AnyType` property and the group class |
| `tests/Viu.Emporix.Tests/EmporixProductConverterTests.cs` | **Create.** The converter is a pure function — this is where the real coverage is |
| `tests/Viu.Emporix.Tests/ProductAnyTypeTests.cs` | **Create.** Path tests for the seven group methods |
| `README.md` | **Modify.** A short section under Products |

The converter and the interface are separate files because they change for different reasons: the interface changes when the spec adds a product type, the converter when the discrimination rules change.

**Build order.** Task 1 defines the interface every later task references. Task 2's converter needs it. Task 3 is a refactor with the existing 33 `ProductServiceTests` as its net, and Task 4 cannot compile before it. Task 5 documents what exists.

---

## Task 1: The interface across the five types

**Files:**
- Create: `src/Viu.Emporix/EmporixProduct.cs`
- Test: `tests/Viu.Emporix.Tests/EmporixProductConverterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public interface IEmporixProduct` in namespace `Viu.Emporix.ProductModels`, with `string? Id { get; }`, `string? Code { get; }`, `ProductType? ProductType { get; }`. Implemented by `BasicProductWithId`, `BundleProductWithId`, `ParentVariantProductWithId`, `VariantProductWithId` and `DynamicVariantProductWithId`.

**Why a hand-written file rather than a generator change:** `partial` declarations in the same assembly attach the interface without touching `Generated/`. A consumer cannot do this from their own project — `partial` does not cross assemblies, and the declaration would create a colliding new type. That is why the interface has to live in the SDK.

- [ ] **Step 1: Write the failing test**

Create `tests/Viu.Emporix.Tests/EmporixProductConverterTests.cs`:

```csharp
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// Resolving a product response into the type its productType names.
/// </summary>
/// <remarks>
/// The specification declares a product read as a oneOf over five schemas with
/// no discriminator, so the generator picked one and every read returned
/// BasicProductWithId. productType is a usable discriminator in practice, which
/// is what makes this possible at all.
///
/// The converter is a pure function — JSON in, type out, no HTTP — which is why
/// the tests here carry weight. A stubbed handler would only confirm the same
/// expectation the code holds.
/// </remarks>
public class EmporixProductConverterTests
{
    [Fact]
    public void All_five_generated_types_carry_the_interface()
    {
        // The interface is what the facade returns, so every shape the
        // specification's oneOf lists has to implement it. A sixth type added
        // upstream would fail here rather than at a customer.
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(BasicProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(BundleProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(ParentVariantProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(VariantProductWithId)));
        Assert.True(typeof(IEmporixProduct).IsAssignableFrom(typeof(DynamicVariantProductWithId)));
    }

    [Fact]
    public void The_interface_reads_through_to_the_concrete_properties()
    {
        // DynamicVariantProductWithId declares Id, Code and ProductType
        // non-nullable, because the specification marks them required. An enum
        // and its Nullable are different types for interface implementation, so
        // that one needs explicit members — this checks they forward rather
        // than returning default.
        IEmporixProduct dynamic = new DynamicVariantProductWithId
        {
            Id = "d1",
            Code = "dyn",
            ProductType = ProductType.DYNAMIC_VARIANT,
        };

        Assert.Equal("d1", dynamic.Id);
        Assert.Equal("dyn", dynamic.Code);
        Assert.Equal(ProductType.DYNAMIC_VARIANT, dynamic.ProductType);

        IEmporixProduct basic = new BasicProductWithId { Id = "b1", Code = "plain" };

        Assert.Equal("b1", basic.Id);
        Assert.Equal("plain", basic.Code);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductConverterTests"`
Expected: compile failure — `IEmporixProduct` does not exist.

- [ ] **Step 3: Write the interface and the declarations**

Create `src/Viu.Emporix/EmporixProduct.cs`:

```csharp
namespace Viu.Emporix.ProductModels;

/// <summary>
/// What every product shape has in common, whatever its type.
/// </summary>
/// <remarks>
/// <para>
/// The specification declares a product read as a <c>oneOf</c> over five
/// schemas — basic, bundle, parent variant, variant and dynamic variant — with
/// no <c>discriminator</c> anywhere in the document. The generator therefore
/// resolved it to one alternative, and the reads on
/// <see cref="Viu.Emporix.ProductService"/> return that one for all five.
/// </para>
/// <para>
/// <c>productType</c> is a reliable discriminator in practice, which is what
/// makes resolving them possible. Reads that do so live on
/// <see cref="Viu.Emporix.ProductAnyTypeOperations"/>, reached through
/// <c>client.Products.AnyType</c>.
/// </para>
/// <para>
/// Deliberately three members. <c>Name</c>, <c>Description</c> and
/// <c>Mixins</c> sit on <c>ProductCore</c>, which
/// <see cref="DynamicVariantProductWithId"/> does not inherit — each would need
/// another forwarding member. Everything beyond these three is what the pattern
/// match is for.
/// </para>
/// </remarks>
public interface IEmporixProduct
{
    /// <summary>The product id.</summary>
    string? Id { get; }

    /// <summary>The product code, unique within the tenant.</summary>
    string? Code { get; }

    /// <summary>Which of the five shapes this is.</summary>
    ProductType? ProductType { get; }
}

// Attached through the generated classes' own partial declarations. Editing
// Generated/ would work until the next spec sync overwrote it.
public partial class BasicProductWithId : IEmporixProduct;

public partial class BundleProductWithId : IEmporixProduct;

public partial class ParentVariantProductWithId : IEmporixProduct;

public partial class VariantProductWithId : IEmporixProduct;

/// <remarks>
/// The specification marks this type's members required, so the generator
/// emitted them non-nullable. <c>ProductType</c> and <c>ProductType?</c> are
/// different types as far as implementing an interface goes, so the three
/// members are forwarded explicitly rather than matched implicitly.
/// </remarks>
public partial class DynamicVariantProductWithId : IEmporixProduct
{
    string? IEmporixProduct.Id => Id;

    string? IEmporixProduct.Code => Code;

    ProductType? IEmporixProduct.ProductType => ProductType;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductConverterTests"`
Expected: 2 passed.

If a type fails to satisfy the interface implicitly, do **not** widen the
interface — add explicit forwarding members to that type the way
`DynamicVariantProductWithId` has them, and note in a comment which
specification detail caused it.

- [ ] **Step 5: Record the public API**

Run: `./scripts/update-public-api.sh && dotnet build`
Expected: 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix/EmporixProduct.cs tests/Viu.Emporix.Tests/EmporixProductConverterTests.cs src/Viu.Emporix/PublicAPI.Unshipped.txt
git commit -m "feat: give the five product shapes a shared interface

The specification declares a product read as a oneOf over five schemas with no
discriminator, so the generator resolved it to one alternative. A shared
interface is the handle a resolving read can return.

Attached through the generated classes' partial declarations rather than by
editing them, since the next spec sync overwrites that directory. The dynamic
variant type needs explicit members because the specification marks its
members required, and an enum is not its own Nullable when implementing an
interface.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: The converter

**Files:**
- Create: `src/Viu.Emporix/EmporixProductConverter.cs`
- Modify: `src/Viu.Emporix/JsonContexts.cs`
- Modify: `tests/Viu.Emporix.Tests/EmporixProductConverterTests.cs`

**Interfaces:**
- Consumes: `IEmporixProduct` from Task 1.
- Produces: `internal sealed class EmporixProductConverter : JsonConverter<IEmporixProduct>`. Registered on `ProductJsonContext`, which gains `IEmporixProduct`, `List<IEmporixProduct>`, `BundleProductWithId`, `ParentVariantProductWithId`, `VariantProductWithId` and `DynamicVariantProductWithId` as serializable types.

- [ ] **Step 1: Write the failing tests**

Append to `EmporixProductConverterTests.cs`:

```csharp
    [Theory]
    [InlineData("BASIC", typeof(BasicProductWithId))]
    [InlineData("BUNDLE", typeof(BundleProductWithId))]
    [InlineData("PARENT_VARIANT", typeof(ParentVariantProductWithId))]
    [InlineData("VARIANT", typeof(VariantProductWithId))]
    [InlineData("DYNAMIC_VARIANT", typeof(DynamicVariantProductWithId))]
    public void Each_product_type_resolves_to_its_own_shape(string productType, Type expected)
    {
        IEmporixProduct? product = JsonSerializer.Deserialize(
            $$"""{"id":"p1","code":"c1","productType":"{{productType}}"}""",
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType(expected, product);
    }

    [Fact]
    public void A_bundle_exposes_its_bundled_products_typed()
    {
        // The point of the whole exercise: these fields were reachable only as
        // extension data before.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """
            {"id":"g1","code":"gift","productType":"BUNDLE",
             "bundledProducts":[{"productId":"p1","amount":2},{"productId":"p2","amount":1}]}
            """,
            ProductJsonContext.Default.IEmporixProduct);

        BundleProductWithId bundle = Assert.IsType<BundleProductWithId>(product);
        Assert.Equal(2, bundle.BundledProducts.Count);
        Assert.Equal("p1", bundle.BundledProducts[0].ProductId);
        Assert.Equal(2, bundle.BundledProducts[0].Amount);
    }

    [Fact]
    public void A_variant_exposes_its_parent_typed()
    {
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """{"id":"v1","code":"red-m","productType":"VARIANT","parentVariantId":"parent-42"}""",
            ProductJsonContext.Default.IEmporixProduct);

        VariantProductWithId variant = Assert.IsType<VariantProductWithId>(product);
        Assert.Equal("parent-42", variant.ParentVariantId);
    }

    [Fact]
    public void An_unknown_product_type_falls_back_to_the_basic_shape()
    {
        // Emporix has extended the list before. Throwing here would mean a new
        // product type upstream breaks every read at a customer, and the basic
        // shape carries every shared field plus extension data.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """{"id":"x1","code":"new","productType":"SOMETHING_NEW"}""",
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType<BasicProductWithId>(product);
    }

    [Fact]
    public void An_absent_product_type_falls_back_to_the_basic_shape()
    {
        // The specification does not require productType on variantProductWithId,
        // so this is a case Emporix is allowed to produce. Recorded as a
        // limitation in the design spec rather than guessed at from other fields.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """{"id":"v1","code":"red-m","parentVariantId":"parent-42"}""",
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType<BasicProductWithId>(product);
    }

    [Fact]
    public void A_nested_product_type_does_not_decide_the_shape()
    {
        // A dynamic variant carries a variants map whose entries have their own
        // productType. Scanning without a depth guard would let the inner value
        // win — and the first version of that guard was off by one, which made
        // every product resolve to the fallback with no error at all.
        IEmporixProduct? product = JsonSerializer.Deserialize(
            """
            {"id":"d1","code":"dyn","productType":"DYNAMIC_VARIANT",
             "variants":{"v1":{"productType":"VARIANT"}}}
            """,
            ProductJsonContext.Default.IEmporixProduct);

        Assert.IsType<DynamicVariantProductWithId>(product);
    }

    [Fact]
    public void A_mixed_list_resolves_every_element_on_its_own()
    {
        // The reason this is a converter rather than logic in the facade: lists
        // need no second code path.
        List<IEmporixProduct>? products = JsonSerializer.Deserialize(
            """
            [{"id":"b1","code":"plain","productType":"BASIC"},
             {"id":"g1","code":"gift","productType":"BUNDLE"},
             {"id":"p1","code":"shirt","productType":"PARENT_VARIANT"},
             {"id":"v1","code":"red-m","productType":"VARIANT"},
             {"id":"d1","code":"dyn","productType":"DYNAMIC_VARIANT"}]
            """,
            ProductJsonContext.Default.ListIEmporixProduct);

        Assert.Collection(
            products!,
            p => Assert.IsType<BasicProductWithId>(p),
            p => Assert.IsType<BundleProductWithId>(p),
            p => Assert.IsType<ParentVariantProductWithId>(p),
            p => Assert.IsType<VariantProductWithId>(p),
            p => Assert.IsType<DynamicVariantProductWithId>(p));
    }

    [Fact]
    public void Writing_through_the_interface_is_refused()
    {
        // Products are written through the concrete update types, whose field
        // sets differ from the response schemas. A writable converter would
        // suggest a read product can be sent back.
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(
            (IEmporixProduct)new BasicProductWithId { Id = "b1" },
            ProductJsonContext.Default.IEmporixProduct));
    }
```

Add `using System.Text.Json;` to the file's usings.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductConverterTests"`
Expected: compile failure — `ProductJsonContext.Default.IEmporixProduct` does not exist.

- [ ] **Step 3: Write the converter**

Create `src/Viu.Emporix/EmporixProductConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix;

/// <summary>
/// Reads a product response into the shape its <c>productType</c> names.
/// </summary>
/// <remarks>
/// <para>
/// A converter rather than logic inside the facade, for one reason: a mixed
/// list then costs nothing. <c>PaginatedItems&lt;IEmporixProduct&gt;</c> and
/// <c>List&lt;IEmporixProduct&gt;</c> work without a second code path.
/// </para>
/// <para>
/// <c>[JsonPolymorphic]</c> would be the idiomatic route and is not available
/// here: <c>productType</c> is both the natural discriminator and a declared
/// field on all five types, which System.Text.Json refuses. Using it would mean
/// hiding the field with <c>[JsonIgnore]</c> and inventing a base class for the
/// dynamic variant shape.
/// </para>
/// </remarks>
internal sealed class EmporixProductConverter : JsonConverter<IEmporixProduct>
{
    public override IEmporixProduct? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? productType = PeekProductType(reader);

        return productType switch
        {
            "BUNDLE" => Take(ref reader, ProductJsonContext.Default.BundleProductWithId),
            "PARENT_VARIANT" => Take(ref reader, ProductJsonContext.Default.ParentVariantProductWithId),
            "VARIANT" => Take(ref reader, ProductJsonContext.Default.VariantProductWithId),
            "DYNAMIC_VARIANT" => Take(ref reader, ProductJsonContext.Default.DynamicVariantProductWithId),

            // BASIC, an unknown value, and no value at all. Emporix has extended
            // the list before, and the specification does not require the field
            // on a variant — so this has to be a fallback rather than an error.
            // The basic shape carries every shared field plus extension data, so
            // nothing reachable is lost.
            _ => Take(ref reader, ProductJsonContext.Default.BasicProductWithId),
        };

        static IEmporixProduct? Take<T>(ref Utf8JsonReader reader, JsonTypeInfo<T> typeInfo)
            where T : IEmporixProduct
            => JsonSerializer.Deserialize(ref reader, typeInfo);
    }

    /// <summary>
    /// Finds <c>productType</c> without consuming anything.
    /// </summary>
    /// <remarks>
    /// <see cref="Utf8JsonReader"/> is a struct, so the copy scans ahead while
    /// the original stays where the real deserialization needs it.
    /// </remarks>
    private static string? PeekProductType(Utf8JsonReader peek)
    {
        // The reader arrives positioned on the value's StartObject, which the
        // loop below never observes — so the object we are already inside counts
        // as depth 1 from the start. Initialising this to 0 is silent: the guard
        // never matches, every product resolves to the fallback, and nothing
        // errors. That is how the first version of this method behaved.
        int depth = 1;

        while (peek.Read())
        {
            switch (peek.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    depth++;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (--depth == 0)
                    {
                        return null;
                    }

                    break;

                // Depth 1 only: a dynamic variant's «variants» map carries
                // entries with their own productType, and an inner value must
                // not decide the outer shape.
                case JsonTokenType.PropertyName
                    when depth == 1 && peek.ValueTextEquals("productType"):
                    return peek.Read() && peek.TokenType == JsonTokenType.String
                        ? peek.GetString()
                        : null;
            }
        }

        return null;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IEmporixProduct value,
        JsonSerializerOptions options)
        => throw new NotSupportedException(
            "A product is written through its concrete update type — BasicProductUpdate and its siblings. "
            + "The update schemas carry different fields from the response schemas, so a product that was "
            + "read cannot be sent back unchanged.");
}
```

- [ ] **Step 4: Register everything on the product context**

In `src/Viu.Emporix/JsonContexts.cs`, find the `ProductJsonContext` declaration.
Add the converter to its `JsonSourceGenerationOptions` and the six entries:

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(EmporixProductConverter)])]
// The resolving read returns the interface; the four types below never
// travelled over the wire before, because every read produced the basic shape.
[JsonSerializable(typeof(Viu.Emporix.ProductModels.IEmporixProduct))]
[JsonSerializable(typeof(List<Viu.Emporix.ProductModels.IEmporixProduct>))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.BundleProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.ParentVariantProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.VariantProductWithId))]
[JsonSerializable(typeof(Viu.Emporix.ProductModels.DynamicVariantProductWithId))]
```

Keep the existing `PropertyNamingPolicy` and `DefaultIgnoreCondition` values exactly as they are — read them off the file rather than copying from here, and only add the `Converters` line.

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~EmporixProductConverterTests"`
Expected: 14 passed — the theory counts as five.

If `Each_product_type_resolves_to_its_own_shape` fails for every case with the
basic shape, the depth counter is wrong. Check that it starts at `1`.

- [ ] **Step 6: Verify the AOT bar**

Run: `dotnet publish samples/Viu.Emporix.Sample --configuration Release`
Expected: 0 warnings. An `IL2026` or `IL3050` means the converter resolves a
type at runtime somewhere — every `Deserialize` call must take a
`JsonTypeInfo`, never a `Type`.

- [ ] **Step 7: Record the public API and commit**

```bash
./scripts/update-public-api.sh && dotnet build
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: resolve a product response by its productType

A JsonConverter that peeks at productType with a copied Utf8JsonReader and
defers to the concrete type's own JsonTypeInfo. A converter rather than facade
logic because a mixed list then needs no second code path.

An unknown or absent productType falls back to the basic shape rather than
throwing. Emporix has extended the list before, and the specification does not
require the field on a variant, so an exception here would break every read at
a customer for a value nobody controls.

The depth guard starts at 1 because the reader arrives on the value's
StartObject, which the scan never sees. At 0 the guard never matches, every
product resolves to the fallback, and nothing errors — which is how the first
version behaved.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
## Task 3: Make the four private cores generic

**Files:**
- Modify: `src/Viu.Emporix/ProductService.cs` — `GetAsync:88`, `GetByCodeAsync:111`, `ListPageAsync:483`, `SearchInChunksAsync:530`

**Interfaces:**
- Consumes: `IEmporixProduct` from Task 1, the context entries from Task 2.
- Produces: five generic cores, all **`internal`** — Task 4's group is a
  separate type and has to reach them, and `internal` keeps them off the public
  surface and out of the API baseline:
  - `GetOneCoreAsync<T>(string productId, JsonTypeInfo<T> typeInfo, AuthContext auth, CancellationToken cancellationToken)` → `Task<T?>`
  - `GetOneByCodeCoreAsync<T>(string code, JsonTypeInfo<List<T>> listTypeInfo, AuthContext auth, CancellationToken cancellationToken)` → `Task<T?>`
  - `ListPageAsync<T>(ProductPageOptions options, string? query, JsonTypeInfo<List<T>> listTypeInfo, AuthContext auth, CancellationToken cancellationToken)` → `Task<PaginatedItems<T>>`
  - `SearchByNameCoreAsync<T>(string term, ProductPageOptions? options, JsonTypeInfo<List<T>> listTypeInfo, AuthContext auth, CancellationToken cancellationToken)` → `Task<PaginatedItems<T>>`
  - `SearchInChunksAsync<T>(string field, IReadOnlyCollection<string> values, int chunkSize, JsonTypeInfo<List<T>> listTypeInfo, AuthContext auth, CancellationToken cancellationToken)` → `Task<IReadOnlyList<T>>`
- Also produces: `DefaultChunkSize` changed from `private const` to `internal
  const`, so the group's default parameter can name it rather than repeating
  the number.

**This is a refactor with a net.** `tests/Viu.Emporix.Tests/ProductServiceTests.cs` holds 33 tests over these methods. They must stay green without being edited — if one needs changing, the refactor changed behaviour and is wrong.

**Why generic cores rather than copying the methods into the new group:** the paths and query parameters are verified against the specification and scanned by `SpecPathTests`. Two copies can drift in a way no test catches — a query parameter present in one and not the other. One implementation, two type substitutions.

- [ ] **Step 1: Confirm the net is green before touching anything**

Run: `dotnet test --filter "FullyQualifiedName~ProductServiceTests"`
Expected: 33 passed. Write the number down; it must be identical at the end.

- [ ] **Step 2: Make the single-product read generic**

In `ProductService.cs`, replace the body of `GetAsync` and add the core below
it. The public signature does not change:

```csharp
    public Task<BasicProductWithId?> GetAsync(
        string productId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => GetOneCoreAsync(productId, ProductJsonContext.Default.BasicProductWithId, auth, cancellationToken);

    // The path and the auth default live here once. ProductAnyTypeOperations
    // passes a different JsonTypeInfo and gets the resolving read for free.
    private async Task<T?> GetOneCoreAsync<T>(
        string productId,
        JsonTypeInfo<T> typeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        return await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = $"{BasePath}/{Uri.EscapeDataString(productId)}",
                Auth = Anonymous(auth),
            },
            typeInfo,
            cancellationToken).ConfigureAwait(false);
    }
```

Keep the XML documentation on the public `GetAsync` exactly as it is — the
`<exception>` entries in particular, since the behaviour is unchanged.

Add `using System.Text.Json.Serialization.Metadata;` to the file's usings.

- [ ] **Step 3: Make the code lookup generic**

Replace the body of `GetByCodeAsync` and add its core:

```csharp
    public Task<BasicProductWithId?> GetByCodeAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => GetOneByCodeCoreAsync(code, ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);

    private async Task<T?> GetOneByCodeCoreAsync<T>(
        string code,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        List<T>? matches = await _http.SendAsync(
            new EmporixRequest
            {
                Method = HttpMethod.Get,
                Path = BasePath,
                Auth = Anonymous(auth),
                Query = [new("q", $"code:{code}")],
            },
            listTypeInfo,
            cancellationToken).ConfigureAwait(false);

        return matches is { Count: > 0 } ? matches[0] : default;
    }
```

Note `default` rather than `null` in the return: `T` is unconstrained, so
`null` does not compile.

- [ ] **Step 4: Make the paged read generic**

`ListPageAsync` is already private and already the single place all four list
methods go through. Add the type parameter and the `JsonTypeInfo` argument, and
leave everything else — the paging validation, the `X-Total-Count` header
handling, the query assembly — untouched:

```csharp
    private async Task<PaginatedItems<T>> ListPageAsync<T>(
        ProductPageOptions options,
        string? query,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        // body unchanged, except the last argument to SendPageAsync
    }
```

Inside it, the call becomes:

```csharp
        return await _http.SendPageAsync(
            new EmporixRequest { … },
            listTypeInfo,
            options.PageNumber,
            options.PageSize,
            cancellationToken).ConfigureAwait(false);
```

Then update the four call sites, which are `ListAsync`, `ListAllAsync` by way
of `ListAsync`, `SearchAsync` and `SearchByNameAsync`, to pass
`ProductJsonContext.Default.ListBasicProductWithId` as the new argument.

- [ ] **Step 5: Make the chunked search generic**

`SearchInChunksAsync` at line 530 gets the same treatment — a type parameter
and a `JsonTypeInfo<List<T>>` argument, with the chunking, the empty-collection
short circuit and the `q` assembly unchanged. Its two callers,
`GetManyByIdAsync` and `GetManyByCodeAsync`, pass
`ProductJsonContext.Default.ListBasicProductWithId`.

- [ ] **Step 6: Extract the name search into a core**

`SearchByNameAsync` cleans the term inline with two regular expressions before
delegating to `ListPageAsync`. Move that into a core of its own so the group
gets the same cleaning rather than a second copy of it:

```csharp
    public Task<PaginatedItems<BasicProductWithId>> SearchByNameAsync(
        string term,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => SearchByNameCoreAsync(
            term, options, ProductJsonContext.Default.ListBasicProductWithId, auth, cancellationToken);

    internal Task<PaginatedItems<T>> SearchByNameCoreAsync<T>(
        string term,
        ProductPageOptions? options,
        JsonTypeInfo<List<T>> listTypeInfo,
        AuthContext auth,
        CancellationToken cancellationToken)
    {
        // Body moved verbatim from the old SearchByNameAsync: the two regular
        // expressions, the whitespace collapsing and the query assembly. The
        // comment explaining why it replaces rather than drops belongs with it.
    }
```

Move the body unchanged. The comment about replacing rather than dropping
whitespace explains a real defect and travels with the code it explains.

- [ ] **Step 7: Make the five cores internal**

The group in Task 4 is a separate type, so `private` will not do. Change all
five to `internal`, and `DefaultChunkSize` from `private const` to `internal
const`. `internal` members do not enter the public API baseline, so Step 9
should still report no new symbols.

- [ ] **Step 8: Run the net**

Run: `dotnet test --filter "FullyQualifiedName~ProductServiceTests"`
Expected: 33 passed, the same number as Step 1, with no test file edited.

Run: `dotnet test`
Expected: everything green.

If a `ProductServiceTests` case fails, the refactor changed behaviour. Do not
adjust the test — find what moved. The most likely candidates are the auth
default (`Anonymous` versus `Service`) and the order of query parameters.

- [ ] **Step 9: Confirm the public surface did not move**

Run: `./scripts/update-public-api.sh`
Expected: «No missing entries» — this task adds no public API. If it reports
new symbols, a core was made `public` or `internal` by accident.

- [ ] **Step 10: Commit**

```bash
git add src/Viu.Emporix/ProductService.cs
git commit -m "refactor: make the product read cores generic over the element type

The four private methods that every read goes through now take a JsonTypeInfo
instead of hard-coding the basic shape. Public signatures and behaviour are
unchanged; the 33 existing tests pass untouched.

This exists so the resolving reads can share the paths and query parameters
rather than copy them. Those are verified against the specification and
scanned by SpecPathTests, and two copies could drift in a way no test would
catch.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: The AnyType operation group

**Files:**
- Modify: `src/Viu.Emporix/ProductService.cs`
- Test: `tests/Viu.Emporix.Tests/ProductAnyTypeTests.cs`

**Interfaces:**
- Consumes: the four generic cores from Task 3, `IEmporixProduct` from Task 1, the context entries from Task 2.
- Produces: `ProductService.AnyType` returning `ProductAnyTypeOperations`; that class with `GetAsync`, `GetByCodeAsync`, `ListAsync`, `SearchAsync`, `SearchByNameAsync`, `GetManyByIdAsync`, `GetManyByCodeAsync`, each mirroring the existing signature and returning `IEmporixProduct` in place of `BasicProductWithId`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Viu.Emporix.Tests/ProductAnyTypeTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viu.Emporix.ProductModels;

namespace Viu.Emporix.Tests;

/// <summary>
/// The reads that resolve the product type.
/// </summary>
/// <remarks>
/// These assert the addresses, not Emporix's behaviour — the group has to build
/// exactly what the plain methods build, since it goes through the same cores.
/// What the resolution itself does is covered by
/// <see cref="EmporixProductConverterTests"/>, where it is a pure function.
/// </remarks>
public class ProductAnyTypeTests
{
    private static ProductService Create(StubHttpMessageHandler handler)
    {
        IOptions<EmporixOptions> options = Options.Create(new EmporixOptions { Tenant = "acme" });

        return new ProductService(
            new EmporixHttpClient(new HttpClient(handler), options),
            options,
            NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task Get_addresses_the_same_path_as_the_plain_read()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """{"id":"g1","code":"gift","productType":"BUNDLE"}""");
        ProductService products = Create(handler);

        IEmporixProduct? product = await products.AnyType.GetAsync("g1");

        Assert.Equal("/product/acme/products/g1", handler.RequestUris[0].PathAndQuery);
        Assert.IsType<BundleProductWithId>(product);
    }

    [Fact]
    public async Task GetByCode_filters_by_code_and_resolves_the_shape()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK, """[{"id":"v1","code":"red-m","productType":"VARIANT"}]""");
        ProductService products = Create(handler);

        IEmporixProduct? product = await products.AnyType.GetByCodeAsync("red-m");

        Assert.Contains("q=code", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
        Assert.IsType<VariantProductWithId>(product);
    }

    [Fact]
    public async Task List_pages_the_same_way_and_resolves_each_element()
    {
        StubHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            [{"id":"b1","code":"plain","productType":"BASIC"},
             {"id":"g1","code":"gift","productType":"BUNDLE"}]
            """);
        ProductService products = Create(handler);

        PaginatedItems<IEmporixProduct> page = await products.AnyType.ListAsync();

        Assert.Contains("pageNumber=1", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
        Assert.Collection(
            page.Items,
            p => Assert.IsType<BasicProductWithId>(p),
            p => Assert.IsType<BundleProductWithId>(p));
    }

    [Fact]
    public async Task Search_passes_the_query_through()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.SearchAsync("productType:BUNDLE");

        Assert.Contains("q=productType", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchByName_reaches_the_same_endpoint()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.SearchByNameAsync("coffee");

        Assert.StartsWith("/product/acme/products", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetManyById_chunks_like_the_plain_read()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.GetManyByIdAsync(["a", "b"]);

        Assert.Contains("q=id", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetManyByCode_chunks_like_the_plain_read()
    {
        StubHttpMessageHandler handler = new(HttpStatusCode.OK, "[]");
        ProductService products = Create(handler);

        await products.AnyType.GetManyByCodeAsync(["x", "y"]);

        Assert.Contains("q=code", handler.RequestUris[0].PathAndQuery, StringComparison.Ordinal);
    }
}
```

Read `tests/Viu.Emporix.Tests/ProductServiceTests.cs` first and copy its
`Create` helper verbatim rather than the one above — the `ProductService`
constructor's parameter order and whether it takes a logger are what that file
already knows. This is the one place in this plan where you must look before
writing.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ProductAnyTypeTests"`
Expected: compile failure — `AnyType` does not exist.

- [ ] **Step 3: Add the property and the group**

At the top of `ProductService`, beside the other members, add:

```csharp
    /// <summary>Reads that resolve the product type instead of assuming BASIC.</summary>
    /// <remarks>
    /// The methods on this service return <see cref="BasicProductWithId"/> for
    /// every product, because the specification declares a product read as a
    /// <c>oneOf</c> over five schemas with no discriminator. That is right for a
    /// catalogue of plain products and wrong the moment a bundle or a variant
    /// appears. These reads return whichever shape <c>productType</c> names.
    /// </remarks>
    public ProductAnyTypeOperations AnyType => new(this);
```

At the end of the file, outside `ProductService`:

```csharp
/// <summary>
/// Product reads that resolve <c>productType</c> into its own generated type.
/// </summary>
/// <remarks>
/// <para>
/// Reached through <c>client.Products.AnyType</c>. Every method mirrors the one
/// on <see cref="ProductService"/> — same parameters, same defaults, same
/// addresses — and returns <see cref="IEmporixProduct"/> instead of
/// <see cref="BasicProductWithId"/>, so the caller can pattern match:
/// </para>
/// <code>
/// var product = await client.Products.AnyType.GetAsync(id);
///
/// if (product is BundleProductWithId bundle)
/// {
///     foreach (var item in bundle.BundledProducts) { }
/// }
/// </code>
/// <para>
/// <b>One limitation.</b> The specification does not require <c>productType</c>
/// on a variant, so a variant sent without it resolves to
/// <see cref="BasicProductWithId"/> and its <c>parentVariantId</c> is reachable
/// only through the extension data. Whether Emporix ever omits it is not
/// established; deriving the type from other fields would be guessing.
/// </para>
/// </remarks>
public sealed class ProductAnyTypeOperations
{
    private readonly ProductService _products;

    // Holds the service rather than the http client and tenant, because the
    // point of this group is to reuse its cores — the paths and query
    // parameters exist once, and this only substitutes the type.
    internal ProductAnyTypeOperations(ProductService products) => _products = products;

    /// <summary>Fetches a product by its id, as its own shape.</summary>
    /// <param name="productId">The product id.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IEmporixProduct?> GetAsync(
        string productId,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.GetOneCoreAsync(
            productId, ProductJsonContext.Default.IEmporixProduct, auth, cancellationToken);

    /// <summary>Fetches a product by its code, as its own shape.</summary>
    /// <param name="code">The product code.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IEmporixProduct?> GetByCodeAsync(
        string code,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.GetOneByCodeCoreAsync(
            code, ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);

    /// <summary>Lists products, each as its own shape.</summary>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> ListAsync(
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.ListPageAsync(
            options ?? new ProductPageOptions(),
            query: null,
            ProductJsonContext.Default.ListIEmporixProduct,
            auth,
            cancellationToken);

    /// <summary>Searches products with an Emporix <c>q</c> filter.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> SearchAsync(
        string query,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return _products.ListPageAsync(
            options ?? new ProductPageOptions(),
            query,
            ProductJsonContext.Default.ListIEmporixProduct,
            auth,
            cancellationToken);
    }

    /// <summary>Searches products by name.</summary>
    /// <param name="term">The search term.</param>
    /// <param name="options">Paging; the first page of 60 when omitted.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaginatedItems<IEmporixProduct>> SearchByNameAsync(
        string term,
        ProductPageOptions? options = null,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
        => _products.SearchByNameCoreAsync(
            term, options, ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);

    /// <summary>Fetches several products by id, as their own shapes.</summary>
    /// <param name="productIds">The ids.</param>
    /// <param name="chunkSize">How many ids per request.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<IEmporixProduct>> GetManyByIdAsync(
        IReadOnlyCollection<string> productIds,
        int chunkSize = ProductService.DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return _products.SearchInChunksAsync(
            "id", productIds, chunkSize,
            ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);
    }

    /// <summary>Fetches several products by code, as their own shapes.</summary>
    /// <param name="codes">The codes.</param>
    /// <param name="chunkSize">How many codes per request.</param>
    /// <param name="auth">What to authorise with; anonymous when omitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<IEmporixProduct>> GetManyByCodeAsync(
        IReadOnlyCollection<string> codes,
        int chunkSize = ProductService.DefaultChunkSize,
        AuthContext auth = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);

        return _products.SearchInChunksAsync(
            "code", codes, chunkSize,
            ProductJsonContext.Default.ListIEmporixProduct, auth, cancellationToken);
    }
}
```

Everything this class calls was made `internal` in Task 3 — the five cores and
`DefaultChunkSize`. If any of them is still `private`, Task 3 was left
incomplete; fix it there rather than widening it here, so the visibility change
travels with the refactor it belongs to.

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ProductAnyTypeTests"`
Expected: 7 passed.

Run: `dotnet test`
Expected: everything green, `ProductServiceTests` still at 33.

- [ ] **Step 5: Verify the AOT bar and record the API**

```bash
dotnet publish samples/Viu.Emporix.Sample --configuration Release   # 0 warnings
./scripts/update-public-api.sh && dotnet build
```

The baseline gains about twelve symbols: the property, the class, its seven
methods and the interface with its three members.

- [ ] **Step 6: Commit**

```bash
git add src/Viu.Emporix tests/Viu.Emporix.Tests
git commit -m "feat: add product reads that resolve the product type

client.Products.AnyType mirrors the seven reads on the service and returns
IEmporixProduct, so a bundle or a variant arrives as its own generated type and
can be pattern matched.

The existing methods keep their signatures. Returning the interface from them
would move 18 of 21 reachable fields behind a cast for every caller, to benefit
the ones reading bundles.

The group holds the service rather than the http client, so the paths and query
parameters stay in one place instead of being copied.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Document it and close the loop

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-03-product-type-resolution-design.md`
- Modify: `samples/Viu.Emporix.SmokeTest/Program.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: no API.

- [ ] **Step 1: Add a README section**

In `README.md`, immediately after the `## Products` section's examples, add:

```markdown
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
```

- [ ] **Step 2: Record the smoke-test gap**

In `samples/Viu.Emporix.SmokeTest/Program.cs`, find the product section and add
a comment beside it — no code, because the tenant may have no bundles:

```csharp
// Not covered here: whether Emporix sends productType on a VARIANT. The
// specification leaves it optional there, and the resolving reads on
// Products.AnyType fall back to the basic shape without it. Establishing this
// needs a tenant with variant products — read one back through
// Products.AnyType.GetAsync and check the returned type is
// VariantProductWithId rather than BasicProductWithId.
```

- [ ] **Step 3: Mark the open question as still open**

In the design spec's «Open questions» section, leave the text as it is and
append one line, so a later reader knows it was not forgotten:

```markdown
As of the implementation this is still open — the smoke test carries a comment
at the product section naming what to check and what a tenant with variants
would settle.
```

- [ ] **Step 4: Full verification**

```bash
dotnet build                                                          # 0 warnings
dotnet test                                                           # all green
dotnet publish samples/Viu.Emporix.Sample --configuration Release      # 0 AOT warnings
dotnet publish samples/Viu.Emporix.Storefront --configuration Release  # 0 AOT warnings
./scripts/update-public-api.sh                                        # no missing entries
```

- [ ] **Step 5: Commit**

```bash
git add README.md docs/superpowers/specs samples/Viu.Emporix.SmokeTest
git commit -m "docs: document the resolving product reads

A README section under Products, and a comment in the smoke test naming what
a tenant with variant products would settle — whether Emporix sends
productType when the specification does not require it.

The limitation is stated in the README rather than left for a caller to
discover: a variant sent without productType resolves to the basic shape, and
guessing from other fields is what this repository forbids.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## After the plan

The four follow-ups in the spec's «Follow-up work» section stay untouched:
typed writing, the `Anonymous` element type names, whether any other response
union has a usable discriminator, and an `AnyType` counterpart for
`ListAllAsync`. Two of them are already recorded as separate tasks.

The open question — `productType` on a variant — is the only thing this work
cannot settle on its own. It needs a tenant with variant products, and the
smoke test now says so at the place someone would look.

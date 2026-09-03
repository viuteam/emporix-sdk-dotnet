# Resolving the product type — Design

- **Date:** 2026-09-03
- **Status:** Approved (brainstorming) → ready for implementation plan
- **Affects:** `Viu.Emporix` — `ProductService`, `JsonContexts.cs`, one new file
- **Related:** [ADR-0001](../../adr/0001-type-generation.md) type generation,
  [ADR-0004](../../adr/0004-aot-trimming.md) AOT and trimming

## Problem

`specs/product.yml` declares `GET`, `PUT` and `PATCH` on
`/product/{tenant}/products/{productId}` as a `oneOf` over **five** schemas:

```yaml
oneOf:
  - basicProductWithId
  - bundleProductWithId
  - parentVariantProductWithId
  - variantProductWithId
  - dynamicVariantProductWithId
```

There is **no `discriminator`** — not in that operation, not anywhere in the
specification. NSwag therefore resolved the union to one alternative, and every
read on `ProductService` returns `BasicProductWithId`: `GetAsync`,
`GetByCodeAsync`, `ListAsync`, `SearchAsync`, `SearchByNameAsync`,
`GetManyByIdAsync`, `GetManyByCodeAsync`.

For a tenant whose catalogue is all `BASIC`, that is correct and nothing is
wrong. The moment a bundle or a variant appears, the caller receives an object
of the wrong class — with `productType` readable, so they can see it is wrong,
and no typed way to act on it.

## Verified groundwork

Everything here was measured against the specification or run in this
repository. Nothing in this section is inference.

### Nothing is lost today, but nothing is typed either

`ProductCore` carries `[JsonExtensionData] IDictionary<string, object>
AdditionalProperties`, and every one of the five types inherits or declares it.
Reading a bundle through `BasicProductWithId` therefore keeps its fields:

```
BUNDLE  → id=b1  productType=BUNDLE   extension data keys: bundledProducts
VARIANT → id=v1  productType=VARIANT  extension data keys: parentVariantId
```

They are reachable, as `JsonElement`, with the caller supplying the type to
deserialize into. What is missing is the typed path, not the data.

### All five generated types are field-complete

Compared against the specification with `allOf` chains resolved:

| Type | Spec fields | Generated | Extension data |
| --- | --- | --- | --- |
| `BasicProductWithId` | 21 | 21 | yes |
| `BundleProductWithId` | 22 | 22 | yes |
| `ParentVariantProductWithId` | 22 | 22 | yes |
| `VariantProductWithId` | 21 | 21 | yes |
| `DynamicVariantProductWithId` | 27 | 27 | yes |

No field missing, none extra. The type-specific fields are genuinely typed
rather than dropped to `object`: `BundledProducts`, `VariantAttributes`,
`VariantsMap`, `OwnVariantAttributes`, `ICollection<string>` for
`parentVariantPath`. Only `mixins` is `object?`, which is the mixin case and
not particular to these types.

**There are five, not four.** An earlier count in this work said four —
`ParentVariantProductWithId` exists and was missed by a search that was too
narrow.

### The inheritance is not uniform

| Type | Chain | `productType` |
| --- | --- | --- |
| `BasicProductWithId` | → Creation → Product → `ProductCore` | `ProductType?` |
| `BundleProductWithId` | → Creation → Product → `ProductCore` | `ProductType?` |
| `ParentVariantProductWithId` | → Creation → Product → `ProductCore` | `ProductType?` |
| `VariantProductWithId` | → Creation → Product → `ProductCore` | `ProductType?` |
| `DynamicVariantProductWithId` | **none** | `ProductType` |

The specification writes `dynamicVariantProductWithId` out in full instead of
composing it with `allOf`, and marks its members `required` — so the generator
emitted it standalone with non-nullable properties. That is specification
reality, not a generator artefact.

It does carry **all 13** `ProductCore` fields, so a `ProductCore` base would be
a restoration rather than an invention. It is still not the approach taken; see
below.

### `[JsonPolymorphic]` is ruled out

`productType` is the natural discriminator and also a declared field on every
type. `System.Text.Json` refuses that combination:

```
InvalidOperationException: The type 'BasicShape' contains property 'productType'
that conflicts with an existing metadata property name.
```

Using it would mean hiding `productType` with `[JsonIgnore]` on all five —
removing a field callers legitimately read — and inventing a base class for
`DynamicVariantProductWithId` on top. Two changes to generated code for a
mechanism that then reports less than it does now.

### `partial` does not cross assemblies

A consumer cannot attach an interface to the generated classes from their own
project: a `partial class` declaration in another assembly creates a new type
and collides with the imported one. The interface has to live in
`Viu.Emporix`. Found by trying it.

### The reader-peek approach works, and its failure mode is silence

A `JsonConverter` that copies the `Utf8JsonReader`, finds `productType`, and
defers to the concrete type's own `JsonTypeInfo` resolves all five — verified
under `IsAotCompatible`, both analyzers, warnings-as-errors and
`JsonSerializerIsReflectionEnabledByDefault=false`, with zero warnings:

```
single : BundleProduct  productType=BUNDLE
         bundled=1, first=p1 x2
list   : BASIC            BasicProduct           falls back to basic
list   : VARIANT          VariantProduct         parent=parent-42
list   : DYNAMIC_VARIANT  DynamicVariantProduct  sellable=True
list   : PARENT_VARIANT   ParentVariantProduct   is a parent
list   : SOMETHING_NEW    BasicProduct           falls back to basic
```

The first attempt initialised the depth counter to `0`. The reader arrives
positioned on the value's `StartObject`, which the scan loop never observes, so
top-level properties sat at depth 0 and the `depth == 1` guard never matched.
Every product resolved to the fallback type, and nothing errored. That is why
the counter starts at `1` and why the line carries a comment.

## Architecture — three units

### Unit 1 — The type layer

One new hand-written file, `src/Viu.Emporix/EmporixProduct.cs`. Nothing under
`Generated/` is touched, because the next spec sync overwrites it.

```csharp
public interface IEmporixProduct
{
    string? Id { get; }
    string? Code { get; }
    ProductType? ProductType { get; }
}

public partial class BasicProductWithId : IEmporixProduct;
public partial class BundleProductWithId : IEmporixProduct;
public partial class ParentVariantProductWithId : IEmporixProduct;
public partial class VariantProductWithId : IEmporixProduct;

// Required in the specification, so non-nullable in the generated type. An enum
// and its Nullable are different types for interface implementation, so the
// bridge is stated.
public partial class DynamicVariantProductWithId : IEmporixProduct
{
    string? IEmporixProduct.Id => Id;
    string? IEmporixProduct.Code => Code;
    ProductType? IEmporixProduct.ProductType => ProductType;
}
```

The four `allOf` types satisfy it implicitly.

**The interface stays at three members on purpose.** `Name`, `Description` and
`Mixins` live on `ProductCore`, which `DynamicVariantProductWithId` does not
inherit — each one would need another bridge line. Three are worth stating,
thirteen are not, and everything beyond them is what the pattern match is for.

### Unit 2 — The converter

`EmporixProductConverter : JsonConverter<IEmporixProduct>`, registered on
`ProductJsonContext`.

A converter rather than logic inside the facade, for one reason: a mixed list
then costs nothing. `PaginatedItems<IEmporixProduct>` and
`List<IEmporixProduct>` work without a second code path.

```csharp
Utf8JsonReader peek = reader;
int depth = 1;   // the object we are already inside; see «failure mode is silence»

while (peek.Read())
{
    if (peek.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) depth++;
    else if (peek.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { if (--depth == 0) break; }
    else if (depth == 1 && peek.TokenType == JsonTokenType.PropertyName
             && peek.ValueTextEquals("productType"))
    {
        peek.Read();
        kind = peek.GetString();
        break;
    }
}
```

Three decisions in it:

- **An unknown `productType` is not an error.** It falls back to
  `BasicProductWithId`. Emporix has extended the list before, and the basic
  shape carries every shared field plus extension data — the caller loses
  nothing reachable. Throwing would mean a new product type upstream breaks
  every read at a customer.
- **`depth == 1` excludes nested matches.** A `productType` inside `variants`
  or `bundledProducts` must not capture the decision. With a
  `DYNAMIC_VARIANT` carrying a `variants` map that is a real case.
- **`Write` throws `NotSupportedException`.** Products are written through the
  concrete update types. A writable converter would suggest a read product can
  be sent back, and it cannot: the update schemas carry different fields.

### Unit 3 — The facade

**The seven existing methods are unchanged.** Replacing their return type would
move 18 of 21 reachable fields behind a cast — `name`, `metadata`, `media`,
`mixins` among them — for every caller, including the majority reading only
`BASIC` products. The benefit accrues to bundle and variant callers; the cost
would fall on everyone.

A nested operation group instead, following `client.Iam.Groups`:

```csharp
public ProductAnyTypeOperations AnyType => new(_http, _tenant);
```

```csharp
var product = await client.Products.AnyType.GetAsync(id);

if (product is BundleProductWithId bundle)
{
    foreach (var item in bundle.BundledProducts) { … }
}
```

Seven methods on it, mirroring the existing set: `GetAsync`, `GetByCodeAsync`,
`ListAsync`, `SearchAsync`, `SearchByNameAsync`, `GetManyByIdAsync`,
`GetManyByCodeAsync`. Each is the existing method with a different
`JsonTypeInfo` — paths, query parameters, idempotency flags and auth defaults
copied verbatim, because they are already verified against the specification
and `SpecPathTests` keeps checking them.

Signatures are taken over unchanged: same parameter names, order and defaults.
Moving from `Products.GetAsync` to `Products.AnyType.GetAsync` changes the path
to the call and nothing else.

No `RS0026` risk: the methods sit on a different class, so there are no
overload pairs with optional parameters.

`ListAllAsync` stays on `BasicProductWithId`. It is one of the methods to
extend later, named here so it does not read as an oversight.

**Public API cost:** one property, one class, seven methods, one interface, one
converter — about twelve symbols. `./scripts/update-public-api.sh` afterwards.

## Testing

Unusually for this repository, the converter is a **pure function**: JSON in,
type out, no HTTP and no assumption about the API. That makes it the one place
where a unit test earns its keep, unlike a stubbed handler confirming the same
wrong expectation the code holds.

| Test | Catches |
| --- | --- |
| each of the five `productType` values → expected type | the switch |
| `BUNDLE` → `bundledProducts` readable typed | that the concrete type takes effect |
| unknown value → `BasicProductWithId` | the fallback |
| absent `productType` → `BasicProductWithId` | the case the specification allows for VARIANT |
| `productType` nested inside `variants` → ignored | the depth guard |
| mixed list → five different types | that lists need no extra code |

All against the real `ProductJsonContext`, not a test context — otherwise the
test exercises a configuration that is never shipped.

The fifth deserves its place: the depth bug would have passed a test that only
checked `BASIC`.

The seven facade methods each get a path test with `StubHttpMessageHandler`,
like the existing ones. They assert nothing about Emporix, only that the group
builds the same addresses as the original.

## Open questions

**Does Emporix actually send `productType` for a VARIANT?** The specification
does not require it there:

| Schema | `productType` |
| --- | --- |
| `basicProductWithId` | optional |
| `bundleProductWithId` | required |
| `parentVariantProductWithId` | required |
| `variantProductWithId` | **optional** |
| `dynamicVariantProductWithId` | required |

Optional on `BASIC` is harmless — the fallback is `BASIC`. Optional on VARIANT
is not: without the value, a variant resolves to the basic type and
`parentVariantId` is reachable only through extension data.

A heuristic on the presence of `parentVariantId` would be the obvious repair,
and it is exactly what this repository forbids: guessing. **This belongs in the
smoke test**, against a tenant with variant products, and until then in the XML
documentation of the group as a stated limitation.

## Follow-up work

Deliberately outside this design. Recorded here so the next piece of work
starts from what is already known rather than rediscovering it.

### 1. Typed writing is still asymmetric

`UpdateAsync` and `ReplaceAsync` take `BasicProductUpdate`, while
`BundleProductUpdate`, `ParentVariantProductUpdate`, `VariantProductUpdate` and
`DynamicVariantProductUpdate` all exist generated and unused. After this design
a bundle can be read typed but not written typed.

Its own scope, for three reasons: the update schemas carry different fields from
the response schemas, `PUT` and `PATCH` differ again in what they accept, and a
write is not repeatable — so the idempotency gate needs deciding per method
rather than copied. Start by reading the four `*Update` schemas against the
response schemas; the field sets are not the same and that difference is the
whole problem.

### 2. The `Anonymous` type names

`BundledProducts` is a `Collection<Anonymous>`. The element carries the right
fields — `productId` as `string`, `amount` as `int` — but the specification
defines it inline without a name, so NSwag called it `Anonymous`. There are
three such types in `Product.cs`: `Anonymous`, `Anonymous2`, `Anonymous3`.

It reads badly at the call site:

```csharp
foreach (Anonymous item in bundle.BundledProducts) { … }
```

Functionally complete, cosmetically poor. The fix belongs in
`tools/Viu.Emporix.SpecSync`'s `GeneratedCodeFixer`, which already renames
generator artefacts — not in the generated file, which the next sync
overwrites. Small and self-contained; worth doing right after this, because it
is the type a bundle caller touches first.

### 3. The same union pattern elsewhere

Twenty-six of the vendored specifications contain `oneOf`. Four properties are
already retyped to `JsonElement` by `LocalizedProperties.ReadUnions`, on the
stated grounds that «losing the other branches' fields silently is not an
option» — those four are provider configurations with no discriminator at all.

Products differ because `productType` is a usable discriminator in practice.
Whether any other response union has one has not been checked. If a second case
turns up, the converter here is the pattern to copy rather than to generalise
prematurely: one more converter is cheaper than an abstraction over two.

### 4. `ListAllAsync` and the remaining reads

`ListAllAsync` streams pages through `PaginatedItems.EnumerateAllAsync` over
`ListAsync`. An `AnyType` counterpart is mechanical once Unit 3 exists. Left
out here to keep the first version to one shape; add it when someone streams a
catalogue that contains bundles.

## Out of scope

- Polymorphic writing, and `[JsonPolymorphic]` on the generated types — ruled
  out above with the diagnostic.
- A base class for `DynamicVariantProductWithId`. Permissible, since it carries
  all 13 `ProductCore` fields, but `ProductCore` has no `Id` — a return type
  whose identity cannot be read without a cast moves the problem rather than
  solving it.
- Renaming the `Anonymous` types, which is follow-up 2.

# Typed writing per product type

**Date:** 2026-09-03
**Status:** design, approved for planning
**Follows:** [2026-09-03-product-type-resolution-design.md](2026-09-03-product-type-resolution-design.md), whose
«Follow-up work» section item 1 this design answers

## The problem

Reading a product now resolves into one of five generated types through
`client.Products.AnyType`. Writing one does not: all five write operations on
`ProductService` accept the **basic** shape only, while a full matrix of
generated types sits unused.

`specs/product.yml` declares every write body as a `oneOf` — except one, and
that exception is where the SDK is wrong rather than merely narrow:

| SDK method | Verb and path | What the specification declares | What the SDK sends |
| --- | --- | --- | --- |
| `CreateAsync` | `POST /products` | `productCreateBody`: `oneOf` over five `*Creation` | `BasicProductCreation` |
| `UpdateAsync` | `PATCH /products/{productId}` | `productPartialUpdateBody`: **one flat `productPartialUpdate`** | `BasicProductUpdate` |
| `ReplaceAsync` | `PUT /products/{productId}` | `productUpdateBody`: `oneOf` over five `*Update` | `BasicProductUpdate` |
| `CreateManyAsync` | `POST /products/bulk` | array of `oneOf` over five `*Creation` | `BasicProductCreation` |
| `UpdateManyAsync` | `PUT /products/bulk` | array of `oneOf` over five `*BulkUpdate` | `BasicProductBulkUpdate` |

The request-body definitions are at `specs/product.yml:1713` for creation,
`1926` for update, `2097` for the partial update, `2104` for bulk update and
`2136` for bulk creation. The SDK methods are at `ProductService.cs:401`, `433`,
`463`, `528` and `719`.

### PATCH is not per-type at all

`productPartialUpdate` is a single flat schema, and the generated
`ProductPartialUpdate : ProductCore` carries `Template`, `BundledProducts`,
`VariantAttributes` and `Metadata` — the union of what the type-specific shapes
add, so that a caller can patch a bundle's contents without a bundle-specific
type.

`BasicProductUpdate : BasicProduct : ProductCore` carries `Metadata` and
`ProductType`, and none of the other three.

Two consequences, stated precisely:

1. **`bundledProducts`, `variantAttributes` and `template` cannot be patched.**
   No property exists on the type the SDK sends. This is a capability the
   specification grants and the SDK withholds.
2. **`productType` travels in a body that does not declare it.** The generated
   documentation calls the field immutable and «taken into account only for
   insert operations». Emporix presumably ignores it; that is **unverified**.

What is *not* claimed: that existing PATCH calls fail. `BasicProductUpdate` and
`ProductPartialUpdate` both derive from `ProductCore`, so every field a caller
can reach today serializes identically. The fault is the missing capability plus
a body that does not match the declared schema.

### Why per-type writing matters less than per-type reading, and still matters

Reading needed resolution because the caller cannot know what arrives. Writing
is the opposite: the caller always knows they are creating a bundle. The gap is
therefore not «I get the wrong type» but «there is no way to say it» — a bundle
cannot be created with its `bundledProducts`, a variant cannot be created
against its parent, at all.

## Approach

Three marker interfaces plus three write converters — the mirror image of the
read side's `IEmporixProduct` and its converter, which refuses to write.

Two alternatives were weighed and rejected:

**Per-type nested groups** — `client.Products.Bundles.CreateAsync(…)`. This is
the house pattern and the most discoverable, but it costs five classes and about
twenty methods, and its bulk methods would be **strictly less capable than the
specification**: one type per call where Emporix accepts a mixed array. More
surface for less capability.

**A generic method with a static abstract type-info member** —
`CreateAsync<T>(T product, …) where T : IEmporixProductCreation<T>`, each
generated type declaring its own `static JsonTypeInfo<T>`. This buys
compile-time safety: a sixth product type would not build until it declared
one. But the mixed bulk array still needs the converter, so this adds generic
machinery on top of the chosen approach rather than instead of it.

Plain overloads are not available: five `CreateAsync` overloads all carrying
optional parameters trigger **RS0026**, which CLAUDE.md says to rename around
rather than suppress.

## Section 1 — The three interfaces and where they attach

Three interfaces in `Viu.Emporix.ProductModels`, attached by partial
declaration to the same five families the specification's `oneOf` lists:

| Interface | Attached to | Serves |
| --- | --- | --- |
| `IEmporixProductCreation` | the five `*Creation` | `POST`, `POST /bulk` |
| `IEmporixProductUpdate` | the five `*Update` | `PUT` |
| `IEmporixProductBulkUpdate` | the five `*BulkUpdate` | `PUT /bulk` |

**Marker interfaces, no members.** Unlike `IEmporixProduct`, nothing reads
through these: the caller holds the concrete type and the converter dispatches
on it. Members would add forwarding for nothing. All five creation types share
`ProductCore` in any case — including `DynamicVariantProductCreation`, which
derives from `DynamicVariantProduct : ProductCore`, unlike
`DynamicVariantProductWithId`, which has no base at all and is why the read
side needed explicit members.

Attached in a hand-written file, never by editing `Generated/`, for the reason
the read side established: the next spec sync overwrites that directory. A
consumer cannot attach them from their own project either — `partial` does not
cross assemblies.

### Two inheritance facts decide the converters' shape

The generated hierarchy is not disjoint:

- `BasicProductBulkUpdate : BasicProductUpdate` — the bulk types therefore
  **also** satisfy `IEmporixProductUpdate`
- `BasicProductWithId : BasicProductCreation` — four of the five **response**
  types therefore satisfy `IEmporixProductCreation`. `DynamicVariantProductWithId`
  is the exception, having no base

Both mean a caller can pass the wrong thing and have it compile: a product they
just read into `CreateAsync`, a bulk update into `ReplaceAsync`.

**Therefore the converters dispatch on exact runtime type, never on pattern
matching.** `value is BasicProductUpdate` matches a `BasicProductBulkUpdate`
too, and would serialize it through the base contract — silently dropping the
`id` the derived type adds, which for a bulk write is the field that says which
product to change. `value.GetType() == typeof(BasicProductUpdate)` cannot do
that.

Loud failure over silent field loss. The default branch throws, and its message
names the inheritance that let the call compile.

## Section 2 — The write converters

One file, `src/Viu.Emporix/EmporixProductWriteConverters.cs`, holding three
converters of the same shape:

```csharp
public override void Write(
    Utf8JsonWriter writer,
    IEmporixProductCreation value,
    JsonSerializerOptions options)
{
    // Exact type, not «is»: BasicProductWithId derives from
    // BasicProductCreation, and a pattern match would serialize it through the
    // base contract — dropping the fields it adds, with no error at all.
    Type type = value.GetType();

    if (type == typeof(BasicProductCreation))
    {
        JsonSerializer.Serialize(
            writer, (BasicProductCreation)value, ProductJsonContext.Default.BasicProductCreation);
    }
    else if (type == typeof(BundleProductCreation))
    {
        // … and so on for the remaining three
    }
    else
    {
        throw new NotSupportedException(/* names the five, and the inheritance */);
    }
}
```

**`Read` throws in all three.** These are request bodies; nothing in the API
returns them. The read side's converter throws on `Write` for the mirror-image
reason, so the pair reads consistently.

**Three converters rather than one generic base.** Each switch *is* one of the
specification's `oneOf` lists written out — which five schemas that endpoint
accepts. A shared abstraction with a type-info table would hide exactly that,
to save perhaps thirty lines.

**The pattern is already proven on this context.** `IEmporixProduct` is an
interface with a converter registered on `ProductJsonContext`, and
`List<IEmporixProduct>` deserializes through it under fourteen tests. Element
converters resolve through the options, which is what makes the mixed bulk array
fall out at no extra cost.

**`DefaultIgnoreCondition = WhenWritingNull` is what makes partial writing work
at all**, and it is already set on this context. A `PATCH` body that serialized
every unset property as `null` would clear fields the caller never mentioned.

The context grows by roughly nineteen `[JsonSerializable]` entries: the three
interfaces, `List<>` of the creation and bulk-update interfaces, the twelve
non-basic write types, `BasicProductBulkUpdate` as a single type — only its
`List<>` is registered today — and `ProductPartialUpdate`.

## Section 3 — The five signatures

```csharp
CreateAsync(IEmporixProductCreation product, …)                    // was BasicProductCreation
UpdateAsync(string productId, ProductPartialUpdate changes, …)     // was BasicProductUpdate
ReplaceAsync(string productId, IEmporixProductUpdate product, …)   // was BasicProductUpdate
CreateManyAsync(IReadOnlyCollection<IEmporixProductCreation> products, …)
UpdateManyAsync(IEnumerable<IEmporixProductBulkUpdate> products, …)
```

Verbs, paths, query parameters, idempotency flags and auth defaults all stay,
so `SpecPathTests` should not move.

**Four of the five keep existing callers compiling.** `BasicProductCreation`
converts to its interface implicitly, and `IReadOnlyCollection<out T>` and
`IEnumerable<out T>` are covariant, so a `List<BasicProductCreation>` still
binds.

**`UpdateAsync` is a real source break.** `BasicProductUpdate` and
`ProductPartialUpdate` are unrelated classes, so every existing `PATCH` call
needs editing.

The break is taken rather than avoided. Keeping the old signature would keep
sending a schema the specification does not declare, and an `[Obsolete]`
overload beside the new one would trip **RS0026** and need a renamed sibling —
carrying a defect forward under a worse name.

**Release shape:** `feat!`, which below 1.0 is a minor bump. The public API
baseline takes five removals; `scripts/promote-public-api.sh` handles those
since the `*REMOVED*` gap was fixed, and that fix was made for exactly this
case.

## Section 4 — Testing

**The converters are pure functions, so this is where real coverage lives.**
In `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs`:

1. A theory over all fifteen types: each serializes through its interface into
   its own shape
2. Round-trip a `BundleProductCreation` carrying `bundledProducts` — out
   through the interface, back through the concrete type, fields intact. The
   no-silent-field-loss proof
3. **The trap test.** A `BasicProductBulkUpdate` handed to the
   `IEmporixProductUpdate` converter throws instead of dropping its `id`. This
   is the highest-value test here: it is the entire reason for exact-type
   dispatch
4. **The second trap.** A `BasicProductWithId` handed to the creation converter
   throws, and the message names the inheritance that let it compile
5. A mixed `List<IEmporixProductBulkUpdate>` — five types, one array, each
   element in its own shape
6. Unset properties emit no `null`, so a `PATCH` body clears nothing the caller
   did not mention
7. `Read` throws on all three converters
8. Through the service: `UpdateAsync` sends a body containing `bundledProducts`
   — the capability that does not exist today

**What none of this catches is whether Emporix accepts the bodies.** Of this
SDK's roughly two dozen known defects, about half came from reading
specifications against the code and most of the rest from live calls; none from
a unit test. A stubbed handler asserts the same body the code builds.

**Exactly one existing test line changes:** `ProductServiceTests.cs:368`,
`new BasicProductUpdate()` becomes `new ProductPartialUpdate()`. The other seven
write call sites in that file bind through the covariant interfaces unchanged,
and no sample project touches a product write.

## Files

| File | |
| --- | --- |
| `src/Viu.Emporix/EmporixProductWrite.cs` | create — three interfaces, fifteen partial declarations |
| `src/Viu.Emporix/EmporixProductWriteConverters.cs` | create — the three converters |
| `src/Viu.Emporix/JsonContexts.cs` | modify — about nineteen entries, three converters |
| `src/Viu.Emporix/ProductService.cs` | modify — five signatures and their XML documentation |
| `tests/Viu.Emporix.Tests/EmporixProductWriteConverterTests.cs` | create |
| `tests/Viu.Emporix.Tests/ProductServiceTests.cs` | modify — one line |
| `README.md` | modify — per-type write examples and the `UpdateAsync` migration note |

The interfaces and the converters are separate files because they change for
different reasons: the interfaces when the specification adds a product type,
the converters when the dispatch rules change.

## Out of scope

- **A resolving read for writes.** There is nothing to resolve: the caller has
  the concrete type.
- **Per-type nested groups.** Rejected above; if discoverability turns out to
  matter more than the surface cost, groups can be added later over the same
  interfaces without another break.
- **`ProductPartialUpdate` per type.** The specification declares one flat
  schema for `PATCH`. Splitting it per type would be inventing.
- **The other services' write unions.** Twenty-six specifications contain
  `oneOf`; whether any other write body is one is unchecked.

## Open questions

**Does `PATCH` reject `productType` or ignore it?** The field travels in a body
that does not declare it. Emporix presumably ignores it, and after this design
the SDK stops sending it — but knowing which would say whether today's callers
were ever affected. This needs a deliberate write against a scratch tenant; the
smoke test's seller-side pass is read-only by design.

**Does Emporix accept a mixed array on `PUT /products/bulk`?** The
specification's `oneOf` inside `items` says yes. No call has been made. This is
the one capability of this design that rests on the specification alone.

## Follow-up work

Recorded so the next piece of work starts from what is known.

1. **A write path for the smoke test.** Two of the open questions above need one
   deliberate write against a scratch tenant. The smoke test is read-only on the
   seller side, which is the right default — this would be an opt-in step behind
   its own environment variable.
2. **Unknown enum values still break reads.** Carried over from the read-side
   design: NSwag emits the enum converter as a property-level attribute, so a
   `productType` the vendored specification does not list makes the read throw.
   The fix belongs in a `SpecSync` `GeneratedCodeFixer` rule and affects every
   enum in every specification.
3. **The `Anonymous` element type names.** `BundledProducts` is a
   `Collection<Anonymous>`, so a caller creating a bundle writes
   `new Anonymous { ProductId = …, Amount = … }`. That is now the first type a
   bundle *writer* touches, not just a reader — which raises this from cosmetic.
4. **`ListAllAsync` and `ListVariantsAsync` have no `AnyType` counterpart.**
   Mechanical once the group exists; unchanged by this design.

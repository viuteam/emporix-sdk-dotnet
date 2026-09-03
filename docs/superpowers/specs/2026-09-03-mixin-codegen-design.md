# Typed Emporix mixins for .NET — Design

- **Date:** 2026-09-03
- **Status:** Approved (brainstorming) → ready for implementation plan
- **Affects:** `Viu.Emporix` (new public surface), `Viu.Emporix.MixinSync` (new package), `publish.yml`
- **Ports:** `@viu/emporix-mixins` 1.0.0 from the Node SDK
- **Related:** [ADR-0001](../../adr/0001-type-generation.md), [ADR-0004](../../adr/0004-aot-trimming.md),
  [`docs/analysis.md` §6](../../analysis.md)

## Problem

An Emporix **mixin** is a set of tenant-defined properties stored under
`entity.mixins.<key>`, described by a JSON Schema hosted at a URL, with that URL
recorded in `entity.metadata.mixins.<key>`. Emporix assigns a new schema version
on every change, so a consumer can only observe versions, never predict them.

The .NET SDK types this as `object?` in 106 places (and, inconsistently, as
`IDictionary<string, string>?` in 14 more — the same `metadata.mixins` concept,
modelled two ways by two specifications). Two consequences, both measured:

1. **Only `JsonElement` can be written.** Assigning a POCO or an anonymous
   object to `object? Mixins` throws `NotSupportedException` at runtime, because
   a source-generated context has no resolver for the runtime type. This is not
   AOT-specific: it fails with reflection enabled too, so there is no hidden
   trap — but there is also no ergonomic path.
2. **No drift signal.** Nothing tells a consumer that the tenant's schema moved
   from v6 to v7 while their code still assumes v6.

This design adds typed read/write access, a type-safe `q` filter builder, and a
CLI that generates the types and detects version drift.

## Verified groundwork

Everything below was reproduced in this repository before the design was fixed.
The probes are throwaway; the findings are not.

### The AOT bar holds

A probe implementing the full runtime — descriptor, reader, writer, filter —
compiles under the repository's own settings (`IsAotCompatible`,
`EnableTrimAnalyzer`, `EnableAotAnalyzer`, `TreatWarningsAsErrors`) with **zero
warnings**, publishes to a native Mach-O arm64 binary with **zero ILC
warnings**, and produces identical output when run natively.

Two reasons it holds: `JsonTypeInfo<T>` on the descriptor replaces all
polymorphic serialization, and the expression selector is only asked for
`MemberExpression.Member.Name` — a name already present in the tree. No
`.Compile()`, no metadata lookup a trimmer could remove.

### A source generator cannot do this

`docs/analysis.md` §6 currently proposes a Roslyn source generator. It does not
work, and the failure is structural rather than an effort question:

| Approach | Result |
|---|---|
| `RegisterPostInitializationOutput` emits the context | works — but cannot read files |
| `RegisterSourceOutput` from `AdditionalFiles` | `CS0534` — the STJ generator never sees it |
| Generator emits only the type, context hand-written | `SYSLIB1030` — no metadata generated |

Generators do not observe each other's output, and the only phase that can read
schema files is the phase `System.Text.Json` no longer processes. **That section
of `analysis.md` needs correcting as part of this work.**

### Three collisions the generator must handle

| Collision | Symptom | Resolution |
|---|---|---|
| Nested types across mixins | two `partial class Note` — same member gives `CS0102`, differing members **merge silently** | one namespace per mixin |
| One shared serializer context | `SYSLIB1031`, an error under warnings-as-errors | one context per mixin |
| Attribute names normalising alike | `x-custom` + `xCustom` → both `XCustom` → `CS0102` | detect at `generate`, report, refuse |

The third was found by probing, not by reasoning: NJsonSchema appends no
disambiguating suffix, so a tenant with both keys receives code that does not
compile, with a diagnostic that names no Emporix concept.

**Correction, made while implementing.** The first row originally read
`CS0101`. It does not: NJsonSchema emits `partial` classes, so two same-named
nested types in one namespace merge rather than clash. Identical members then
give `CS0102`, and differing members compile into a type carrying both mixins'
fields. That makes the namespace split more important than first stated — the
failure mode is silence, not a build error. Found by writing the compilation
test, which is the point of having it.

### The generator already exists

`NJsonSchema.CodeGeneration.CSharp` used **as a library** (not the `nswag` CLI
that `SpecSync` shells out to) produces the same shape of type the SDK already
ships — `System.Text.Json` attributes, nullable reference types, enums — and
gives per-mixin namespace control, which is what resolves collision #1. No
process launch, and no dependency on a tool manifest in the consumer's repo.

### compoundLogicalQuery is per service, not per entity

Confirmed by the Emporix documentation
([q-param](https://developer.emporix.io/api-references/standard-practices/q-param))
and by the Node SDK's per-method flags: the operator is accepted by **Approval,
Audit Logs, Availability, Product, Quote and Schema**, and by no other service.

This invalidated an earlier decision in this design to derive the capability
from the descriptor's entity: `Entity = "PRODUCT"` does not imply the caller is
invoking `Products.SearchAsync`.

## Architecture

Two artefacts on one version line.

```
src/Viu.Emporix/Mixins/          (existing package, ~400 new lines)
  MixinDescriptor.cs             descriptor carrying JsonTypeInfo<T>
  MixinReader.cs                 Read, SavedVersion
  MixinWriter.cs                 Values, SchemaUrls
  MixinQuery.cs                  builder, conditions, filters, capability gate

src/Viu.Emporix.MixinSync/       (new, dotnet tool)
  Program.cs                     pull | generate | check
  SchemaSource.cs                Schema Service → RawMixin[]
  AttributeSchema.cs             attributes[] → JSON Schema (fallback)
  Generator.cs                   NJsonSchema → types, contexts, registry
  Lockfile.cs                    version + url + sha256
```

**Build order.** The units are not independent: Unit 2's builder reads
`Attributes` off the descriptor, and Unit 3 generates code against both. So Unit
1, then Unit 2, then Unit 3 — and Unit 3's structural test cannot exist before
Unit 1 compiles.

**One version line, not two Release Please packages.** `Directory.Build.props`
states «there is exactly one packable project, and a second props layer with
`IsPackable` conditions would be more structure than benefit». A second release
package would need components in tags — the mechanism that already cost this
repository a silent release failure (PR #6). So: `release-please-config.json`
unchanged, `publish.yml` packs two projects, one tag covers both.

**Three deliberate deviations for the tool**, carried in its own csproj the way
`SpecSync` does, rather than as conditions in the shared props:

| Shared rule | Tool |
|---|---|
| `IsAotCompatible=true` | off — NJsonSchema is not AOT-compatible |
| package metadata only in the core csproj | own `PackAsTool`, `ToolCommandName`, `PackageId` |
| zero runtime dependencies | still holds for the core; the tool takes NJsonSchema + Newtonsoft |

The last one is why the tool cannot live in the core package: Newtonsoft.Json in
`Viu.Emporix` would break a stated goal.

## Unit 1 — Runtime (in the core package)

### The descriptor

```csharp
public sealed class MixinDescriptor<T>
{
    public required string Key { get; init; }        // "deliveryOptions"
    public required string Entity { get; init; }     // "PRODUCT" — informational
    public required string Url { get; init; }        // → metadata.mixins[key]
    public required int Version { get; init; }       // 6
    public required JsonTypeInfo<T> TypeInfo { get; init; }
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}
```

`TypeInfo` is the AOT bridge and the reason the whole path is reflection-free:
the consumer's own `JsonSerializerContext` supplies it, the SDK never resolves a
type at runtime. `Attributes` maps CLR property name to JSON attribute name so
the filter can resolve a selector without reading metadata.

`Entity` carries the schema's own `types[]` value. It does **not** drive the
capability gate — that was an earlier decision here, invalidated by the
documentation saying the capability is per service. It is kept because it makes
the generated registry readable and lets an error message name where a mixin
belongs; one schema assigned to several entity types yields one descriptor each,
as in the Node SDK.

### Reading and writing

```csharp
MixinReader.Read(product.Mixins, Mixins.Delivery)              // T?
MixinReader.SavedVersion(product.Metadata.Mixins, "key")       // int?
MixinWriter.Create().Set(Mixins.Delivery, value)               // .Values + .SchemaUrls
```

`Read` takes the property value, not the entity: C# is nominally typed, the
generated classes share no interface, and 120 mixin sites are modelled two
different ways. Handing in `product.Mixins` works everywhere and needs no change
to generated code.

`SavedVersion` needs two overloads for the same reason — `IDictionary<string,
string>?` for the 14 sites typed that way, `object?` for the rest. Both
parameters are required, so `RS0026` does not apply.

`MixinWriter` returns the two halves separately, and the caller assigns both:

```csharp
var w = MixinWriter.Create().Set(Mixins.Delivery, new DeliveryMixinV6 { Packaging = "Paper" });
product.Mixins          = w.Values;      // {"deliveryOptions":{"packaging":"Paper"}}
product.Metadata.Mixins = w.SchemaUrls;  // deliveryOptions → …MixIn.v6.json
```

Two statements rather than one. The alternative — normalising `object? Mixins` to
`JsonElement?` across 106 sites and attaching an `IMixinCarrier` interface —
would give single-statement writes and the full Node ergonomics, at the cost of
a breaking change to the core's public API. Rejected for now; revisit if the
forgotten-metadata mistake actually shows up in use.

**Known gap:** nothing makes the caller write the second line. Node's
`writeMixin` sets both, and its design calls the metadata half «the part
consumers get wrong». The mitigation is documentation and the version warning on
read, not the type system.

## Unit 2 — The q filter (in the core package)

### Conditions are categorised, not generic

The obvious signature does not compile:

```csharp
// CS0411 — with d.Weight (double?), C# cannot tell whether TValue is double
// under Nullable<> or is itself nullable. Inference runs before constraints,
// so `where TValue : notnull` does not rescue it.
Where<TValue>(Expression<Func<T, TValue?>> selector, MixinCondition<TValue> condition)
```

So conditions carry a category and the overload set resolves purely from the
selector's return type:

```csharp
Where(Expression<Func<T, string?>> s, TextCondition   c);
Where(Expression<Func<T, double?>> s, NumberCondition c);
Where(Expression<Func<T, int?>>    s, NumberCondition c);
Where(Expression<Func<T, bool?>>   s, BoolCondition   c);
Where<TAttr>(Expression<Func<T, TAttr?>> s, AnyCondition c);          // exists / missing
WhereEnum<TEnum>(Expression<Func<T, TEnum?>> s, TEnum v) where TEnum : struct, Enum;
WhereLocalized<TAttr>(Expression<Func<T, TAttr?>> s, string lang, TextCondition c);
```

Type gating survives — `Is.AtLeast(10)` yields a `NumberCondition` and fits no
`string?` selector — while inference stays trivial. `WhereLocalized` is a
separate name, not an overload: a localized attribute is an object of language
keys, so its selector type says nothing about the compared value, and as an
overload it was ambiguous.

### The capability gate is enforced by the compiler

`Or()` returns a different type, and that type's `Build()` requires a target.
Deliberately **not** a subclass — inheriting `Build()` would let the gate be
skipped silently. Verified: `CompoundMixinFilter` exposes exactly one `Build`
method and no parameterless one.

```csharp
MixinQuery.For(Mixins.Delivery)
    .Where(d => d.Packaging, Is.EqualTo("Paper"))
    .Build();                                     // string — every q endpoint

a.Or(b).Build(EmporixQuery.ProductSearch);        // string
a.Or(b).Build(EmporixQuery.CategorySearch);       // throws
a.Or(b).Build();                                  // does not compile
```

`EmporixQuery` exposes one value per verified endpoint: `ProductSearch`,
`AvailabilitySearch`, `QuoteSearch`, `ApprovalSearch`, `SchemaSearch`,
`AuditLogSearch` (compound accepted) and `CategorySearch`, `OrderList`,
`VendorSearch`, `CustomerAdminSearch` (rejected). No value is added without a
source.

`Build()` returns `string`, which the existing `string query` parameters take
unchanged — no new overloads on ~15 service methods, and no `RS0026`.

### Values carrying whitespace are refused

The q DSL separates clauses with spaces and the Node SDK records its escaping as
unverified. A value containing whitespace throws rather than being mangled; the
escape hatch is `MixinFilter.Raw`.

## Unit 3 — The tool

```bash
dotnet tool install --global Viu.Emporix.MixinSync

emporix-mixins pull        # Schema Service → snapshot.json + mixins.lock.json
emporix-mixins generate    # snapshot.json → .cs files
emporix-mixins check       # live vs. lockfile; exit 1 on drift
```

`generate` reads the committed snapshot, never the network, so builds stay
offline and deterministic — and a consumer maintaining schemas themselves writes
the snapshot and skips `pull`. That is why only one source is needed: Node's
`localFiles` adapter comes free from this split.

### Configuration

`emporix-mixins.json`, with credentials from the environment as the smoke test
already does (`EMPORIX_BACKEND_CLIENT_ID`, `EMPORIX_BACKEND_SECRET` — the Schema
Service is seller-side). Nothing secret in the file.

```json
{
  "tenant": "acme",
  "namespace": "Acme.Mixins",
  "out": "src/Acme.Shop/Mixins/Generated",
  "lockfile": "src/Acme.Shop/Mixins/mixins.lock.json"
}
```

### What is generated

Per mixin: one file, its own namespace, its own serializer context. Plus one
registry binding it all to Unit 1.

```csharp
// AUTO-GENERATED by Viu.Emporix.MixinSync — do not edit.
public static class Mixins
{
    public static readonly MixinDescriptor<DeliveryOptions.DeliveryOptionsMixinV6> DeliveryOptions = new()
    {
        Key = "deliveryOptions", Entity = "PRODUCT", Version = 6,
        Url = "https://cdn.emporix.io/…/deliveryOptionsMixIn.v6.json",
        TypeInfo = DeliveryOptions.DeliveryOptionsContext.Default.DeliveryOptionsMixinV6,
        Attributes = new Dictionary<string, string> { ["Packaging"] = "packaging", … },
    };
}
```

Each generated context sets `DefaultIgnoreCondition = WhenWritingNull`. Without
it the writer emits `"note":null` into a schema declaring
`additionalProperties: false` — unnecessary payload and unnecessary risk. Found
by running the probe, not by reading it.

The `Attributes` table is **parsed out of the generated code**, not recomputed.
`ConversionUtilities.ConvertToUpperCamelCase` matched the emitted name in all
six probed cases including the awkward ones (`x-custom` → `XCustom`, `2ndChoice`
→ `_2ndChoice`), but the Node package already tripped here («reference the name
it ACTUALLY emitted»), and the collision case proves computed and emitted names
can diverge. Parsing is correct by construction and costs one regex.

### The lockfile

Shaped after `SyncManifest`, `SortedDictionary` for legible diffs:

```json
{ "generatedAt": "2026-09-03T…", "mixins": {
  "deliveryOptions": { "entity": "PRODUCT", "version": 6,
                       "url": "…v6.json", "sha256": "a1b2c3d4e5f60718" } } }
```

The hash covers schema content, not just the version: Emporix can change a
schema without raising the version, and then the hash is the only signal.

### The fallback

When `metadata.url` cannot be fetched, `attributes[]` is converted to JSON
Schema across all eleven `SchemaAttributeType` values (`TEXT NUMBER DECIMAL
BOOLEAN DATE TIME DATE_TIME ENUM ARRAY OBJECT REFERENCE`). More type-safe than
the Node equivalent, which compares strings. `REFERENCE` maps to `string` (a
reference is an id); `OBJECT` recurses.

### The drift workflow

Copied into the consumer's repository — and the actual argument for the tool:

```yaml
on: { schedule: [{ cron: "0 6 * * *" }], workflow_dispatch: {} }
# pull && generate → peter-evans/create-pull-request
```

A raised schema version arrives as a pull request with the type diff beside it.
That is the part nobody does by hand. Generating classes for three mixins is ten
minutes of typing; noticing that Emporix moved v6 to v7 is not.

## Testing

No defect in this SDK has ever been found by a unit test (CLAUDE.md, and the
wave-by-wave tallies in `docs/roadmap.md`). So the tests here target pure
functions, where they can actually fail for a real reason:

| Test | Catches |
|---|---|
| q renderer per condition kind | grammar formatting |
| gate: `CategorySearch` + compound | the verified service list |
| whitespace guard | a silently mangled value |
| `SavedVersion`: `.v6.json` → 6; no marker → `null` | parsing edges |
| lockfile diff: version, url, hash, added, removed | drift detection itself |
| `attributes[]` → JSON Schema, all eleven types | fallback gaps |

**The one structural test**, playing the role `SpecPathTests` plays for paths:
the generator runs over fixture schemas and the result is compiled in-memory
with Roslyn. If it does not compile, the test fails. One fixture with two mixins
that both declare `note`, plus one with `x-custom` and `xCustom`, covers all
three collisions without asserting any of them individually.

Risk: Roslyn in a test needs reference assemblies. If that breaks the «under a
second» test run, it becomes a separate trait rather than part of the default
pass.

## Open questions

Five q forms are taken from the Node implementation and are **not** verified
against a live tenant. Three of them Node flags itself:

| Open | Source |
|---|---|
| `q` escaping for whitespace | Node: «the safe `q` escaping is unverified» → hence the guard |
| `exists` / `missing` at attribute path | Node: «confirm attribute-level semantics on your tenant» |
| localized path `mixins.<key>.<attr>.<lang>` | Node: «should be confirmed against your tenant» |
| range syntax `(>=1 AND <=5)` | from the Node code; no tenant evidence |
| whether `metadata.mixins` must be sent on PATCH | Node always sends it; whether Emporix maintains it is unchecked |

The route to closing them is the only one with a track record here: extend
`samples/Viu.Emporix.SmokeTest` with a mixin pass once a tenant with mixins is
available. Until then they stay as comments in the code, beside the other live
findings.

Three items outside the code:

1. **Package id availability unchecked** (no network access when this was
   written). Before implementing:
   `curl -s -o /dev/null -w "%{http_code}" https://api.nuget.org/v3-flatcontainer/viu.emporix.mixinsync/index.json`
   — 404 means free. The `Viu.*` prefix is not reserved.
2. **`publish.yml` packs `src/Viu.Emporix` only** and needs a second `dotnet
   pack`. The NuGet trusted-publishing policy is unaffected: it names
   `publish.yml`, which remains the workflow containing the job.
3. **`docs/analysis.md` §6 recommends the source-generator approach** and must
   be corrected, whether or not this design is built.

## Out of scope (YAGNI)

- Runtime JSON Schema validation. `ajv` has no free .NET counterpart, the core
  package carries no runtime dependencies, and generated types already enforce
  `required` and `enum` — most of what Node needs ajv for.
- Terraform and CDN source adapters; an extension point for custom sources.
- q operators beyond the mixin cases (`elemMatch` and friends).
- Normalising `object? Mixins` to `JsonElement?` across the generated types.

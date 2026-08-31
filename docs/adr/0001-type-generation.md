# ADR-0001 — Type generation

**Status:** Proposed · **Date:** 2026-08-31 · Supersedes: — · Affects: [ADR-0004](0004-aot-trimming.md)

## Context

43 Emporix OpenAPI specifications have to become C# types. The Node SDK generates
**types only** with `@hey-api/openapi-ts` and writes the client by hand — because
the facades do things no generator supplies: an auth context per call, pagination
assembled from response headers, YRN construction, query-DSL validation, chunking.

Constraints: AOT compatibility is settled ([ADR-0004](0004-aot-trimming.md)), the
dependency budget is «as little as possible», and we want control over the public
API surface.

## Options

Rather than speculate, I measured against `specs/product.yml` (3,869 lines, the
most complex specification, with `oneOf` and `allOf` chains).

| Option | Result | Dependencies |
| --- | --- | --- |
| **Kiota** 1.34.1 | 132 files, 11,969 lines. Generates the entire request-builder tree. Two discriminator warnings on the product metadata. | 4–5 `Microsoft.Kiota.*`, with [open AOT warnings](https://github.com/microsoft/kiota/issues/4065) in `Abstractions` and `Serialization.Json` |
| **NSwag** 14.7.1, full client | The client uses reflection-based serialization — [not AOT compatible](https://github.com/RicoSuter/NSwag/issues/5284), an open feature request | moderate |
| **NSwag, DTOs only** | **2,691 lines, 102 classes.** `allOf` chains map cleanly to inheritance (`BasicProductWithId : BasicProductCreation : BasicProduct : ProductCore`). | **none** |
| **OpenAPI Generator** | Java toolchain in the build | moderate |
| **OpenApiGenerator** (HavenDV) | Incremental source generator, AOT-capable — but 0.x versions | small |

### The decisive measurement

NSwag's DTOs, compiled against `net10.0` with `IsAotCompatible=true`,
`EnableTrimAnalyzer=true`, `Nullable=enable` and a `JsonSerializerContext`:

```
0 Warning(s)
0 Error(s)
```

Two switches are required: `/generateDataAnnotations:false` (otherwise two
`IL2026` from `MinLength`/`MaxLength`) and `/generateNullableReferenceTypes:true`.

Worth noting: `[JsonExtensionData] IDictionary<string, object>` — how NSwag maps
Emporix' localised fields and mixins — produces **no** AOT warning. The source
generator handles it. I had expected the opposite.

## Decision

**NSwag in DTO-only mode, plus a hand-written client.**

Structurally the same approach as the Node SDK, and the only one that achieves
AOT compatibility and zero dependencies at the same time.

Configuration:

```
nswag openapi2csclient
  /input:specs/<service>.yml
  /generateClientClasses:false
  /jsonLibrary:SystemTextJson
  /generateDataAnnotations:false
  /generateNullableReferenceTypes:true
  /generateOptionalPropertiesAsNullable:true
```

## Consequences

**Good:** No runtime dependencies. Full control over the public surface. The
facade layer can be ported from the Node SDK rather than reinvented. `allOf`
becomes idiomatic inheritance.

**Cost:** Roughly 650 operations have to be written by hand — the main work of
this project and the reason for the staged scope from the analysis. For inline
schemas NSwag produces names like `Anonymous`, `Anonymous2`, `Value`, `Values`;
those need renaming through `/typeNameGeneratorType` or post-processing in the
generator tool.

**`oneOf` stays hand work.** NSwag does not turn it into a discriminated union.
Where Emporix uses `oneOf` (for example `Product` = Basic | Bundle |
ParentVariant), we write the `JsonConverter` with `[JsonDerivedType]` ourselves —
AOT-capable, but once per case.

**The `JsonSerializerContext` has to be generated.** At roughly 100 types per
specification a hand-maintained `[JsonSerializable]` list is not maintainable; the
generator tool writes it along.

**Revisit if:** NSwag fails structurally on a specification, or OpenApiGenerator
(HavenDV) reaches 1.0. Either changes only the generation pipeline, not the
client — that is the point of the separation.

Sources: [Kiota AOT issue #4065](https://github.com/microsoft/kiota/issues/4065) ·
[NSwag STJ source generation #5284](https://github.com/RicoSuter/NSwag/issues/5284) ·
[AOT-compatible libraries](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)

---

## Addendum, 2026-08-31 — what the first full run showed

The decision stands; the effort was larger than estimated here. Four points that
were not visible before the first run:

**The specifications need more repairs than in the Node SDK.** There it is a
single patch; here there are three. Two of them are not Emporix' doing but our
toolchain's: NSwag converts YAML via JSON and trips over YAML's own escapes
(`\_`, `\L`) and over whitespace-only lines following a `|` block. The Node SDK's
YAML reader digests both without complaint. **A patch the Node SDK does not have
is therefore no indication of a new defect at Emporix.**

**The generated code needs post-processing.** Two patterns did not compile: empty
classes deriving from `string` (from an `allOf` with a single constituent — an
alias in TypeScript, impossible in C#), and references to types NSwag never
generated. Both are resolved in the pipeline, not by hand in the generated file.

**The `oneOf` weakness costs more than expected.** 48 fields across 6 types end up
as `JsonElement` because Emporix describes them as a union without a
discriminator. For the first wave of facades that is tolerable; where such a field
sits on the storefront path, a hand-written converter belongs beside it.

**A wrong post-processing step is more dangerous than none.** The first version of
the dangling-reference resolution replaced 121 valid types in `Cart.cs` alone with
`JsonElement` — a cart's price would have become raw JSON. It surfaced on
inspection, not at compile time: the code built without complaint. The check is
now written so its correctness is evident, and is covered by tests.

**Result:** 43 specifications, 43 files, roughly 56,000 lines, 2.4 MB — compiling
without a single warning.

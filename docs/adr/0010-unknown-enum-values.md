# ADR-0010 — Unknown enum values: null where there is room, strict where there is not

**Status:** Implemented · **Date:** 2026-09-04 · Affects: [ADR-0001](0001-type-generation.md), [ADR-0004](0004-aot-trimming.md)

## Context

The vendored specifications declare 240 enums. When Emporix sends a value one
of them does not list, `JsonStringEnumConverter` throws — and it does not throw
for the field alone. **The whole response is lost:** a product, an order, a page
of sixty, because one field carried a value the vendored copy had not caught up
with.

The attribute cannot be overridden from `JsonContexts.cs`. NSwag emits it at the
**property** level on 243 scalar enum properties, and a property-level attribute
beats anything a `JsonSerializerContext` declares. `AnnotateEnums` in
`GeneratedCodeFixer` additionally puts one on all 240 enum **declarations**,
which is what covers collections and dictionary values.

Nothing has been observed. No live call has produced an unlisted value; the
recorded enum defects are of a different kind — casing that the live API rejects,
and the missing converter inside collections. The case for acting is the
disproportion between the cost and the field, not an incident.

## What the probes established

Written down because each was cheap to measure and expensive to assume.

| Question | Answer |
| --- | --- |
| Does a converter on the enum *type* also serve `T?` properties? | Yes |
| Can it then answer «unrecognised»? | **No.** It returns `T`, so unknown becomes `default(T)` — for `ProductType` that is `BASIC`: a wrong value that looks right |
| Can a converter over `T?` answer null? | Yes, and it works under source generation |
| Does that null travel back on a write? | No. Every context sets `DefaultIgnoreCondition = WhenWritingNull`, so the property is omitted |
| May the converter be `internal`? | **No.** The source generator rejects it with `SYSLIB1220` |
| May it tighten case matching? | **No.** 180 generated members differ from their wire value in case alone — the specifications write `string` where NSwag emitted `String` |

## Options

1. **Null on unknown, nullable properties only.** Chosen.
2. **A sentinel member on every enum.** Uniform for `T` and `T?`, and the only
   option that also covers the 75 non-nullable properties. Rejected: it adds a
   public member to 240 enums, requires stripping 243 property-level attributes
   so the type-level one wins, and — unlike a null — the sentinel *serializes
   back out*, sending Emporix a value it never defined.
3. **Enums as `string`.** Rejected: destroys 240 public types and all type
   safety for a risk nobody has met.
4. **Leave it, document it.** Rejected on the disproportion above.

## Decision

A `SpecSync` rule rewrites the property-level attribute on the **168 nullable**
enum properties to `NullOnUnknownEnumConverter<T>`. An unrecognised value reads
as `null`; the rest of the object survives; nothing travels back out.

The **75 non-nullable** properties keep the strict converter. The specifications
mark those required, so an unrecognised value there is a broken contract rather
than a field to shrug off — and there is nowhere to put the absence. The 240
type-level attributes are untouched, so collections and dictionary values stay
strict too.

## Why not simply tolerant everywhere

Because `default(T)` is not «unknown», it is the first member. A tolerant
type-level converter would have made `productType: "SOMETHING_NEW"` read as
`BASIC` — silently, and on every one of the 483 sites. Replacing a loud failure
with a quiet wrong answer is the one outcome worse than the failure.

## Consequences

- An unknown value is indistinguishable from an absent one on those 168
  properties. Both leave the caller without a usable value and send nothing
  back, so the conflation costs nothing a caller can act on.
- The raw string is not recoverable. The property is declared, so the value does
  not land in `AdditionalProperties`. Recovering it would need a companion
  property on generated types, which is more machinery than the case justifies.
- `NullOnUnknownEnumConverter<T>` is public because `SYSLIB1220` requires it,
  not because anything outside the SDK should name it.
- Enum **casing** is a separate, real, observed defect and stays a `SpecPatch`
  matter. This converter deliberately does not fix what it puts on the wire.

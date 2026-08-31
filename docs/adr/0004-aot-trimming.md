# ADR-0004 — Native AOT and trimming

**Status:** Decided (product owner, 2026-08-31) · Affects: [ADR-0001](0001-type-generation.md), [ADR-0005](0005-resilience.md)

## Context

A library is never «AOT» — Native AOT is a publish option for applications. All we
decide is whether our SDK is **AOT compatible**, that is, whether a consumer
publishing with `PublishAot=true` can use us.

Without that compatibility such a consumer gets trim warnings at publish time and,
in the worst case, a `NotSupportedException` from the serializer only at runtime.

## Decision

**Build for AOT compatibility; do not advertise AOT as a feature.**

```xml
<IsAotCompatible>true</IsAotCompatible>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Plus a sample project that publishes with `PublishAot=true` as a CI gate —
otherwise the compatibility rots unnoticed.

## Consequences

### Binding rules for all code

1. **`System.Text.Json` exclusively through `JsonSerializerContext`.** No
   `JsonSerializer.Serialize(obj)` without a context, no reflection fallbacks.
2. **No `Type.GetType()`, no `Activator.CreateInstance` with dynamic types, no
   `Expression.Compile()`.**
3. **Options binding through the source generator**
   (`Microsoft.Extensions.Options` otherwise binds by reflection).
4. **Every dependency must be trim-safe.** A single trim warning from a foreign
   package blocks the build under `TreatWarningsAsErrors` — an additional, hard
   reason for the dependency budget in [ADR-0005](0005-resilience.md).

### What was measured

NSwag's DTOs for `product.yml` compile under exactly these switches with **0
warnings** (see [ADR-0001](0001-type-generation.md)). Contrary to my expectation
`[JsonExtensionData] IDictionary<string, object>` produces no warning either — the
AOT cost is not where I assumed it would be.

### Where it does hurt

**Mixins.** Emporix mixins are dynamic by definition (`additionalProperties:
true`, customer-specific fields). They are passed through as `JsonElement` or
`JsonNode`, not as a `Dictionary<string, object>` with polymorphic
deserialization. That shapes the public API at a visible spot — a consumer wanting
to deserialize a mixin into their own type has to bring their own
`JsonSerializerContext`. That is the documented consequence, not an oversight.

**`oneOf` polymorphism** needs `[JsonDerivedType]` or a custom converter rather
than a reflection discriminator.

### Benefits even without AOT

Source-generated serialization is faster and allocates less than the reflection
path under JIT too. Trimming works, which makes self-contained deployments
smaller. The benefit therefore accrues even if no consumer ever publishes AOT.

### Why now and not later

The costs are almost entirely up front. Becoming AOT compatible after the fact is
substantially more expensive, because by then reflection has spread through
serialization, polymorphism and options binding. You pay once at the start or
several times later.

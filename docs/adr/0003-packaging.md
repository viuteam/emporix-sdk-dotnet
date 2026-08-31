# ADR-0003 — Packaging

**Status:** Proposed · **Date:** 2026-08-31 · Affects: [ADR-0006](0006-naming.md)

## Context

The Node SDK is a monorepo of five packages, and `@viu/emporix-sdk` additionally
offers 15 subpath exports (`@viu/emporix-sdk/product` and so on) plus a
tree-shakeable `createEmporixClient` factory.

**The reason is bundle size in the browser, nothing else.** The code says so
itself: «reach for this when bundle size matters». There is a `check:treeshake`
test asserting that unused services fall out.

In .NET that problem does not exist in the same form. The IL trimmer works on the
assembly and removes unused code when the consumer publishes — regardless of how
we slice the package.

## Options

| Option | For | Against |
| --- | --- | --- |
| **One package** | One version, one changelog, one reference. Consumers assemble nothing. The trimmer handles size. | Someone reading only products still carries every type (on disk, not in memory) |
| Core plus one package per service | Fine-grained references | ~48 packages, ~48 versions, a matrix problem at every release. Emporix services cross-reference each other (segments need products and categories), which forces package dependencies |
| Core plus groups (storefront / B2B / admin) | A middle ground | The group boundary is arbitrary and becomes contested at the first service that straddles it |

## Decision

**One package.** `Viu.Emporix` contains the core and every service.

## Consequences

**Good:** One `PackageId`, one SemVer line, one `CHANGELOG.md`, one
`PackageValidation` baseline. The staged scope from the analysis (storefront →
B2B → admin) becomes additive minor releases of the same package rather than new
packages — much the calmer experience for consumers.

**Cost:** The assembly grows large. With roughly 650 operations and the generated
DTOs from 43 specifications, expect several MB. For consumers who do not trim
that is disk space, not runtime overhead — .NET loads metadata on demand. Those
who trim get rid of it.

**What we do not port:** `createEmporixClient` (the tree-shakeable factory) and
the subpath exports are dropped entirely. Both answer a JavaScript problem.

**Lazy instantiation instead of a factory.** The 48 service properties on
`EmporixClient` are initialised on first use, so a client that only touches
`Products` does not build 48 objects. That replaces the factory's benefit at a
fraction of the complexity.

**Revisitable:** Should size turn out to bother consumers, splitting later is a
breaking change — but one that can be done cleanly through type forwarding. The
reverse (merging many packages) is the more painful direction. Here too: the
chosen direction has the cheaper way back.

# ADR-0002 — Target framework

**Status:** Decided (product owner, 2026-08-31) · Affects: [ADR-0003](0003-packaging.md), [ADR-0004](0004-aot-trimming.md)

## Context

The choice was `net10.0` alone, or additionally `net8.0` as an LTS for wider
reach. `net10.0` has been available since November 2025 and is itself LTS;
`net8.0` leaves support in November 2026.

## Options

| Option | For | Against |
| --- | --- | --- |
| **`net10.0` only** | One build, one test run, no `#if` branches. C# 14 without restraint. `IsAotCompatible` without a framework condition. | Excludes consumers on `net8.0` |
| `net10.0` + `net8.0` | Wider reach | Doubled test matrix, feature gates for C# 14 and BCL additions, ongoing maintenance |

## Decision

**`net10.0` only.** Decided by the product owner.

## Consequences

**Good:** No multi-targeting means no `#if NET8_0_OR_GREATER` branches, one test
matrix instead of two, and C# 14 plus the current BCL without regard for an older
baseline. `IsAotCompatible` can be set unconditionally rather than through
`IsTargetFrameworkCompatible`.

**Cost:** Consumers on `net8.0` cannot use the package. For an SDK starting fresh
with no existing user base that is acceptable — the audience is building new.

**Revisitable:** Adding a `net8.0` target later is cheap as long as we do not lean
on `net10.0`-exclusive APIs deep in the core. The reverse — removing a target
later — would be a breaking change. The chosen direction has the cheaper way back.

**To keep in mind:** Where a `net10.0`-exclusive BCL API offers a convenient
shortcut that is not essential, we take it anyway — but we do not document it as
architectural bedrock. That keeps the reverse option open without constraining us
today.

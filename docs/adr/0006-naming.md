# ADR-0006 — Naming and legal boundary

**Status:** Accepted (product owner, 2026-08-31) · **Date:** 2026-08-31

## Context

We are not Emporix. The name has to make clear that this is a viu SDK and not an
official Emporix product.

The Node SDK solves that through the npm scope: `@viu/emporix-sdk`. The scope
carries authorship, the rest carries subject matter. **NuGet has no comparable
scope mechanism** — only a flat namespace with a dot convention. The name alone
therefore has to carry the distinction more clearly than in the Node case.

## Options

| `PackageId` | Assessment |
| --- | --- |
| **`Viu.Emporix`** | Origin first, subject second. As close to `@viu/emporix-sdk` as NuGet allows. Short. |
| `Viu.Emporix.Sdk` | The same plus a word that distinguishes nothing — a NuGet package containing client classes *is* an SDK. Redundant. |
| `Emporix.Sdk` / `EmporixSdk` | Reads like a first-party package. **Reject**, regardless of the trademark question. |
| `Viu.Commerce.Emporix` | Reserves room for further commerce backends — speculative, there is no second one |

For reserved prefixes on nuget.org: `Viu.*` would be reservable by viu,
`Emporix.*` would not. Another argument for the leading `Viu.`.

## Decision

| | |
| --- | --- |
| `PackageId` | `Viu.Emporix` |
| Root namespace | `Viu.Emporix` |
| Assembly | `Viu.Emporix.dll` |
| Main type | `Viu.Emporix.EmporixClient` |
| Generated DTOs | `Viu.Emporix.<Service>Models` |
| Internal infrastructure | `Viu.Emporix.Http`, `Viu.Emporix.Authentication` |
| License | MIT — **confirmed** |
| `RepositoryUrl` | `https://github.com/viuteam/emporix-sdk-dotnet`, public — **confirmed** |

A namespace without an `.Sdk` level: `Viu.Emporix.EmporixClient` reads better than
`Viu.Emporix.Sdk.EmporixClient`, and the type name already carries «Emporix».

## On the trademark question

**May «Emporix» appear in the package name?** Confirmed by the product owner on
2026-08-31.

Basis: the Node SDK already uses the name publicly on npm, so the same
consideration applies here. In addition, nominative use of another party's
trademark to describe compatibility is generally permissible as long as no
confusion about authorship arises — which the leading `Viu.` and the disclaimer
below work against. This is expressly not legal advice.

## Disclaimer in the README and package description

Proposed wording, prominently at the top:

> This SDK is built and maintained by viu. It is **not an official product of
> Emporix AG** and is neither published nor supported by Emporix. «Emporix» is a
> trademark of its respective owner and is used here solely to describe
> compatibility.

The same, abbreviated, as `<Description>` in the `.csproj`, because nuget.org
shows only that and not everyone opens the README.

## Consequences

After the first publish the name is effectively immutable — changing a
`PackageId` means a new package and a migration for every consumer. That is why
the trademark question had to be answered **before** `0.1.0-preview.1`, not after.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An unofficial .NET SDK for the Emporix Commerce API, published as `Viu.Emporix`.
Every one of the 44 vendored Emporix specifications has a hand-written facade,
plus cloud functions, which has no specification: 47 properties on the client
over 665 public calls.

## Commands

```bash
dotnet build                                    # TreatWarningsAsErrors is on — a warning fails the build
dotnet test                                     # ~495 tests, under a second
dotnet test --filter "SpecPathTests"            # one class
dotnet test --filter "FullyQualifiedName~Import" # one subject
```

```bash
./scripts/update-public-api.sh                  # record new public symbols after adding API
./scripts/promote-public-api.sh                 # Unshipped → Shipped; runs itself on the release PR
```

`Viu.Emporix.MixinSync` is the second published project — a `dotnet tool` that
generates typed mixins into a consumer's repository. It is the only project
besides `tools/Viu.Emporix.SpecSync` that opts out of `IsAotCompatible`, because
NJsonSchema cannot satisfy it and pulls Newtonsoft. It shares the core's version
line: one tag, one Release Please package, two `dotnet pack` calls in
`publish.yml`. Because it lives under `src/` rather than `tools/`, the
console-application exception in `.editorconfig` names it explicitly.

```bash
dotnet tool install --global Viu.Emporix.MixinSync
emporix-mixins pull && emporix-mixins generate   # in a consumer repository
emporix-mixins check                             # CI drift gate
```

```bash
dotnet run --project tools/Viu.Emporix.SpecSync             # fetch and generate
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch    # download and repair specs only
dotnet run --project tools/Viu.Emporix.SpecSync -- generate # regenerate types only
```

```bash
dotnet publish samples/Viu.Emporix.Sample --configuration Release      # the real AOT check
dotnet publish samples/Viu.Emporix.Storefront --configuration Release
```

The live smoke test needs credentials from the environment and is the only thing
that catches a request body the API rejects. `samples/Viu.Emporix.SmokeTest`
walks the anonymous storefront flow; with `EMPORIX_BACKEND_CLIENT_ID` and
`EMPORIX_BACKEND_SECRET` it also makes a read-only pass over the seller-side
services. See the README section «Before releasing: the smoke test».

## The rule that matters most

**Do not invent endpoints, fields or scopes.** Everything is verified against a
vendored specification in `specs/` or against the Node SDK at
`../emporix-sdk`. When neither settles a question, ask — the Emporix
documentation MCP connector is the third source. Guessing here is not a style
preference: it produces calls that compile, pass their tests, and can never work.

That has happened repeatedly. Of the twenty-odd defects found in this SDK so far,
about half came from reading specifications against the code, most of the rest
from live API calls, one from building a sample that consumes the package, and
**none from a unit test**. A stubbed `HttpMessageHandler` asserts the same wrong
call the code builds. The tallies are in [docs/roadmap.md](docs/roadmap.md), wave
by wave.

`tests/Viu.Emporix.Tests/SpecPathTests.cs` exists because of that: it scans the
service sources, resolves every `VERB /path` the SDK builds, and fails if the
specifications do not declare it. It has caught real defects since. Write paths
as a single interpolated string — a path assembled by concatenating outside the
string is invisible to the scanner.

## Architecture

**`src/Viu.Emporix/Generated/`** — DTOs produced by NSwag from `specs/`. **Never
edited by hand.** A defect in a generated type is fixed in
`tools/Viu.Emporix.SpecSync`, either as a `SpecPatch` when the specification is
wrong or as a `GeneratedCodeFixer` rule when the generator is. Editing the output
means the next sync silently undoes the fix.

**Hand-written facades** sit next to it, one file per service or per closely
related group. Each takes `EmporixHttpClient` and `IOptions<EmporixOptions>`,
exposes a `BasePath`, and returns generated types. Wide services expose nested
operation groups as properties — `client.Iam.Groups`, `client.Ai.Tools` — see
`IamService.cs` for the pattern.

**`EmporixClient`** bundles the facades lazily; `ServiceCollectionExtensions`
registers the same set for a container. Adding a service means touching three
places — the facade, the client property, the DI registration — and forgetting
the third compiles and passes every other test.

### Four invariants

**`AuthContext` is a per-call parameter, never state on the client.** That is what
lets one client instance serve many concurrent users. `Defaults.Service(auth)`
and `Defaults.Anonymous(auth)` pick the default when a caller passes nothing.
Endpoints that need a signed-in customer reject the wrong kind of context up
front rather than failing obscurely later.

**One `JsonSerializerContext` per service, without exception.** All of them live
in `JsonContexts.cs` with fully qualified `[JsonSerializable]` entries. Emporix
reuses type names across specifications — `Metadata`, `Vendor`, `Price` — and a
shared context collides on them and aborts the source generator with
`SYSLIB1031`, taking every other context with it. Grouping even three small
services was enough to collide.

**The idempotency gate.** `Idempotent = true` marks a request the retry handler
may repeat. GET, PUT and DELETE generally qualify. A `POST` or `PATCH` qualifies
only when repeating it is provably harmless — a search that carries its filter in
the body does; anything that moves money, places an order, consumes a number or
runs someone else's code does not.

**No reflection.** `IsAotCompatible` is on and warnings are errors, so a
reflection-based call fails the build rather than shipping. This is
[ADR-0004](docs/adr/0004-aot-trimming.md), and it is why `AddEmporix(IConfiguration)`
needs `EnableConfigurationBindingGenerator` and why cloud functions take a
`JsonTypeInfo` from the caller.

### Where the SDK gives up on types, deliberately

`Ai.Tools` and `Ai.McpServers` return `JsonElement` from reads: the
specifications declare unions with no discriminator the generator can act on, and
picking one alternative would silently drop the others' fields. Writes stay
typed, one method per kind. `CloudFunctions` has no specification at all.

### Decisions

`docs/adr/` holds nine ADRs. 0001 type generation, 0004 AOT and trimming, 0005
retry and backoff, 0007 streaming, 0008 long-running jobs, 0009 cloud functions.
Read the relevant one before changing behaviour it covers.

## Public API surface

`Microsoft.CodeAnalysis.PublicApiAnalyzers` tracks every public symbol.
`RS0016` after adding API means running `./scripts/update-public-api.sh`. `RS0026`
means two overloads with optional parameters — rename one rather than suppress
it. Generated types are excluded from the baseline.

## Releases

Cut by Release Please: every push to `main` updates a release pull request,
merging it publishes. Commit subjects decide the version and the changelog —
`feat` minor, `fix` patch, `chore`/`ci`/`test` nothing. Full detail in
[docs/releasing.md](docs/releasing.md).

**Do not put nested parentheses in a commit body.** Release Please parses the
whole message and drops a commit it cannot parse — no changelog entry, no version
bump, the change ships anyway. A code fence does not protect them. Write
`GetSection of the options`, not `GetSection("x"))`. A CI check enforces this.

## Language

Code, comments, documentation and commit messages in English. Comments explain
why, not what, and are worth writing where a reader would otherwise wonder — most
of this codebase's comments record a defect that a live call found.

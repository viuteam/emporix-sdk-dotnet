# Contributing

## The rule that matters most

**Do not invent endpoints, fields or scopes.** Everything is verified against a
vendored specification in [`specs/`](specs/) or against the Node SDK. When
neither settles a question, ask — the Emporix documentation is the third source.

This is not a style preference. Guessing here produces calls that compile, pass
their tests, and can never work. Of the two dozen defects found in this SDK so
far, about half came from reading specifications against the code, most of the
rest from live API calls, and **none from a unit test** — a stubbed
`HttpMessageHandler` asserts the same wrong call the code builds.

## Local checks

```bash
dotnet build          # TreatWarningsAsErrors is on: a warning fails the build
dotnet test           # ~570 tests, well under a second
```

Before anything that touches the public surface or serialization:

```bash
./scripts/update-public-api.sh                                    # after adding public API
dotnet publish samples/Viu.Emporix.Sample --configuration Release # the real AOT check
```

The AOT publish is worth the minute. The analyzers catch most of it, but ILC
sees things they do not.

## Six mistakes that cost time here

| Mistake | What happens |
| --- | --- |
| Editing `src/Viu.Emporix/Generated/**` | the next spec sync silently reverts it — fix it in `tools/Viu.Emporix.SpecSync` instead, as a `SpecPatch` or a `GeneratedCodeFixer` rule |
| Adding public API without the baseline | `RS0016`. Run `./scripts/update-public-api.sh` |
| Two public overloads with optional parameters | `RS0026`. Rename one rather than suppress it |
| A new service without its DI registration | compiles and passes every other test. Adding a service means the facade, the client property **and** `ServiceCollectionExtensions` |
| Grouping services into one `JsonSerializerContext` | `SYSLIB1031`. Emporix reuses type names across specifications, so it is one context per service, without exception |
| Nested parentheses in a commit body | Release Please drops the commit whole: no changelog entry, no version bump, and the change ships anyway. A code fence does not protect them |

The last one has a CI check, but it is worth knowing why: write `GetSection of
the options`, not `GetSection("x"))`.

## Commit messages

Conventional Commits, and here they **do** drive the version — Release Please
reads them. That is the difference from the Node SDK, where Changesets does it
and the messages are history hygiene only.

| Type | Changelog | Version |
| --- | --- | --- |
| `feat` | Added | minor while below 1.0 |
| `fix` | Fixed | patch |
| `perf`, `refactor` | Changed | patch |
| `docs` | Documentation | patch |
| `deps` | Dependencies | patch |
| `chore`, `test`, `ci`, `build`, `style` | not shown | none |

A `!` after the type, or a `BREAKING CHANGE:` footer, marks a break. Below 1.0
that bumps the minor.

```
feat: add the audit log service
fix(cart): send the item YRN rather than the bare id
feat!: require a JsonTypeInfo for cloud functions
```

Comments and commit bodies explain **why**, not what. Most comments in this
codebase record a defect that a live call found — that is the kind worth adding.

## Pull requests

**Every pull request is squashed.** Merge and rebase merges are switched off,
because a merge commit produced a second, duplicate changelog entry. The full
reasoning is in [docs/releasing.md](docs/releasing.md).

Two consequences worth knowing before you write the description:

- **The pull-request title becomes the commit subject**, so it has to be a valid
  conventional commit. A CI check enforces it.
- **A `BREAKING CHANGE:` footer has to live in a branch commit**, not only in
  the description. The description is not carried onto `main`, and the
  breaking-changes section would then just repeat the title.

## Releases

Cut by Release Please: every push to `main` updates a release pull request, and
merging it publishes to nuget.org. You do not tag anything by hand.

Two packages share one version line — `Viu.Emporix` and the
`Viu.Emporix.MixinSync` tool. Full detail, including the traps that have cost
releases here, in [docs/releasing.md](docs/releasing.md).

Before a release that changes request bodies, run the smoke test. It needs
credentials from the environment and is the only thing that catches a body the
API rejects — see «Before releasing: the smoke test» in the
[readme](README.md).

## Decisions

[`docs/adr/`](docs/adr/) holds nine architecture decisions. The ones with
behaviour attached are 0001 type generation, 0004 AOT and trimming, 0005 retry
and backoff, 0007 streaming, 0008 long-running jobs and 0009 cloud functions.
Read the relevant one before changing behaviour it covers, and add an ADR when
you change something at that level.

Design work in progress lives in
[`docs/superpowers/specs/`](docs/superpowers/specs/) and its plans alongside.

## Language

Code, comments, documentation and commit messages in English.

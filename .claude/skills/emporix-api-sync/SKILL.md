---
name: emporix-api-sync
description: >
  Brings the vendored Emporix OpenAPI specifications level with upstream, finds
  which spec operations no facade wraps yet, implements the gaps to this repo's
  standard, proves them against a live tenant, and opens a reviewable PR with
  `gh`. Use this skill whenever the work touches Emporix spec or schema drift —
  "sind die YAMLs noch aktuell", "verifiziere ob das Schema neue Endpoints
  erhalten hat", a link to developer.emporix.io/changelog or to
  emporix/api-references, "welche Endpoints fehlen noch", "sync the specs", "is
  <service> fully covered", the daily `chore/spec-sync` PR needing review, or a
  request to implement a newly documented Emporix endpoint. It applies even when
  the user only wants to *check* one service and has not asked for a PR, and
  even when they name the service rather than the spec file.
---

# Emporix API sync

Vendored specs level with upstream, every uncovered operation found, the gaps
built to this repo's standard, one PR a reviewer can check. The deliverable is a
number someone else can reproduce with one command — not an impression.

## The one thing to get right

**The repo already measures coverage. Use it; do not re-derive it.**
`tests/Viu.Emporix.Tests/SpecPathTests.cs` resolves every `VERB /path` the
facades build and set-compares both directions against the specifications:

| Test | Direction | Fails when |
|---|---|---|
| `Every_call_a_service_builds_exists_in_a_specification` | facade → spec | the SDK calls something no spec declares |
| `Every_operation_a_specification_declares_has_a_facade` | spec → facade | a spec operation has no facade, beyond the known gaps |
| `The_scanner_reads_every_path_but_one` | the scanner itself | a new way of writing a path crept in, so something is checked by neither direction |

Operations are matched on path literal, normalised to `METHOD /svc/{}/thing/{}`
— never counted. «11 operations before and after» is equally true when one was
removed and another added, and `operationId`s get renamed upstream. Writing a
second measurement next to this one would be a worse copy of 400 lines of
resolution logic, and the two would disagree the first time either moved.

The third test is the one people forget. A path the scanner cannot read is
invisible to *both* directions, which is how 212 of 639 calls once went
unchecked in silence. If it fails, fix the scanner before believing anything
else in this file.

## 1. Find out whether the bot already synced

`.github/workflows/spec-sync.yml` runs daily at 06:00 UTC and does the vendoring
for you: fetch, regenerate, refresh the public API surface, build, test, then
open or update **`chore/spec-sync`**.

```bash
git fetch origin --prune
git log --oneline HEAD..origin/main
gh pr list --state all --limit 5 --json number,state,title,headRefName
gh run list --workflow "Emporix specification sync" --limit 3
```

- **A sync PR is open** → that is the vendoring. Review it, measure coverage
  against its head, and put facade work in a follow-up branch. Do not push to
  `chore/spec-sync`; `peter-evans/create-pull-request` force-owns that branch and
  you would be fighting a scheduled job.
- **A sync PR merged recently** → the specs on `main` are current. Skip to
  step 3; `fetch` will tell you nothing changed.
- **The last run failed** → read it before anything else. **A failed run is the
  interesting case, not the boring one.** The workflow builds and tests before
  it opens anything, and `SpecPathTests` fails the moment a sync brings an
  operation no facade wraps — so the run that found the most gets no PR at all.
  The 2026-09-10 run failed exactly this way on `PATCH /media/{}/assets/{}`, and
  from the outside it looked identical to a quiet day.

  ```bash
  gh run view <id> --log-failed | grep -E "Actual|Expected"
  ```

  That `Actual:` list is already the coverage measurement, taken on upstream's
  current state. Start from it.
- **Neither** → sync yourself, step 2.

**Check `main` before branching, every time.** A daily job pushes here, so a
checkout goes stale over a weekend and `git log -- specs/<svc>.yml` then answers
truthfully about a file that moved two days ago on the real `main`. That has
produced a confident wrong answer twice. The tell: the local
`specs/sync-manifest.json` records a different `sha256` for a service than an
open PR's own base side shows — impossible unless the checkout is behind.

Then start clean:

```bash
git status --porcelain     # must be empty
```

A spec left modified by an earlier `fetch` becomes part of your diff without
appearing in your reasoning.

## 2. Sync the specs yourself

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch
```

Its **last line is the authoritative answer** to «did anything change»:
`Changed: ai-service, media, product` — a sha256 per service from
`SyncManifest.Diff` — or `No content changes.`, in which case say so and stop;
there is no PR to make.

Never answer that question from `git diff specs/sync-manifest.json`. every `fetchedAt`
plus `generatedAt` is rewritten on every run, so the manifest shows about 45
changed lines on each side when no spec byte moved. Reporting drift that did not exist is the failure
mode here. The workflow guards against it by discarding the manifest when no
spec content changed; on your own branch nothing does that for you.

Watch the output for a patch reported **stale** — upstream fixed a defect that
`tools/Viu.Emporix.SpecSync/SpecPatches.cs` was working around, so that entry
should go, in its own commit with the reason.

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- generate
git diff --stat src/Viu.Emporix/Generated/
```

The generated diff must touch only the services `fetch` named, plus whatever a
`GeneratedCodeFixer` rule reaches. Anything wider means the generator moved,
which is a separate PR.

Read the generated diff, don't skim it. A removed type is as interesting as an
added one: when the AI attachment body became a `oneOf`, NSwag stopped emitting
`FileParameter` — harmless there because nothing referenced it, but the same
shape of change can delete a type a facade names.

**Never hand-edit `src/Viu.Emporix/Generated/`.** A wrong generated type is
fixed in `tools/Viu.Emporix.SpecSync` — a `SpecPatch` when the specification is
wrong, a `GeneratedCodeFixer` rule when the generator is. Editing the output
works until the next sync silently undoes it.

## 3. Measure coverage

```bash
dotnet build
dotnet test --no-build --filter "SpecPathTests"
```

A failure prints the uncovered set as `Actual:` — that list is the answer. Turn
it into something you can act on:

```bash
python3 .claude/skills/emporix-api-sync/scripts/annotate_operations.py --from-test
python3 .claude/skills/emporix-api-sync/scripts/annotate_operations.py "PATCH /media/{}/assets/{}"
python3 .claude/skills/emporix-api-sync/scripts/annotate_operations.py --spec media
```

It prints each operation's `operationId`, its OAuth scopes — which is what picks
the auth default — and whether upstream marks it **`[deprecated]`**.

**Check that flag before building anything.** `SpecPathTests` does not exclude
deprecated operations, so one appears as a gap like any other, and wrapping an
endpoint Emporix has already retired is wasted work that then has to be
supported. At the time of writing, all five entries in `KnownGaps` carry
`deprecated: true` upstream — so that list currently says «retired», not «not
built yet», whatever its comment says. Re-check rather than trusting this
paragraph; it is exactly the kind of statement that goes stale.

### Known gaps that are decisions, not work

Three lists in `SpecPathTests.cs` carry them, and each is a different kind of
«not a gap». Read them before reporting anything as missing:

| List | Meaning |
|---|---|
| `ImplementedWithoutAFacade` | reached, but not through a facade — the token endpoints belong to `DefaultTokenProvider`, the customer-session ones to a private helper in `CustomerService`. Covered by their own tests. |
| `Superseded` | the specification marks it `deprecated: true` and the SDK uses its replacement |
| `KnownGaps` | Emporix offers it, the SDK does not wrap it, and someone chose that |

`KnownGaps` is the interesting one: it is the difference between a gap someone
chose and a gap nobody noticed. When you fill one, delete its line — the test
asserts set equality, so a filled gap left in the list fails just as loudly as a
new one.

## 4. Read the changelog for what the tests cannot see

<https://developer.emporix.io/changelog> and the upstream PRs in
`emporix/api-references`. Fetch the changelog through the **Emporix
documentation MCP connector**; it is large, so grep the saved file for the
`{% update date="…" %}` blocks newer than the last sync rather than reading all
of it.

`SpecPathTests` finds missing *paths*. It cannot find:

- **New fields.** Facades return generated types, so a field becomes usable the
  moment the spec is vendored — no facade diff, no test, nothing to build. It
  still needs saying, because nothing else announces it.
- **New behaviour on an existing path.** A new `502`, a validation that now runs
  before a write, a field that turns out to be immutable, a merge semantic. This
  is often the more valuable half of the sync and it shows up as zero missing
  endpoints.

Both belong in the PR body. Where the behaviour changes what a caller must do,
it belongs in the facade's XML docs too — that is where someone will actually
meet it.

## 5. Implement the gaps

**Zero missing endpoints is a normal, frequent outcome.** Of three specs that
moved on 2026-09-10, exactly one added an operation; the 2026-09-07 indexing
sync added none and only documented a new `502`. Do not manufacture work to fill
a PR. What remains in that case is real but small: document the new fields and
the changed behaviour, and say plainly that the facades already covered
everything.

Read `references/facade-standard.md` — the facade method, the JSON context, the
idempotency gate, the public API surface, the tests, and the traps that review
of this repo actually catches.

## 6. Verify what a stub cannot

```bash
dotnet build                                                        # warnings are errors
dotnet test
./scripts/update-public-api.sh                                      # RS0016 means run this
dotnet publish samples/Viu.Emporix.Sample --configuration Release   # the real AOT check
```

Then break each new behaviour on purpose and prove the right test fails: flip
the verb, drop the query parameter, remove the field from the body. A test that
passes both ways tests nothing. Restore, re-run, quote the result.

And understand the limit of all of it: **of the two dozen defects found in this
SDK, none came from a unit test.** A stubbed `HttpMessageHandler` answers with
whatever the test author already believed, so a wrong call and its test agree
with each other. `SpecPathTests` closes the address half. What is left — a body
the API rejects, a response that deserialises to nothing, a write the server
accepts and discards — only a real call finds.

## 7. Prove it against the live tenant

Credentials live in `~/.emporix-smoke.env`, outside the repo. Source it, never
read it; never echo a variable from it.

```bash
set -a; . ~/.emporix-smoke.env; set +a
dotnet build --configuration Release
dotnet run --project samples/Viu.Emporix.SmokeTest -c Release --no-build
```

`SCOPE` is not a failure — the address was right and the client is not entitled.
`EMPTY` usually means the tenant has nothing configured for that step. Only
`FAIL` is yours. Tenant `viu` refuses `importtool` and `changelog` for missing
scopes; that pair is the expected baseline, not news.

**If you wrote a new endpoint, the smoke test is where it belongs** — not in a
throwaway probe. This is the sharpest lesson this repo has:

> A one-off probe for the media patch was written to exercise the path its
> author had in mind, and it passed. The same code added to the smoke test
> failed on its first run, because the smoke test creates its asset the way a
> caller would — without references — and Emporix discards a reference written
> onto an asset that has none, answering `204` with no body. The probe had
> seeded the asset with a reference and never met the case.

So: a probe tells you whether the call *can* work; a smoke-test step tells you
whether it works for someone who did not write it. Prefer the second. Three
things make such a step earn its keep:

1. **Read back after every write.** Several Emporix writes answer `204` and
   discard the change — whole-array writes to `refIds`, `productType` in a
   product `PATCH`. A status code proves the request was accepted, nothing more.
2. **Clean up unconditionally.** Create what you need, delete it at the end, and
   let the delete run even when a step before it failed. A crashed probe left
   two assets in the tenant once.
3. **Start from the state a caller starts from.** A fixture arranged to make the
   call succeed is the probe mistake above.

Anything the tenant's credentials cannot reach stays **unverified against a live
tenant**, and the PR says so per method rather than implying coverage.

## 8. Ship it

Read `references/ship-it.md` — commit subject rules, the Release Please
mechanics, and the `gh pr create` body that makes the diff reviewable.

## Boundaries

- The skill **opens** a PR. It never merges one, never publishes to nuget.org,
  and never touches the `release-please--branches--main` PR.
- Never push to `chore/spec-sync`. It belongs to the scheduled job.
- Everything committed is English — code, XML docs, commit, PR body. The
  conversation stays in whatever language the user is using.
- Never log or print a token, a secret or customer data, and never `cat`
  `~/.emporix-smoke.env`.

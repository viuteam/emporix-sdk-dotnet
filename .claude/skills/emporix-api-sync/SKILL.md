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
  <service> fully covered", a failed or pending `chore/spec-sync` run, or a
  request to implement a newly documented Emporix endpoint. It applies even when
  the user only wants to *check* one service and has not asked for a PR, and
  even when they name the service rather than the spec file.
---

# Emporix API sync

Vendored specs level with upstream, every uncovered operation found, the gaps
built to this repo's standard, one PR a reviewer can check. The deliverable is a
number someone else can reproduce with one command — not an impression.

**Paths in this file are relative to the skill's own directory**, printed as
«Base directory for this skill» when the skill loads. Agents here are routinely
launched into a git worktree, where `.claude/skills/` does not exist; a
cwd-relative path is broken before you start. Set it once:

```bash
SKILL_DIR=<the base directory printed above>
```

## Most requests here are questions, not pull requests

«Sind die YAMLs noch aktuell», «welche Endpoints fehlen in availability», «warum
ist der Sync-Lauf rot» — each is answered in a handful of commands, and the
answer is usually *nothing to do*. Steps 5 to 7 are for the case where something
has to be built. Do not read them to answer a question.

```bash
git fetch origin --prune && git log --oneline HEAD..origin/main   # am I current?
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch          # last line is the answer
git status --porcelain -- specs/                                  # which files actually moved
git checkout -- specs/                                            # undo the timestamp churn
dotnet build && dotnet test --no-build --filter "SpecPathTests"   # 3 pass = no *unknown* gap
grep -A 12 "KnownGaps =" tests/Viu.Emporix.Tests/SpecPathTests.cs # the gaps that are known
```

The last two lines are one answer, not two. A green test means «nothing missing
**beyond** the known gaps», and the known gaps are usually what the question is
about — «fehlt noch ein Endpoint» is asking about exactly the list a green test
stays silent on. Report both, and run them through
`scripts/annotate_operations.py` (step 3) before calling any of them work: at the
time of writing every one is deprecated upstream.

`fetch` rewrites `fetchedAt` for all 44 services whether or not anything moved,
so a check-only run must put `specs/` back — and `git status --porcelain --
specs/` before that restore is what proves the YAMLs themselves never moved. In
a worktree-isolated run `git` through the agent's shell may be refused; spell it
`/usr/bin/git` rather than skipping the restore. If you must not write at all,
read one service's upstream URL out of
`tools/Viu.Emporix.SpecSync/SpecCatalog.cs`, `curl` it to a scratch file and
`diff`; that covers one service per run, so it answers «is *this* spec current»
rather than «is anything stale».

Then answer, and stop. **Report «no drift, fully covered» as a result, not as a
failure to find work.** That is the common outcome and it is worth a sentence,
not a pull request.

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

## 1. What state is upstream in

Run the fast path above first — it answers «did anything move» in one line, and
you need that answer under every branch below. The state only decides what to do
with it.

`.github/workflows/spec-sync.yml` is scheduled at 06:00 UTC and does the
vendoring for you: fetch, regenerate, refresh the public API surface, build,
test, then open or update **`chore/spec-sync`**. GitHub runs scheduled jobs late
— observed starts are 09:57 to 11:08 UTC — so **check `gh run list`, never the
clock**, before concluding that today's run happened.

```bash
gh pr list --state all --limit 8 --json number,state,title,headRefName
gh run list --workflow "Emporix specification sync" --limit 5
```

| What you see | What it means |
|---|---|
| A `chore/spec-sync` PR is open | that is the vendoring. Review it, measure against its head, put facade work in a follow-up branch. **Never push to `chore/spec-sync`** — `peter-evans/create-pull-request` force-owns it |
| The last run **failed** | the interesting case. See below |
| The last run succeeded and its PR merged | the specs were current as of that run |
| No run yet today | nothing has happened; your `fetch` is the only evidence |

Vendoring is not only the bot's job. A person fixing a gap vendors the specs in
the same PR — `feat!:` or `fix:`, not `chore:` — so «no sync PR» does not mean
«no sync». `git log --oneline -8 -- specs/` shows who last touched them.

### A failed run is a snapshot, not a task list

The workflow builds and tests *before* it opens anything, and `SpecPathTests`
fails the moment a sync brings an operation no facade wraps. So the run that
found the most gets no PR at all, and from the outside it looks like a quiet
day.

```bash
gh run view <id> --log-failed | grep -E "Actual|Expected"
```

`--log-failed` prints the whole job — thousands of lines, every step labelled
`UNKNOWN STEP`. Grep it; do not read it.

Two things about that output, and both have already caused wrong work:

- **It is a snapshot of its head SHA.** Someone may have fixed it since, in
  which case re-implementing is pure waste. Check today's `main` before treating
  anything there as work — run the coverage test locally, which takes a minute
  and is authoritative.
- **xUnit elides the list** with `···` when it is long. What you see may be five
  of six. Re-run the test locally for the complete set rather than trusting the
  log.

## 2. Sync the specs yourself

```bash
dotnet run --project tools/Viu.Emporix.SpecSync -- fetch
```

Its **last line is the authoritative answer** to «did anything change»:
`Changed: ai-service, media, product` — a sha256 per service from
`SyncManifest.Diff` — or `No content changes.`, in which case say so and stop;
there is no PR to make.

Never answer that question from `git diff specs/sync-manifest.json`. Every
`fetchedAt` plus `generatedAt` is rewritten on every run, so the manifest shows
about 45 changed lines on each side when no spec byte moved. Reporting drift
that did not exist is the failure mode here. The workflow discards the manifest
when no spec content changed; on your own branch nothing does that for you.

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

Read that diff, don't skim it. A removed type is as interesting as an added one:
when the AI attachment body became a `oneOf`, NSwag stopped emitting
`FileParameter` — harmless there because nothing referenced it, but the same
change can delete a type a facade names.

**Never hand-edit `src/Viu.Emporix/Generated/`.** A wrong generated type is
fixed in `tools/Viu.Emporix.SpecSync` — a `SpecPatch` when the specification is
wrong, a `GeneratedCodeFixer` rule when the generator is. Editing the output
works until the next sync silently undoes it.

## 3. Measure coverage

```bash
dotnet build && dotnet test --no-build --filter "SpecPathTests"
```

Three passes mean every operation is either wrapped or on one of the three
decision lists in `SpecPathTests.cs`. A failure prints the uncovered set as
`Actual:` — modulo the elision above.

```bash
python3 "$SKILL_DIR"/scripts/annotate_operations.py --from-test
python3 "$SKILL_DIR"/scripts/annotate_operations.py "PATCH /media/{}/assets/{}"
python3 "$SKILL_DIR"/scripts/annotate_operations.py --spec availability
```

`--from-test` runs the coverage test itself (so the build must be current) and
annotates whatever it reports; on a green suite it says so and exits. The other
two forms take keys or a whole spec. Each operation comes back with its
`operationId`, its OAuth scopes — which is what picks the auth default — and
whether upstream marks it **`[deprecated]`**.

**«Which endpoints are missing in service X» is a two-part question**, because
the test reports globally and the script has no coverage notion: read `KnownGaps`
out of `SpecPathTests.cs` and filter it to the service, then run
`--spec <service>` for the detail. A green test does **not** mean «nothing
missing» — it means «nothing missing beyond the known gaps», and the known gaps
are usually what the user is asking about.

Facades are not one file per service. `AvailabilityService` lives in
`StorefrontServices.cs`; grep for the path literal — `grep -rn "availability/"
src/Viu.Emporix --include='*.cs'` — rather than looking for a filename.

### The three decision lists

| List | Meaning |
|---|---|
| `ImplementedWithoutAFacade` | reached, but not through a facade — the token endpoints belong to `DefaultTokenProvider`, the customer-session ones to a private helper in `CustomerService`. Covered by their own tests. |
| `Superseded` | the specification marks it deprecated **and** the SDK uses its replacement |
| `KnownGaps` | Emporix offers it, the SDK does not wrap it, and someone chose that |

**Check `[deprecated]` before building anything.** `SpecPathTests` does not
exclude deprecated operations, so one appears as a gap like any other, and
wrapping an endpoint Emporix has already retired is work that then has to be
supported until it is removed again.

At the time of writing, all five entries in `KnownGaps` carry `deprecated: true`
upstream — a sunset with no replacement, so neither `Superseded` (which assumes
one) nor the list's own «does not implement yet» comment fits. Re-check rather
than trusting this paragraph; it is exactly the kind of statement that goes
stale. If it still holds, the action is not to build them: correct the comment,
and expect to empty `KnownGaps` when upstream removes the operations, because
the test asserts set equality in both directions.

When a deprecation decides the answer, find the **removal date** — the changelog
entry and the service's documentation page have disagreed about it, so quote
both rather than picking one.

## 4. What the tests cannot see

`SpecPathTests` finds missing *paths*. Two things get past it, and they are
often the more valuable half of a sync:

**New behaviour on an existing path** — a new `502`, a validation that now runs
before a write, a field that turns out to be immutable, a merge semantic. Zero
missing endpoints, real consequences for callers. It belongs in the PR body, and
where it changes what a caller must do, in the facade's XML docs.

**New fields — and whether they are actually reachable.** A field on a named
schema arrives free: facades return generated types, so it is usable the moment
the spec is vendored. But «vendored» is not «reachable». `attachmentId` was
added as the alternative half of the attachment upload body, and the facade
built its multipart unconditionally with a file part — the field was in the spec
and could not be sent at all. No test reports that, because the path is covered
and only the shape moved.

**Answer it on the type the facade takes, not on the one the schema names.**
The same week produced a case that looked identical and was not: `eventScopes`
was added inside `AgentTrigger`, a bare `oneOf` that NSwag renders as a class
with nothing but `AdditionalProperties`, which throws on every write. Reported
as unreachable, and wrong — no facade uses `AgentTrigger`. A
`GeneratedCodeFixer` rule had already retyped the property to
`ICollection<JsonElement>` on the base the agent request inherits, so a caller
can set the field today, and `AgentTrigger` is dead code referenced by nothing.

So, in order: find the property a caller would set, on the type a facade method
actually accepts; follow it to its declaration; and only then decide. A class
that carries the schema's name may be nothing but an artefact — the generate log
names every property the fixers retyped, which is the fastest way to notice.

Read the changelog for the same reason — <https://developer.emporix.io/changelog>,
through the **Emporix documentation MCP connector**. It is large; grep the saved
file for the `{% update date="…" %}` blocks newer than the last sync. Skip this
step when `fetch` reported no change; there is nothing to read about.

## 5. Implement the gaps

**Zero missing endpoints is a normal, frequent outcome.** Of three specs that
moved on 2026-09-10, exactly one added an operation; the sync before it added
none and only documented a new `502`. Do not manufacture work to fill a PR.

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
whatever the test author already believed. `SpecPathTests` closes the address
half. What is left — a body the API rejects, a response that deserialises to
nothing, a write the server accepts and discards — only a real call finds, which
is `references/live-verification.md`.

## 7. Ship it

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

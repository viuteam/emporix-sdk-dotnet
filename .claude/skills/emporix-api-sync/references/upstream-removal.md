# When upstream moves or removes a spec

The daily sync fails at `Download the specifications` with
`InvalidOperationException: <spec>: 404 from <url>`, run after run. Two
different things produce that 404, and they need opposite fixes — find out
which one happened before editing anything.

## Moved, or removed?

```bash
# Is the directory still there — renamed, perhaps, or with another extension?
gh api repos/emporix/api-references/contents/<area> --jq '.[] | "\(.type)\t\(.path)"'

# Is anything with that name left anywhere in the tree?
gh api "repos/emporix/api-references/git/trees/main?recursive=1" \
  --jq '.tree[] | select(.path | test("<name>"; "i")) | .path'

# Which commit took it away, and when did that reach main?
gh api "repos/emporix/api-references/commits?path=<area>/<dir>&per_page=5" \
  --jq '.[] | "\(.sha[0:10])  \(.commit.committer.date)  \(.commit.message | split("\n")[0])"'
gh api "repos/emporix/api-references/commits/<sha>/pulls" \
  --jq '.[] | "#\(.number)  merged \(.merged_at)  \(.title)"'
```

The commit date is when the change was written, not when it landed. Pick-Pack's
removal commit is dated 2026-08-27 and was merged on 2026-09-16 at 13:10 UTC,
after that day's run and before the next. The merge time is the one that has to
line up with the first red run.

- **Moved or renamed** — a new directory, or `api.yml` become `api.yaml`; three
  entries in `SpecCatalog.cs` already use `.yaml`. Fix the URL in
  `tools/Viu.Emporix.SpecSync/SpecCatalog.cs`; nothing else changes and nothing
  breaks.
- **Removed** — nothing left in the tree, and the changelog carries a «removal of
  deprecated endpoints» entry calling the service End of Life. The upstream PR's
  diff of `changelog/README.md` shows that entry with its date, which is quicker
  than finding it on the rendered changelog. Remove the service from the SDK.

Confirm a removal live before calling it one: a `GET` on a few of its paths with
a service token, next to one on a service that works. Pick-Pack answered `404`
with an empty body on all three paths tried, with a token that read the tenant's
customer segments fine — the gateway no longer knows the routes. A probe like
that is read-only; `live-verification.md` has the rules for anything that is
not.

## Removing a service

A breaking change, `feat!:`, in its own PR with nothing else in it — no sync, no
facade work. While the version is `0.x`, `bump-minor-pre-major` turns it into a
minor release; say so in the PR body. Check whether the SDK ever marked the
service `[Obsolete]`. Pick-Pack had not been, so its removal reached callers
without a warning, and the PR body has to say that too.

Everything the service touches:

| Where | What goes |
|---|---|
| `tools/Viu.Emporix.SpecSync/SpecCatalog.cs` | the entry |
| `specs/` | `<svc>.yml`, and its entry in `sync-manifest.json` — by hand, because a `fetch` would vendor everything else as well |
| `src/Viu.Emporix/Generated/` | nothing by hand: `generate` deletes every generated file and writes only what the catalogue lists. Run it and check that no other generated file changed |
| the facade | its file, or its class in a grouped file |
| `JsonContexts.cs` | the service's context |
| `EmporixClient.cs` | the backing field and the property |
| `ServiceCollectionExtensions.cs` | the registration |
| `PublicAPI.Unshipped.txt` | `./scripts/update-public-api.sh` records a `*REMOVED*` line per symbol, which `promote-public-api.sh` applies at release |
| tests | the service's own tests, and its lines in the wiring test |
| docs | the client table in `README.md`, and the counts in `README.md` and `CLAUDE.md` |

No absence test is needed, unlike in the Node SDK: `SpecPathTests` fails as soon
as anything builds a path no specification declares, so a facade that comes back
fails a test on its own.

**Leave history alone:** `CHANGELOG.md`, `docs/analysis.md` and the wave tables
in `docs/roadmap.md` describe the past correctly. A short note under the
roadmap's headline says the service has left since. Then look for what remains:

```bash
git grep -n -i -E "<name-pattern>" -- . ':!CHANGELOG.md' ':!docs/analysis.md'
```

Hits inside an upstream spec are not yours: Pick-Pack left a sequence named
`pickPackNoSequence` in the sequential-id specification.

## Verifying it

- `fetch` **completes** — that is the proof the sync is unblocked. Commit the
  removal first, run `fetch`, read its last line, then `git checkout -- specs/`
  to drop the vendoring again: a sync does not belong in a breaking PR. Keep that
  last line; it is the list the catch-up starts from.
- `dotnet build`, `dotnet test`, and the AOT publish.

## Catching up afterwards

The sync has been blind since the first failure, so catching up is usually more
than a sync. After Pick-Pack, six specifications had moved in eleven days, and
three of them added eleven operations between them. Two ways forward; ask which
one the user wants if it is not obvious:

- **Wait.** Merge the removal. The next scheduled run, or
  `gh workflow run spec-sync.yml`, vendors everything — and fails again at
  `SpecPathTests` if a spec added an operation, with the list to work from.
- **Catch up now.** A sync-and-facades PR stacked on the removal branch,
  following the rest of this skill. CI runs only for pull requests against
  `main`, so a stacked PR shows no checks until GitHub retargets it after its
  parent merges: run the full verification locally, and say so in its
  description.

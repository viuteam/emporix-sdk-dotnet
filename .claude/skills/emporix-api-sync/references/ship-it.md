# Ship it

Commit, then the PR. The PR is where the measurement becomes reviewable, so it
gets the same care as the code.

## Commit subject

Release Please reads the subject and nothing else decides the version:

| Prefix | Effect |
|---|---|
| `feat:` | minor — new methods, new reachable surface |
| `fix:` | patch — a call that was wrong, a body the API rejected, a type that could not deserialise |
| `feat!:` | major — a generated type changed shape, a signature moved |
| `chore:` `ci:` `test:` `docs:` | no release; the change still ships with the next one |

A spec sync that only reworded descriptions is `fix:` — that is what the bot
titles its own PR, deliberately, because `chore` reaches neither the changelog
nor the version and a sync can retype a property. Retitle the bot's PR to
`feat:` when a service gained operations, `feat!:` when a generated type changed
shape.

**Do not put nested parentheses anywhere in the commit body.** Release Please
parses the whole message and silently drops a commit it cannot parse — no
changelog entry, no version bump, and the change ships anyway. A code fence does
not protect them. Write `GetSection of the options`, not `GetSection("x"))`.
`commit-convention.yml` enforces this.

Write the body as prose about what a consumer can now do and what changed in
behaviour, not as a file list. End with the attribution line the repo uses.

## Branch and base

`feat/<service>-<what>` or `fix/<what>`; `chore/<what>` for a
documentation-only follow-up. Base on `main`, unless the work depends on another
open PR — then base on that branch, say so in the first line of the body, and
note that GitHub retargets the base to `main` automatically once the parent
merges.

Never push to `chore/spec-sync`.

## PR

```bash
gh pr create --base main --title "<commit subject>" --body-file <scratchpad>/pr-body.md
```

Write the body to a file first; a heredoc through the shell mangles backticks
and tables. What makes it reviewable:

1. **What changed upstream**, linked — the Emporix changelog entry, and the
   `emporix/api-references` PR when you can find it.
2. **A table of the specs that moved and whether each added an operation.**
   «Three specs changed, exactly one added an endpoint» is the sentence a
   reviewer needs, and it stops them looking for facade work that does not
   exist.
3. **The measurement, with the command that reproduces it.** Name the
   `SpecPathTests` case that failed before and passes now. A reviewer who wants
   to check runs one command.
4. **A table of what was added**: verb, path, scope, and the facade method that
   now wraps it.
5. **What arrived without a diff** — new fields, reachable the moment the spec
   was vendored, and new behaviour on existing paths. A reviewer cannot see
   these in the diff, so if the body omits them they are lost.
6. **What was deliberately left out, and why** — `KnownGaps` entries you chose
   not to fill, and anything the tenant's credentials could not reach, named per
   method as **unverified against a live tenant**. Silence here reads as
   coverage.
7. **Verification**: `dotnet build`, `dotnet test` with the count, the AOT
   publish, and the smoke-test result. Say which test you broke on purpose to
   prove it fails.

When a live probe found something, put the sequence in the body rather than the
conclusion. «The API refused it three times, each time naming the next missing
field» with the three messages in a table tells a reviewer more about the
endpoint than any summary, and it is the part they cannot reconstruct.

Close with:

```
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

## After opening it

Report the link and what a reviewer should look at first. Then watch CI:

```bash
gh pr checks <n>
```

Seven checks run. A PR whose branch was pushed by the scheduled job can sit at
`action_required` — GitHub gates workflow runs for bot-authored branches, and
that gate cannot be lifted from the CLI. Say so rather than merging past it.

## Stop here

Opening the PR is the end of this skill. Merging it, publishing to nuget.org,
and touching the `release-please--branches--main` PR are the user's calls.

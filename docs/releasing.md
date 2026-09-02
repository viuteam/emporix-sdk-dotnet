# Releasing

Releases are cut by [Release Please](https://github.com/googleapis/release-please).
Every push to `main` updates a release pull request; merging that pull request
publishes. The decision to release is a review, not a tag push.

## What you have to do

Write the commit message so that a machine can classify it and a person can read
it. That is the whole contribution to the release process:

```
feat: add the audit log service
fix(cart): send the item YRN rather than the bare id
feat!: require a JsonTypeInfo for cloud functions
docs: explain the localized-string fallback
```

The type in front of the colon decides three things at once — whether the change
appears in the changelog, under which heading, and how the version moves:

| Type | Changelog | Version, before 1.0 |
| --- | --- | --- |
| `feat` | **Added** | minor — 0.1.0 → 0.2.0 |
| `fix` | **Fixed** | patch — 0.1.0 → 0.1.1 |
| `perf`, `refactor` | **Changed** | patch |
| `docs` | **Documentation** | patch |
| `deps` | **Dependencies** | patch |
| `chore`, `test`, `ci`, `build`, `style` | not shown | none |

A `!` after the type, or a `BREAKING CHANGE:` footer, marks a break. While the
version is below 1.0 that bumps the minor rather than the major, which is what
`bump-minor-pre-major` in [release-please-config.json](../release-please-config.json)
says.

A pull request whose title does not parse is rejected by the **Commit convention**
check, because a squash merge puts that title on `main` and an unclassifiable
commit contributes to neither the version nor the changelog. It ships, silently.

## The trap: a commit body that will not parse

Release Please parses the **whole** commit message, not only the subject. A
message its parser rejects is dropped in full: no changelog entry, no version
bump, the change ships regardless, and the only trace is one line in a workflow
log.

The construct that does it is **nested parentheses anywhere in the body** —
including inside a code fence:

```
feat: bind the options from a configuration section

`AddEmporix(builder.Configuration.GetSection("Emporix"))` alongside the
delegate overload.
```

That exact commit vanished from release 0.2.0. The parser stops at the second
opening parenthesis:

```
commit could not be parsed: 0698991 feat: bind the options ...
error message: unexpected token '(' at 3:45, valid tokens [)]
```

A single pair is fine. It is the nesting that ends the parse:

| In a commit body | |
| --- | --- |
| `see AddEmporix for details` | fine |
| `see AddEmporix, second overload` | fine |
| `AddEmporix of GetSection of the options` | fine |
| ``` `AddEmporix(section)` ``` | fine |
| ``` `AddEmporix(GetSection("x"))` ``` | **drops the commit** |

The **Parseable messages** check runs the same library Release Please uses over
every commit a pull request adds and every commit a push brings to `main`, so the
failure is red rather than silent. It cannot be worked around by rewording the
subject — the body is what breaks.

Prose in commit bodies is worth keeping; it is where the reasoning lives now that
the changelog is generated. Just write the code references without nesting the
brackets.

## What happens then

1. Push to `main`. **Release Please** opens or updates a pull request titled
   `chore(main): release <version>`. It contains the version bump, the generated
   changelog section, and nothing else you have to write.
2. The same run then promotes `PublicAPI.Unshipped.txt` into
   `PublicAPI.Shipped.txt` on that branch, builds to prove the promoted baseline
   compiles, and commits it into the pull request. It happens on any push that
   actually changes the release pull request — a `ci:` or `chore:` commit changes
   nothing there, so nothing is promoted either. Read that diff: it is the list
   of symbols the release promises to keep, and the last moment to notice one
   that went public by accident.
3. Merge. Release Please creates the tag and the GitHub release, and the same
   workflow publishes to nuget.org over trusted publishing.

Nothing is published before that merge, and nothing needs a tag pushed by hand.

### Why the checks on the release pull request say «action required»

Because GitHub does not start workflow runs for anything `GITHUB_TOKEN` created —
[release-please documents this](https://github.com/googleapis/release-please-action#github-credentials)
— so the runs for CI and the commit-convention check sit waiting for a manual
approval on the release pull request, and only there.

That is why the API promotion happens inside the Release Please run instead of in
a workflow of its own: a workflow triggered by that pull request would never fire.
The promotion is verified with a Release build in the same run, so the thing that
could actually break is checked either way.

What is lost is the full CI sweep on the release pull request. Two ways to get it
back, when it starts to matter:

- **Approve the runs.** One click per release, on the pull request's checks.
- **Give Release Please a GitHub App token** instead of `GITHUB_TOKEN`, which is
  what the Node SDK does. Then the pull request is authored by the app, its events
  start workflows normally, and CI runs unattended. The cost is an App to create,
  install and keep two secrets for.

Until then: the release pull request changes `CHANGELOG.md`, `version.txt`, the
manifest and the two API files, and nothing else. CI runs in full on `main` right
after the merge, and the publish job runs the tests again before pushing.

## The changelog is thinner than it used to be

Worth saying plainly. Up to 0.1.0 the entries in [CHANGELOG.md](../CHANGELOG.md)
were written by hand, and they explain *why* a change was made and what it would
have cost — the sort of thing a consumer of the package actually needs. Generated
entries are one line per commit, taken from the subject.

The commit body is where the reasoning still belongs, and it is worth writing:
`git log` keeps it, and a reader who follows a changelog line to its commit finds
it. But it does not reach the changelog. If a release deserves a proper account,
edit `CHANGELOG.md` in the release pull request before merging — bearing in mind
that Release Please force-pushes that branch when new commits land on `main`, so
edit it last.

## `version.txt` is not the version

Release Please's `simple` strategy keeps the current version in
[`version.txt`](../version.txt), and it is the file whose change makes the release
pull request visible at a glance. **The package version does not come from it.**
MinVer derives that from the Git tag, which is what `dotnet pack` uses and what
ends up on nuget.org.

So if the two ever disagree, the tag is right and `version.txt` is stale. Fixing
it by editing the file achieves nothing; the tag is the fact.

## Two things that are not automated

**The first release, 0.1.0.** Release Please counts from a published version, and
there is none yet: `.release-please-manifest.json` claims `0.1.0` so that the next
release is 0.2.0, but 0.1.0 itself has to go out through the tag-triggered
[Release (tag)](../.github/workflows/release.yml) workflow. That release also
needs the changelog section it already has, written by hand, which is the better
text anyway.

**Trusted publishing.** Before any release, nuget.org needs a trusted-publishing
policy for this repository, and the repository needs `NUGET_USER`. Without both,
every path fails at the last step. See
[the NuGet documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

`NUGET_USER` is the **personal account of whoever created the policy**, not the
account that owns the packages. The two are different fields and they are usually
different accounts: the policy is owned by the organisation, and created by a
person who belongs to it. nuget.org says so itself when they do not match:

> Make sure you are using the username of the policy creator, not the policy
> owner: No matching trust policy owned by user 'x' was found.

Neither the documentation nor the login action mentions this; the error message
is the only place it is stated. It is a profile name, never an email address.

It works as a repository variable or as a secret, and a variable is the better
place for it. The value is not secret — package owners are public on nuget.org —
and as a secret it is masked only sometimes: GitHub hides it in the step output
while nuget.org's own error response prints it back in the clear. So it buys no
privacy and costs readable logs wherever it does apply.

Which workflow file the policy has to name is the one thing the documentation does
not settle. The publishing job lives in a reusable workflow, so the OIDC token
carries both the entry workflow and the one containing the job, and neither
NuGet's documentation nor the login action says which is compared. Create a policy
for each of `release.yml`, `release-please.yml` and `publish.yml`; the ones that
never match cost nothing.

### Verifying it before the irreversible part

Run **Release (tag)** by hand from the Actions tab. A manual run is always a dry
run: it builds, tests, packs and exchanges the OIDC token, then stops before the
push. It cannot publish, whatever anyone intends — the only way to release is to
push a tag.

A green run proves the policy matches this repository, this workflow file and this
profile name. A red one says which of them is wrong while nothing has been
published yet — the token-exchange failures are specific about what they compared.

The version a dry run packs looks like `0.0.0-alpha.0.28`. That is correct: the
run happens on a branch rather than a tag, and MinVer falls back to a height-based
prerelease when it finds no tag to count from. Nothing is pushed, so the number
does not matter.

## Cutting a release by hand

Still possible, and the only way for 0.1.0:

```bash
git tag v0.1.0
git push origin v0.1.0
```

**This is irreversible.** A version pushed to nuget.org can be unlisted but never
replaced or removed. `MinVerTagPrefix` is `v`, so the tag name is the package
version exactly.

## A one-off prerelease

Put a footer on any commit and Release Please uses that version instead of the one
it derived:

```
Release-As: 0.2.0-preview.1
```

There is no standing prerelease channel, because a 0.x version already says the
surface may move.

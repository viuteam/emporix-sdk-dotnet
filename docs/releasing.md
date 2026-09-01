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

## What happens then

1. Push to `main`. **Release Please** opens or updates a pull request titled
   `chore(main): release <version>`. It contains the version bump, the generated
   changelog section, and nothing else you have to write.
2. The same run then promotes `PublicAPI.Unshipped.txt` into
   `PublicAPI.Shipped.txt` on that branch, builds to prove the promoted baseline
   compiles, and commits it into the pull request. Read that diff: it is the list
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
policy for this repository and for `publish.yml`, and the repository needs the
`NUGET_USER` variable. Without both, every path fails at the last step. See
[the NuGet documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

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

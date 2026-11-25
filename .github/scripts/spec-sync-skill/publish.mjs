/**
 * Publish step of the skill run. Holds the write permissions of the workflow's
 * GITHUB_TOKEN, runs no LLM and executes nothing from the bundle: it validates
 * what the `skill` job handed over, then pushes the follow-up branch and opens
 * or edits its PR, or comments on the sync PR. In `dry` mode it only writes the
 * job summary.
 *
 * A PR the GITHUB_TOKEN opens starts its workflows as «action required», like
 * the sync PR: a maintainer approves the CI run before it starts.
 */
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { MAX_BODY, SKILL_BRANCH, appendSummary, createRunner, formatMarker, isWorkflowBot, parseMarkers } from "./shared.mjs";

const OUTCOMES = new Set(["ship", "nothing"]);
const CHECKS = new Set(["pass", "fail"]);
// What a sync follow-up legitimately touches: the vendored specs and generated
// types (rewritten by SpecSync), facades, tests, the generator's repairs, docs
// and the smoke test. Project files stay out, so no package can be added, and
// the shipped API baseline stays out, so no break can be hidden.
const ALLOWED_PREFIXES = ["specs/", "src/Viu.Emporix/", "tests/", "tools/Viu.Emporix.SpecSync/", "docs/", "samples/"];
const ALLOWED_FILES = new Set(["README.md", "CLAUDE.md"]);
const FORBIDDEN = [/\.(csproj|props|targets|rsp)$/i, /(^|\/)(nuget\.config|global\.json|packages\.lock\.json)$/i, /PublicAPI\.Shipped\.txt$/];
// The pattern commit-convention.yml applies to a PR title.
const CONVENTIONAL = /^(feat|fix|perf|refactor|docs|deps|chore|test|ci|build|style|revert)(\([a-z0-9._-]+\))?!?: .+/;

export function validateOutcome(raw) {
  let value;
  try {
    value = JSON.parse(raw);
  } catch {
    return { ok: false, error: "not JSON" };
  }
  const { outcome, title, checks } = value ?? {};
  if (!OUTCOMES.has(outcome)) return { ok: false, error: `unknown outcome ${JSON.stringify(outcome)}` };
  if (!CHECKS.has(checks)) return { ok: false, error: `unknown checks ${JSON.stringify(checks)}` };
  if (typeof title !== "string" || /[\r\n]/.test(title) || title.length > 200) {
    return { ok: false, error: "title must be one line of at most 200 characters" };
  }
  if (outcome === "ship" && !CONVENTIONAL.test(title)) return { ok: false, error: "ship needs a conventional-commit title" };
  return { ok: true, value: { outcome, title, checks } };
}

export function offendingPaths(files) {
  return files.filter(
    (f) =>
      FORBIDDEN.some((pattern) => pattern.test(f)) ||
      !(ALLOWED_FILES.has(f) || ALLOWED_PREFIXES.some((prefix) => f.startsWith(prefix))),
  );
}

/**
 * Every path the range touches. `--no-renames` lists both sides of a move, so a
 * file outside the scope cannot be deleted by moving it into the scope.
 */
export function changedPaths({ run, base, head }) {
  return run("git", ["diff", "--name-only", "--no-renames", "-z", base, head]).split("\0").filter(Boolean);
}

/** A review, or a branch head the job did not push itself, means a human is working on the PR. */
export function hasHumanWork({ reviewCount, remoteHead, recordedHead }) {
  return reviewCount > 0 || (remoteHead !== null && remoteHead !== recordedHead);
}

export function decidePublish({ mode, skillResult, artifact, validation, commitCount, ancestorOk, offending, bodyLength, existingPr }) {
  const outcome = validation?.ok ? validation.value.outcome : null;
  let reason = "";
  if (skillResult !== "success") reason = `skill job: ${skillResult}`;
  else if (!artifact) reason = "no artifact";
  else if (!validation.ok) reason = `invalid outcome.json: ${validation.error}`;
  else if (outcome === "ship" && commitCount === 0) reason = "outcome ship without commits";
  else if (outcome === "nothing" && commitCount > 0) reason = "commits without outcome ship";
  else if (commitCount > 0 && !ancestorOk) reason = "the bundle rewrites the sync PR's commits";
  else if (offending.length > 0) reason = `paths outside scope: ${offending.join(", ")}`;
  else if (bodyLength > MAX_BODY) reason = `report too long: ${bodyLength} > ${MAX_BODY}`;

  if (mode === "dry") return { kind: "summary", draft: false, reason };
  if (reason) return { kind: "failed", draft: false, reason };
  if (outcome === "nothing") return { kind: "comment-bot", draft: false, reason };
  if (existingPr && hasHumanWork(existingPr)) return { kind: "comment-skill-pr", draft: false, reason };
  return { kind: "pr", draft: mode === "rehearsal" || validation.value.checks === "fail", reason };
}

const read = (path) => (existsSync(path) ? readFileSync(path, "utf8") : null);

export async function main({ env, run, artifactDir, tmpDir }) {
  const { MODE: mode, BASE_SHA: base, BOT_PR: botPr, FINGERPRINT: fp, SKILL_RESULT: skillResult, RUN_URL: runUrl } = env;
  const repo = env.GITHUB_REPOSITORY;
  const prefix = mode === "rehearsal" ? "[rehearsal] " : "";

  const rawOutcome = read(join(artifactDir, "outcome.json"));
  const artifact = rawOutcome !== null;
  const validation = artifact ? validateOutcome(rawOutcome) : null;
  const report = read(join(artifactDir, "report.md")) ?? "";

  // The bundle is only fetched into a ref and inspected; nothing in it runs.
  const bundle = join(artifactDir, "work.bundle");
  let head = null;
  let commitCount = 0;
  let ancestorOk = true;
  let offending = [];
  if (existsSync(bundle)) {
    run("git", ["fetch", bundle, "refs/heads/skill-work:refs/remotes/skill/work"]);
    head = run("git", ["rev-parse", "refs/remotes/skill/work"]);
    commitCount = Number(run("git", ["rev-list", "--count", `${base}..${head}`]));
    try {
      run("git", ["merge-base", "--is-ancestor", base, head]);
    } catch {
      ancestorOk = false;
    }
    offending = offendingPaths(changedPaths({ run, base, head }));
  }

  const ship = validation?.ok && validation.value.outcome === "ship";
  let existingPr = null;
  if (mode !== "dry" && ship) {
    // `--head` matches the branch name only, so fork PRs named like ours are listed too.
    const pr = JSON.parse(
      run("gh", ["pr", "list", "--repo", repo, "--head", SKILL_BRANCH, "--state", "open", "--json", "number,author,body,isDraft,reviews"]),
    ).find((p) => isWorkflowBot(p.author?.login));
    const remoteHead = run("git", ["ls-remote", "origin", `refs/heads/${SKILL_BRANCH}`]).split(/\s/)[0] || null;
    if (pr) {
      existingPr = {
        number: pr.number,
        isDraft: pr.isDraft,
        reviewCount: pr.reviews.length,
        remoteHead,
        recordedHead: parseMarkers(pr.body).at(-1)?.head ?? null,
      };
    } else {
      // No open PR: the lease still guards against a branch someone else pushed meanwhile.
      existingPr = { number: null, isDraft: false, reviewCount: 0, remoteHead, recordedHead: remoteHead };
    }
  }

  const footer = [
    botPr ? `Includes the vendoring of #${botPr}. Merging this PR supersedes it.` : "",
    "Opened by the skill run of `spec-sync.yml` with the workflow's token, so its checks wait for a maintainer to approve them. " +
      `Not called against a live tenant. Run: ${runUrl}`,
  ].filter(Boolean);
  const body = ship
    ? `${report.trimEnd()}\n\n---\n${footer.join("\n\n")}\n\n${formatMarker({ fingerprint: fp, head })}\n`
    : `${prefix}${report.trimEnd()}\n\n${formatMarker({ fingerprint: fp })}\n`;

  const decision = decidePublish({
    mode,
    skillResult,
    artifact,
    validation,
    commitCount,
    ancestorOk,
    offending,
    bodyLength: body.length,
    existingPr: existingPr?.number ? existingPr : null,
  });

  let n = 0;
  const bodyFile = (text) => {
    const file = join(tmpDir, `body-${++n}.md`);
    writeFileSync(file, text);
    return file;
  };
  const comment = (number, text) => run("gh", ["pr", "comment", String(number), "--repo", repo, "--body-file", bodyFile(text)]);
  const failureBody = (reason) =>
    `${prefix}The API sync skill run did not produce a publishable result: ${reason}.\n\n` +
    `Run: ${runUrl}\n\nRerun it with \`workflow_dispatch\`, \`skill_mode: live\`, once the cause is fixed.\n\n` +
    `${formatMarker({ fingerprint: fp })}\n`;

  let { kind, reason } = decision;
  try {
    write();
  } catch (error) {
    // Record the fingerprint anyway: without a marker the schedule would rerun Claude daily.
    kind = "failed";
    reason = `publish failed: ${error.message.split("\n")[0]}`;
    try {
      if (botPr) comment(botPr, failureBody(reason));
    } catch (commentError) {
      console.error(`publish: the failure comment failed too: ${commentError.message}`);
    }
  }

  appendSummary(
    [
      `### API sync skill: ${kind}`,
      `- mode: \`${mode}\`, outcome: \`${validation?.ok ? validation.value.outcome : "none"}\`, commits: ${commitCount}`,
      reason ? `- reason: ${reason}` : "- reason: none",
      "",
      mode === "dry" ? report : "",
    ].join("\n"),
  );
  return { kind, reason };

  function write() {
    switch (decision.kind) {
      case "pr": {
        const title = `${prefix}${validation.value.title}`;
        run("git", [
          "push",
          `--force-with-lease=refs/heads/${SKILL_BRANCH}:${existingPr.remoteHead ?? ""}`,
          "origin",
          `${head}:refs/heads/${SKILL_BRANCH}`,
        ]);
        if (existingPr.number) {
          run("gh", ["pr", "edit", String(existingPr.number), "--repo", repo, "--title", title, "--body-file", bodyFile(body)]);
          if (decision.draft && !existingPr.isDraft) run("gh", ["pr", "ready", String(existingPr.number), "--repo", repo, "--undo"]);
        } else {
          run("gh", [
            "pr", "create", "--repo", repo, "--base", "main", "--head", SKILL_BRANCH,
            "--title", title, "--body-file", bodyFile(body), ...(decision.draft ? ["--draft"] : []),
          ]);
        }
        break;
      }
      case "comment-bot":
        comment(botPr, body);
        break;
      case "comment-skill-pr":
        comment(
          existingPr.number,
          `${prefix}New upstream spec content was found, but this PR was not updated: it has a review, ` +
            `or commits the workflow did not push. Run: ${runUrl}\n\n${formatMarker({ fingerprint: fp })}\n`,
        );
        break;
      case "failed":
        if (botPr) comment(botPr, failureBody(decision.reason));
        break;
    }
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const result = await main({
    env: process.env,
    run: createRunner(),
    artifactDir: process.env.ARTIFACT_DIR,
    tmpDir: tmpdir(),
  });
  console.log(`publish: ${JSON.stringify(result)}`);
  if (result.reason) process.exitCode = 1;
}

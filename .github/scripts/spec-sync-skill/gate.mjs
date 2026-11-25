/**
 * Gate of the skill run: decides whether Claude runs, on which base, and writes
 * the context file the skill reads instead of calling `gh` itself.
 *
 * Outputs (GITHUB_OUTPUT): run, mode, base_sha, bot_pr, fingerprint.
 */
import { createHash } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { BOT_BRANCH, SKILL_BRANCH, createRunner, isWorkflowBot, parseMarkers, writeOutputs } from "./shared.mjs";

const SCHEDULE_MODES = new Set(["rehearsal", "live"]);
const DISPATCH_MODES = new Set(["dry", "rehearsal", "live"]);
const SPEC = /^specs\/([^/]+)\.yml$/;
const BRANCH_NAME = /^[A-Za-z0-9][A-Za-z0-9._/-]*$/;

/** Schedules read the repository variable, a dispatch its input; anything unknown is `off`. */
export function resolveMode({ eventName, variable, input }) {
  if (eventName === "schedule") return SCHEDULE_MODES.has(variable) ? variable : "off";
  if (eventName === "workflow_dispatch") return DISPATCH_MODES.has(input) ? input : "off";
  return "off";
}

/** sha256 over the blob ids of the vendored `*.yml` specs; the manifest's timestamps change daily. */
export function fingerprint(lsTree) {
  const specs = lsTree
    .split("\n")
    .map((line) => line.match(/^\d+ blob ([0-9a-f]+)\t(.+\.yml)$/))
    .filter(Boolean)
    .map(([, oid, path]) => ({ oid, path }))
    .sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  if (specs.length === 0) throw new Error("no vendored specs found");
  return createHash("sha256")
    .update(specs.map(({ oid, path }) => `${oid} ${path}\n`).join(""))
    .digest("hex");
}

export function changedServices(files) {
  return files
    .map((f) => f.match(SPEC)?.[1])
    .filter(Boolean)
    .sort();
}

export function decide({ mode, eventName, botPr, baseRef, fingerprint, seen }) {
  if (mode === "off") return { run: false, base: null, reason: "mode off" };
  // A rehearsal base lets a dry run reproduce a past sync without its PR. Only a
  // dry run takes one (main refuses it otherwise), so it never feeds a publish.
  if (baseRef) return { run: true, base: "ref", reason: `dry run on ${baseRef}` };
  if (!botPr) {
    return mode === "dry"
      ? { run: true, base: "main", reason: "no open sync PR; dry run on main" }
      : { run: false, base: null, reason: "no open sync PR" };
  }
  // A human dispatch is a deliberate rerun, so only schedules skip content already handled.
  if (eventName === "schedule" && seen.includes(fingerprint)) {
    return { run: false, base: null, reason: `spec content ${fingerprint} already handled` };
  }
  return { run: true, base: "pr", reason: `sync PR #${botPr.number}` };
}

/**
 * Fingerprints recorded by the workflow; a marker anyone else writes is
 * ignored. `--head` matches the branch name only, so a fork PR named like the
 * follow-up branch is listed too: filter on the author before taking the latest.
 */
function seenFingerprints({ run, repo, botPr }) {
  const texts = [];
  const skillPr = JSON.parse(
    run("gh", ["pr", "list", "--repo", repo, "--head", SKILL_BRANCH, "--state", "all", "--limit", "50", "--json", "author,body,comments"]),
  ).find((pr) => isWorkflowBot(pr.author?.login));
  if (skillPr) {
    texts.push(skillPr.body);
    texts.push(...skillPr.comments.filter((c) => isWorkflowBot(c.author?.login)).map((c) => c.body));
  }
  const { comments } = JSON.parse(run("gh", ["pr", "view", String(botPr.number), "--repo", repo, "--json", "comments"]));
  texts.push(...comments.filter((c) => isWorkflowBot(c.author?.login)).map((c) => c.body));
  return texts.flatMap(parseMarkers).map((m) => m.fingerprint);
}

export async function main({ env, run, outDir }) {
  const repo = env.GITHUB_REPOSITORY;
  const eventName = env.GITHUB_EVENT_NAME;
  const mode = resolveMode({ eventName, variable: env.SPEC_SYNC_SKILL, input: env.SKILL_MODE });
  const baseRef = (env.BASE_REF ?? "").trim();
  const skip = { run: "false", mode, base_sha: "", bot_pr: "", fingerprint: "" };
  if (mode === "off") return skip;
  if (baseRef && mode !== "dry") throw new Error("base_ref only applies to dry runs");
  if (baseRef && !BRANCH_NAME.test(baseRef)) throw new Error(`base_ref is not a branch name: ${JSON.stringify(baseRef)}`);

  let botPr = null;
  if (!baseRef) {
    const prs = JSON.parse(
      run("gh", [
        "pr", "list", "--repo", repo, "--head", BOT_BRANCH, "--state", "open",
        "--json", "number,headRefOid,title,body,url,author,files",
      ]),
    );
    botPr = prs.find((pr) => isWorkflowBot(pr.author?.login)) ?? null;
  }
  const baseSha = botPr
    ? botPr.headRefOid
    : run("git", ["rev-parse", baseRef ? `refs/remotes/origin/${baseRef}` : "refs/remotes/origin/main"]);
  const fp = fingerprint(run("git", ["ls-tree", "-r", baseSha, "--", "specs"]));
  const seen = eventName === "schedule" && botPr ? seenFingerprints({ run, repo, botPr }) : [];

  const verdict = decide({ mode, eventName, botPr, baseRef, fingerprint: fp, seen });
  const outputs = { run: String(verdict.run), mode, base_sha: "", bot_pr: botPr ? String(botPr.number) : "", fingerprint: fp };
  if (!verdict.run) return outputs;

  const files = botPr
    ? botPr.files.map((f) => f.path)
    : baseRef
      ? run("git", ["diff", "--name-only", `refs/remotes/origin/main...${baseSha}`, "--", "specs"]).split("\n")
      : [];

  mkdirSync(outDir, { recursive: true });
  const context = {
    mode,
    baseSha,
    baseRef: baseRef || null,
    botPr: botPr && { number: botPr.number, title: botPr.title, body: botPr.body, url: botPr.url },
    changedServices: changedServices(files),
    fingerprint: fp,
  };
  writeFileSync(join(outDir, "context.json"), `${JSON.stringify(context, null, 2)}\n`);
  return { ...outputs, base_sha: baseSha };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const outputs = await main({ env: process.env, run: createRunner(), outDir: process.env.OUT_DIR });
  console.log(`gate: ${JSON.stringify(outputs)}`);
  writeOutputs(outputs);
}

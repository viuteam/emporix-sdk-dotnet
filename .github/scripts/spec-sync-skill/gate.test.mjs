import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { resolveMode, fingerprint, changedServices, decide, main } from "./gate.mjs";
import { formatMarker } from "./shared.mjs";
import { fakeRunner } from "./test-helpers.mjs";

const F = "a".repeat(64);

test("resolveMode", () => {
  const c = (eventName, variable, input) => resolveMode({ eventName, variable, input });
  assert.equal(c("schedule", undefined, undefined), "off");
  assert.equal(c("schedule", "live"), "live");
  assert.equal(c("schedule", "rehearsal"), "rehearsal");
  assert.equal(c("schedule", "dry"), "off"); // dry is dispatch-only
  assert.equal(c("schedule", "LIVE"), "off"); // typos fail safe
  assert.equal(c("workflow_dispatch", "off", "live"), "live");
  assert.equal(c("workflow_dispatch", "live", "off"), "off");
  assert.equal(c("workflow_dispatch", undefined, ""), "off");
  assert.equal(c("push", "live"), "off");
});

const tree = (yml, manifest) =>
  `100644 blob ${yml}\tspecs/ai-service.yml\n` +
  `100644 blob ${manifest}\tspecs/sync-manifest.json\n`;

test("fingerprint ignores the manifest", () =>
  assert.equal(
    fingerprint(tree("1".repeat(40), "2".repeat(40))),
    fingerprint(tree("1".repeat(40), "3".repeat(40))),
  ));

test("fingerprint changes with a spec blob", () =>
  assert.notEqual(
    fingerprint(tree("1".repeat(40), "2".repeat(40))),
    fingerprint(tree("4".repeat(40), "2".repeat(40))),
  ));

test("fingerprint is order-independent and fails without specs", () => {
  const a = `100644 blob ${"1".repeat(40)}\tspecs/a.yml`;
  const b = `100644 blob ${"2".repeat(40)}\tspecs/b.yml`;
  assert.equal(fingerprint(`${a}\n${b}\n`), fingerprint(`${b}\n${a}\n`));
  assert.match(fingerprint(`${a}\n`), /^[0-9a-f]{64}$/);
  assert.throws(() => fingerprint(""));
});

test("changedServices", () =>
  assert.deepEqual(
    changedServices([
      "specs/media.yml",
      "specs/sync-manifest.json",
      "src/Viu.Emporix/Generated/Media.cs",
      "specs/ai-service.yml",
    ]),
    ["ai-service", "media"],
  ));

test("decide", () => {
  const pr = { number: 461 };
  assert.equal(decide({ mode: "off", eventName: "schedule", botPr: pr, fingerprint: F, seen: [] }).run, false);
  assert.deepEqual(
    decide({ mode: "dry", eventName: "workflow_dispatch", botPr: null, fingerprint: F, seen: [] }),
    { run: true, base: "main", reason: "no open sync PR; dry run on main" },
  );
  assert.equal(decide({ mode: "dry", eventName: "workflow_dispatch", botPr: null, baseRef: "x", fingerprint: F, seen: [] }).base, "ref");
  assert.equal(decide({ mode: "live", eventName: "workflow_dispatch", botPr: null, fingerprint: F, seen: [] }).run, false);
  assert.equal(decide({ mode: "live", eventName: "schedule", botPr: pr, fingerprint: F, seen: [F] }).run, false);
  assert.equal(decide({ mode: "live", eventName: "workflow_dispatch", botPr: pr, fingerprint: F, seen: [F] }).run, true);
  assert.equal(decide({ mode: "live", eventName: "schedule", botPr: pr, fingerprint: F, seen: [] }).base, "pr");
});

// --- main ---------------------------------------------------------------------

const PR_HEAD = "c".repeat(40);
const MAIN_HEAD = "d".repeat(40);
const REF_HEAD = "9".repeat(40);
const SPEC_TREE = `100644 blob ${"1".repeat(40)}\tspecs/ai-service.yml\n`;
const FP = fingerprint(SPEC_TREE);
const BOT = { login: "app/github-actions" };

const botPr = (login = BOT.login) => ({
  number: 461,
  headRefOid: PR_HEAD,
  title: "fix: sync generated types with upstream Emporix specifications",
  body: "Automated daily sync",
  url: "https://github.com/R/pull/461",
  author: { login },
  files: [{ path: "specs/ai-service.yml" }, { path: "specs/sync-manifest.json" }],
});

/** Table order matters: the skill-branch key must precede its prefix, the bot-branch key. */
function table({ prs = [botPr()], skillPrs = [], botComments = [] } = {}) {
  return [
    ["gh pr list --repo R --head chore/spec-sync-skill", JSON.stringify(skillPrs)],
    ["gh pr list --repo R --head chore/spec-sync", JSON.stringify(prs)],
    ["git rev-parse refs/remotes/origin/main", MAIN_HEAD],
    ["git rev-parse refs/remotes/origin/rehearsal/base", REF_HEAD],
    ["git ls-tree -r", SPEC_TREE],
    [`git diff --name-only refs/remotes/origin/main...${REF_HEAD} -- specs`, "specs/media.yml\nspecs/sync-manifest.json"],
    ["gh pr view 461 --repo R --json comments", JSON.stringify({ comments: botComments })],
  ];
}

const env = (over) => ({ GITHUB_REPOSITORY: "R", GITHUB_EVENT_NAME: "schedule", SPEC_SYNC_SKILL: "live", ...over });
const outDir = () => join(mkdtempSync(join(tmpdir(), "gate-")), "out"); // not created: main must create it

test("scheduled live run on new content", async () => {
  const dir = outDir();
  const out = await main({ env: env(), run: fakeRunner(table()), outDir: dir });
  assert.deepEqual(out, { run: "true", mode: "live", base_sha: PR_HEAD, bot_pr: "461", fingerprint: FP });
  const ctx = JSON.parse(readFileSync(join(dir, "context.json"), "utf8"));
  assert.deepEqual(ctx.changedServices, ["ai-service"]);
  assert.equal(ctx.baseSha, PR_HEAD);
  assert.equal(ctx.botPr.number, 461);
});

test("scheduled run already handled", async () => {
  const dir = outDir();
  const botComments = [{ author: BOT, body: formatMarker({ fingerprint: FP }) }];
  const out = await main({ env: env(), run: fakeRunner(table({ botComments })), outDir: dir });
  assert.equal(out.run, "false");
  assert.equal(existsSync(join(dir, "context.json")), false);
});

test("scheduled run already handled through the skill PR body", async () => {
  const skillPrs = [{ author: BOT, body: `report\n${formatMarker({ fingerprint: FP, head: PR_HEAD })}`, comments: [] }];
  const out = await main({ env: env(), run: fakeRunner(table({ skillPrs })), outDir: outDir() });
  assert.equal(out.run, "false");
});

test("a foreign PR on the skill branch name does not suppress", async () => {
  // Anyone can open a fork PR whose head is named chore/spec-sync-skill.
  const forged = { author: { login: "mallory" }, body: formatMarker({ fingerprint: FP }), comments: [] };
  const out = await main({ env: env(), run: fakeRunner(table({ skillPrs: [forged] })), outDir: outDir() });
  assert.equal(out.run, "true");
});

test("a foreign PR does not hide the workflow's PR behind it", async () => {
  const forged = { author: { login: "mallory" }, body: "", comments: [] };
  const ours = { author: BOT, body: formatMarker({ fingerprint: FP, head: PR_HEAD }), comments: [] };
  const out = await main({ env: env(), run: fakeRunner(table({ skillPrs: [forged, ours] })), outDir: outDir() });
  assert.equal(out.run, "false");
});

test("human marker does not suppress", async () => {
  const botComments = [{ author: { login: "dominic" }, body: formatMarker({ fingerprint: FP }) }];
  const skillPrs = [{ author: BOT, body: "", comments: [{ author: { login: "dominic" }, body: formatMarker({ fingerprint: FP }) }] }];
  const out = await main({ env: env(), run: fakeRunner(table({ botComments, skillPrs })), outDir: outDir() });
  assert.equal(out.run, "true");
});

test("dry dispatch without a sync PR", async () => {
  const out = await main({
    env: env({ GITHUB_EVENT_NAME: "workflow_dispatch", SKILL_MODE: "dry" }),
    run: fakeRunner(table({ prs: [] })),
    outDir: outDir(),
  });
  assert.equal(out.run, "true");
  assert.equal(out.base_sha, MAIN_HEAD);
  assert.equal(out.bot_pr, "");
});

test("dry dispatch on a rehearsal base ignores the sync PR", async () => {
  const dir = outDir();
  const run = fakeRunner(table());
  const out = await main({
    env: env({ GITHUB_EVENT_NAME: "workflow_dispatch", SKILL_MODE: "dry", BASE_REF: "rehearsal/base" }),
    run,
    outDir: dir,
  });
  assert.deepEqual(out, { run: "true", mode: "dry", base_sha: REF_HEAD, bot_pr: "", fingerprint: FP });
  assert.equal(run.calls.some((c) => c.startsWith("gh ")), false);
  const ctx = JSON.parse(readFileSync(join(dir, "context.json"), "utf8"));
  assert.deepEqual(ctx.changedServices, ["media"]);
  assert.equal(ctx.baseRef, "rehearsal/base");
  assert.equal(ctx.botPr, null);
});

test("a rehearsal base is refused outside a dry run, and so is a malformed one", async () => {
  const dispatch = (over) => main({ env: env({ GITHUB_EVENT_NAME: "workflow_dispatch", ...over }), run: fakeRunner(table()), outDir: outDir() });
  await assert.rejects(dispatch({ SKILL_MODE: "live", BASE_REF: "rehearsal/base" }), /only applies to dry runs/);
  await assert.rejects(dispatch({ SKILL_MODE: "dry", BASE_REF: "--upload-pack=x" }), /not a branch name/);
});

test("dispatch never reads markers", async () => {
  const run = fakeRunner(table());
  await main({ env: env({ GITHUB_EVENT_NAME: "workflow_dispatch", SKILL_MODE: "live" }), run, outDir: outDir() });
  assert.equal(run.calls.some((c) => c.startsWith("gh pr list --repo R --head chore/spec-sync-skill")), false);
  assert.equal(run.calls.some((c) => c.startsWith("gh pr view")), false);
});

test("bot PR by another author is ignored", async () => {
  const out = await main({
    env: env({ GITHUB_EVENT_NAME: "workflow_dispatch", SKILL_MODE: "live" }),
    run: fakeRunner(table({ prs: [botPr("dominic")] })),
    outDir: outDir(),
  });
  assert.equal(out.run, "false");
  assert.equal(out.bot_pr, "");
});

test("mode off reads nothing", async () => {
  const run = fakeRunner(table());
  const out = await main({ env: env({ SPEC_SYNC_SKILL: undefined }), run, outDir: outDir() });
  assert.equal(out.run, "false");
  assert.equal(out.mode, "off");
  assert.deepEqual(run.calls, []);
});

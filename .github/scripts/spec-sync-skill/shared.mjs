/**
 * Helpers shared by the gate and the publish step of the skill run in
 * `.github/workflows/spec-sync.yml`. Node built-ins only: the jobs run these
 * scripts with plain `node`, without installing anything.
 *
 * Ported from the Node SDK (viuteam/emporix-sdk, `.github/scripts/api-sync-skill`).
 */
import { execFileSync } from "node:child_process";
import { appendFileSync } from "node:fs";

export const BOT_BRANCH = "chore/spec-sync";
export const SKILL_BRANCH = "chore/spec-sync-skill";
/** GitHub's limit for a PR body or comment. */
export const MAX_BODY = 65536;

const MARKER = /<!-- emporix-api-sync-skill fingerprint=([0-9a-f]{64}) head=([0-9a-f]{40}|-) -->/g;

/** The marker that records which spec content a run handled, and which head it pushed. */
export function formatMarker({ fingerprint, head }) {
  return `<!-- emporix-api-sync-skill fingerprint=${fingerprint} head=${head ?? "-"} -->`;
}

export function parseMarkers(text) {
  return [...(text ?? "").matchAll(MARKER)].map(([, fingerprint, head]) => ({
    fingerprint,
    head: head === "-" ? null : head,
  }));
}

// Both the sync PR and the skill PR are written with the workflow's GITHUB_TOKEN,
// so their author is GitHub Actions. `gh` reports it as `app/github-actions`, REST
// with the `[bot]` suffix. Only a workflow of this repository can act as it.
const BOT_LOGINS = new Set(["app/github-actions", "github-actions", "github-actions[bot]"]);

export function isWorkflowBot(login) {
  return BOT_LOGINS.has(login);
}

export function writeOutputs(outputs, file = process.env.GITHUB_OUTPUT) {
  const lines = Object.entries(outputs).map(([key, value]) => {
    if (/[\r\n]/.test(value)) throw new Error(`output ${key} must be a single line`);
    return `${key}=${value}\n`;
  });
  if (file) appendFileSync(file, lines.join(""));
}

export function appendSummary(markdown, file = process.env.GITHUB_STEP_SUMMARY) {
  if (file) appendFileSync(file, `${markdown}\n`);
}

/** Runs a command without a shell and returns its trimmed stdout; throws on a non-zero exit. */
export function createRunner({ cwd, env } = {}) {
  return (cmd, args) =>
    execFileSync(cmd, args, {
      cwd,
      env,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "inherit"],
      maxBuffer: 256 * 1024 * 1024,
    }).trimEnd();
}

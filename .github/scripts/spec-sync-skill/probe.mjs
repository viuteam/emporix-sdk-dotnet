/**
 * Dry-run probes of the sandbox Claude's Bash runs in. Claude runs this from
 * the root-owned copy in /opt/spec-sync-skill, so it cannot be edited first.
 * It prints one word per layer and never a value. Expected:
 *
 *   env: false        the OAuth token is not in a subprocess's environment
 *   proc: false       no readable /proc/<pid>/environ holds an Anthropic key
 *   network: blocked  a subprocess cannot reach an outside host
 */
import { readFileSync, readdirSync } from "node:fs";

console.log(`env: ${"CLAUDE_CODE_OAUTH_TOKEN" in process.env}`);

let hit = false;
for (const pid of readdirSync("/proc")) {
  if (!/^\d+$/.test(pid)) continue;
  try {
    if (readFileSync(`/proc/${pid}/environ`, "latin1").includes("sk-ant-")) hit = true;
  } catch {
    // Unreadable is the point.
  }
}
console.log(`proc: ${hit}`);

try {
  await fetch("https://example.com", { signal: AbortSignal.timeout(10_000) });
  console.log("network: open");
} catch {
  console.log("network: blocked");
}

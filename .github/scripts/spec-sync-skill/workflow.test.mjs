// Guard rails for the security-relevant shape of spec-sync.yml. Text checks on
// purpose: there is no YAML parser to hand, and these assert a handful of lines
// a refactor must not lose.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const workflow = readFileSync(fileURLToPath(new URL("../../workflows/spec-sync.yml", import.meta.url)), "utf8");
const job = (name, next) => workflow.slice(workflow.indexOf(`\n  ${name}:\n`), next ? workflow.indexOf(`\n  ${next}:\n`) : undefined);
const syncJob = job("sync", "skill");
const skillJob = job("skill", "publish");
const step = (name, scope = skillJob) => {
  const start = scope.indexOf(`- name: ${name}`);
  assert.notEqual(start, -1, `step ${name} missing`);
  const next = scope.indexOf("\n      - ", start + 1);
  return scope.slice(start, next === -1 ? undefined : next);
};
const allowedTools = () => step("Run the skill").match(/--allowedTools "([^"]+)"/)[1].split(",");

test("a failing test still opens the sync PR, so the skill has something to work on", () => {
  assert.match(step("Test", syncJob), /\n\s+continue-on-error: true\n/);
  assert.equal(/steps\.test\.outcome/.test(step("Create or update the pull request", syncJob).match(/\n\s+if: .*/)[0]), false);
});

test("the skill job saves no package cache after Claude ran", () =>
  assert.equal(/\bcache:/.test(skillJob), false));

test("the skill job holds no write permission and no OIDC token", () => {
  assert.match(skillJob, /permissions:\n\s+contents: read\n\s+pull-requests: read\n/);
  assert.equal(/id-token|: write/.test(skillJob), false);
});

test("the Claude step has its own timeout inside the job's", () =>
  assert.match(step("Run the skill"), /\n\s+timeout-minutes: 45\n/));

test("Claude's file tools are scoped to the paths publish accepts", () => {
  const allowed = allowedTools();
  assert.equal(allowed.includes("Edit"), false);
  assert.equal(allowed.includes("Write"), false);
  for (const tool of ["Edit", "Write"]) {
    for (const scope of ["src/Viu.Emporix/**", "tests/**", "tools/Viu.Emporix.SpecSync/**", "docs/**"]) {
      assert.ok(allowed.includes(`${tool}(${scope})`), `${tool}(${scope})`);
    }
  }
  assert.equal(allowed.some((t) => /^(Edit|Write)\((\.github|\.claude|scripts)/.test(t)), false);
});

test("Claude's dotnet never restores, so nothing reaches for the network or adds a package", () => {
  const dotnet = allowedTools().filter((t) => t.startsWith("Bash(dotnet"));
  assert.ok(dotnet.length > 0);
  for (const tool of dotnet) assert.match(tool, /--no-(restore|build)/, tool);
});

test("no gh, no push, no unrestricted shell for Claude", () => {
  for (const tool of allowedTools()) {
    assert.equal(/^Bash\((gh|git push|curl|wget|sh|bash|env)\b/.test(tool), false, tool);
    assert.notEqual(tool, "Bash");
  }
});

test("post-Claude git runs on the restored config through the protected script", () => {
  const pkg = step("Package and check the result");
  const restore = pkg.indexOf("sudo cat /opt/spec-sync-skill/git-config > .git/config");
  const script = pkg.indexOf("node /opt/spec-sync-skill/package.mjs");
  assert.ok(restore > -1 && script > restore, "restore the config, then run the protected script");
  assert.equal(/\bgit (diff|log)\b/.test(pkg), false, "no porcelain git after Claude");
});

test("the sandbox step unloads Ubuntu's bwrap-userns-restrict AppArmor profile", () => {
  // Ubuntu 26.04 confines bwrap's children to a profile without capabilities,
  // so the sandbox's nested user namespace fails (anthropics/claude-code#105194).
  const deps = step("Install sandbox dependencies");
  assert.match(deps, /apparmor_parser -R \/etc\/apparmor\.d\/bwrap-userns-restrict/);
  assert.match(deps, /kernel\.apparmor_restrict_unprivileged_userns=0/);
});

test("the sandbox's placeholder files are excluded from git before Claude runs", () => {
  const checkout = step("Check out the sync PR with the workflow's skill");
  for (const name of ["/.bashrc", "/.gitconfig", "/.mcp.json", "/.vscode/", "/.sandbox-tmp/"]) {
    assert.ok(checkout.includes(name), name);
  }
  assert.match(checkout, />> \.git\/info\/exclude/);
});

test("the sandbox may write /tmp and nothing else beyond the checkout, and reach no domain", () => {
  // MSBuild binds its worker nodes' pipes under /tmp, ignoring TMPDIR. Without
  // it the first two dry runs failed solution builds with EACCES and MSB1025.
  // A -maxCpuCount:1 in Directory.Build.rsp does not help: the dotnet CLI passes
  // -maxcpucount on the command line, which wins.
  const { sandbox } = JSON.parse(readFileSync(fileURLToPath(new URL("claude-settings.json", import.meta.url)), "utf8"));
  assert.deepEqual(sandbox.filesystem, { allowWrite: ["/tmp"] });
  assert.deepEqual(sandbox.network, { allowedDomains: [] });
});

test("the turn cap leaves room for subagent turns", () => {
  // The action fails the step when the result's num_turns exceeds --max-turns,
  // and that count includes subagent turns: the Node SDK's first rehearsal
  // finished its work in 113 turns under a cap of 80 and was reported as a failure.
  const turns = Number(step("Run the skill").match(/--max-turns (\d+)/)[1]);
  assert.ok(turns >= 200, `--max-turns ${turns}`);
});

test("the upload is gated on the package step", () =>
  assert.match(skillJob, /if: always\(\) && steps\.gate\.outputs\.run == 'true' && steps\.package\.outcome == 'success'/));

test("the protected copies are made before Claude runs", () => {
  const order = ["/opt/spec-sync-skill/$file", "/opt/spec-sync-skill/git-config", "- name: Run the skill"].map((s) => skillJob.indexOf(s));
  assert.ok(order.every((i) => i > -1) && order[0] < order[2] && order[1] < order[2], String(order));
  assert.match(step("Check out the sync PR with the workflow's skill"), /for file in claude-settings\.json package\.mjs probe\.mjs;/);
});

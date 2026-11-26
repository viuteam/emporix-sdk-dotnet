// Real git in throwaway repositories: these properties live in git's behaviour,
// which a fake runner cannot show.
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { collectHaystack, containsToken, packageResult } from "./package.mjs";

const TOKEN = "sk-ant-oat01-test-token-value";
const ID = ["-c", "user.name=t", "-c", "user.email=t@example.com"];

function repo() {
  const dir = mkdtempSync(join(tmpdir(), "pkg-repo-"));
  const git = (...args) => execFileSync("git", ["-C", dir, ...ID, ...args], { encoding: "utf8" }).trim();
  git("init", "-q", "-b", "main");
  writeFileSync(join(dir, "a.txt"), "base\n");
  git("add", "-A");
  git("commit", "-qm", "base");
  const base = git("rev-parse", "HEAD");
  git("switch", "-qc", "skill-work");
  return { dir, git, base };
}

function dirs() {
  const root = mkdtempSync(join(tmpdir(), "pkg-out-"));
  const outDir = join(root, "out");
  const pkgDir = join(root, "pkg");
  mkdirSync(outDir);
  mkdirSync(pkgDir);
  return { outDir, pkgDir };
}

test("containsToken fails closed on an empty token", () => {
  assert.throws(() => containsToken("anything", ""));
  assert.throws(() => containsToken("anything", " \n"));
});

test("containsToken ignores a trailing newline in the stored secret", () => {
  assert.equal(containsToken(`x ${TOKEN} y`, `${TOKEN}\n`), true);
  assert.equal(containsToken("x y", `${TOKEN}\n`), false);
});

test("the haystack holds a token an intermediate commit added and a later one removed", () => {
  const { dir, git, base } = repo();
  writeFileSync(join(dir, "leak.txt"), TOKEN);
  git("add", "-A");
  git("commit", "-qm", "add");
  git("rm", "-q", "leak.txt");
  git("commit", "-qm", "remove");
  assert.equal(containsToken(collectHaystack({ repoDir: dir, base, branch: "skill-work", files: [] }), TOKEN), true);
});

test("the haystack holds a token in an author name", () => {
  const { dir, git, base } = repo();
  writeFileSync(join(dir, "b.txt"), "b\n");
  git("add", "-A");
  git("commit", "-qm", "b", `--author=${TOKEN} <x@example.com>`);
  assert.equal(containsToken(collectHaystack({ repoDir: dir, base, branch: "skill-work", files: [] }), TOKEN), true);
});

test("repo config cannot make the post-Claude git run a command", () => {
  const { dir, git, base } = repo();
  const marker = join(dir, "..", `ran-${Date.now()}`);
  const evil = join(dir, "..", `evil-${Date.now()}.sh`);
  writeFileSync(evil, `#!/bin/sh\ntouch ${marker}\n`);
  chmodSync(evil, 0o755);
  for (const key of ["diff.external", "core.fsmonitor", "core.pager", "core.hooksPath"]) git("config", key, evil);
  writeFileSync(join(dir, "b.txt"), "b\n");
  git("-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "add", "-A");
  git("-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "commit", "-qm", "b");
  const { outDir, pkgDir } = dirs();
  packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "dry", executionFile: "", token: TOKEN });
  assert.equal(existsSync(marker), false);
});

test("a tampered .git (commondir) is refused", () => {
  for (const content of ["/tmp/elsewhere\n", "../other/.git", ""]) {
    const { dir, base } = repo();
    writeFileSync(join(dir, ".git", "commondir"), content);
    const { outDir, pkgDir } = dirs();
    assert.throws(
      () => packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "dry", executionFile: "", token: TOKEN }),
      /tampered/,
      JSON.stringify(content),
    );
  }
});

test("the commondir the Linux sandbox leaves behind (\".\") is accepted", () => {
  // Claude Code's Linux sandbox protects .git/commondir with a one-byte "."
  // placeholder and leaves it on disk (anthropics/claude-code#105401). Git
  // reads "." as .git itself, so it redirects nothing.
  const { dir, git, base } = repo();
  writeFileSync(join(dir, ".git", "commondir"), ".");
  assert.equal(git("rev-parse", "HEAD"), base, "git still works with it");
  const { outDir, pkgDir } = dirs();
  assert.doesNotThrow(() => packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "dry", executionFile: "", token: TOKEN }));
});

test("a clean run bundles the commits and copies the outputs", () => {
  const { dir, git, base } = repo();
  writeFileSync(join(dir, "b.txt"), "b\n");
  git("add", "-A");
  git("commit", "-qm", "b");
  const { outDir, pkgDir } = dirs();
  writeFileSync(join(outDir, "report.md"), "report");
  writeFileSync(join(outDir, "outcome.json"), "{}");
  packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "live", executionFile: "", token: TOKEN });
  assert.deepEqual(readdirSync(pkgDir).sort(), ["outcome.json", "report.md", "work.bundle"]);
  execFileSync("git", ["-C", dir, "bundle", "verify", join(pkgDir, "work.bundle")], { stdio: "ignore" });
});

test("no commits, no bundle", () => {
  const { dir, base } = repo();
  const { outDir, pkgDir } = dirs();
  writeFileSync(join(outDir, "report.md"), "nothing");
  packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "live", executionFile: "", token: TOKEN });
  assert.deepEqual(readdirSync(pkgDir), ["report.md"]);
});

test("a token in the report empties the package and throws", () => {
  const { dir, base } = repo();
  const { outDir, pkgDir } = dirs();
  writeFileSync(join(outDir, "report.md"), `oops ${TOKEN}`);
  writeFileSync(join(outDir, "outcome.json"), "{}");
  assert.throws(
    () => packageResult({ repoDir: dir, base, outDir, pkgDir, mode: "live", executionFile: "", token: TOKEN }),
    /contains the OAuth token/,
  );
  assert.deepEqual(readdirSync(pkgDir), []);
});

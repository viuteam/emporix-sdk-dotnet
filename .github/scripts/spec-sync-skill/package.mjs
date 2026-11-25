/**
 * Packages the skill's result after Claude ran, and checks it for the OAuth
 * token before anything is uploaded (the repository is public, and GitHub masks
 * secrets in logs only).
 *
 * The checkout was writable by the code Claude ran, so this script runs as a
 * copy the workflow put under /opt/spec-sync-skill/ before Claude started, on a
 * .git/config the workflow restored from a copy made at the same time. It uses
 * git plumbing only, with global and system config switched off and the
 * command-running config keys overridden.
 */
import { execFileSync } from "node:child_process";
import { copyFileSync, existsSync, lstatSync, readFileSync, readdirSync, rmSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const BRANCH = "skill-work";
const SAFE = ["-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "-c", "core.pager=cat"];
const ENV = { ...process.env, GIT_CONFIG_GLOBAL: "/dev/null", GIT_CONFIG_NOSYSTEM: "1", GIT_TERMINAL_PROMPT: "0" };

function git(repoDir, args, { input, encoding = "utf8" } = {}) {
  return execFileSync("git", [...SAFE, ...args], {
    cwd: repoDir,
    env: ENV,
    input,
    encoding,
    stdio: ["pipe", "pipe", "inherit"],
    maxBuffer: 512 * 1024 * 1024,
  });
}

/** Fails closed: without a token to compare against, nothing counts as clean. */
export function containsToken(haystack, token) {
  const needle = (token ?? "").trim();
  if (!needle) throw new Error("no token to check against; failing closed");
  return haystack.includes(needle);
}

/** Every object the bundle would carry (commits with their author lines, trees, blobs), plus the given files. */
export function collectHaystack({ repoDir, base, branch, files }) {
  const oids = git(repoDir, ["rev-list", "--objects", `${base}..${branch}`])
    .split("\n")
    .map((line) => line.split(" ")[0])
    .filter(Boolean);
  const objects = oids.length ? git(repoDir, ["cat-file", "--batch"], { input: `${oids.join("\n")}\n`, encoding: "latin1" }) : "";
  return [objects, ...files.filter((f) => existsSync(f)).map((f) => readFileSync(f, "latin1"))].join("\n");
}

function assertIntact(repoDir) {
  const dotGit = join(repoDir, ".git");
  const problems = [];
  if (!lstatSync(dotGit).isDirectory()) problems.push(".git is not a directory");
  else {
    // Claude Code's Linux sandbox protects .git/commondir with a one-byte "."
    // placeholder and leaves it behind (anthropics/claude-code#105401); git
    // reads "." as .git itself. Anything else redirects git or breaks it.
    const commondir = join(dotGit, "commondir");
    if (existsSync(commondir) && readFileSync(commondir, "utf8").trim() !== ".") problems.push(".git/commondir points elsewhere");
    if (!lstatSync(join(dotGit, "config")).isFile()) problems.push(".git/config is not a regular file");
  }
  if (problems.length) throw new Error(`repository tampered: ${problems.join(", ")}`);
}

export function packageResult({ repoDir, base, outDir, pkgDir, mode, executionFile, token }) {
  assertIntact(repoDir);
  const commits = Number(git(repoDir, ["rev-list", "--count", `${base}..${BRANCH}`]).trim());
  if (commits > 0) git(repoDir, ["bundle", "create", "-q", join(pkgDir, "work.bundle"), `${base}..${BRANCH}`]);
  for (const name of ["report.md", "outcome.json"]) {
    if (existsSync(join(outDir, name))) copyFileSync(join(outDir, name), join(pkgDir, name));
  }
  if (mode === "dry" && executionFile && existsSync(executionFile)) copyFileSync(executionFile, join(pkgDir, "execution.json"));

  const files = readdirSync(pkgDir)
    .filter((name) => name !== "work.bundle")
    .map((name) => join(pkgDir, name));
  if (containsToken(collectHaystack({ repoDir, base, branch: BRANCH, files }), token)) {
    for (const name of readdirSync(pkgDir)) rmSync(join(pkgDir, name), { force: true });
    throw new Error("The result contains the OAuth token; nothing is uploaded.");
  }
  return { commits };
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const { commits } = packageResult({
      repoDir: process.cwd(),
      base: process.env.BASE_SHA,
      outDir: join(process.env.RUNNER_TEMP, "spec-sync-skill"),
      pkgDir: join(process.env.RUNNER_TEMP, "spec-sync-skill-artifact"),
      mode: process.env.MODE,
      executionFile: process.env.EXECUTION_FILE,
      token: process.env.TOKEN,
    });
    console.log(`package: ${commits} commit(s), token check passed`);
  } catch (error) {
    console.log(`::error::${error.message.split("\n")[0]}`);
    process.exitCode = 1;
  }
}

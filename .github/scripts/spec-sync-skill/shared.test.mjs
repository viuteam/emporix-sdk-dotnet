import { test } from "node:test";
import assert from "node:assert/strict";
import { formatMarker, parseMarkers, isWorkflowBot, writeOutputs } from "./shared.mjs";

const F = "a".repeat(64);
const H = "b".repeat(40);

test("marker round-trips", () =>
  assert.deepEqual(parseMarkers(`x ${formatMarker({ fingerprint: F, head: H })} y`), [
    { fingerprint: F, head: H },
  ]));

test("a missing head renders as - and parses to null", () =>
  assert.deepEqual(parseMarkers(formatMarker({ fingerprint: F })), [{ fingerprint: F, head: null }]));

test("malformed markers are ignored", () =>
  assert.deepEqual(parseMarkers("<!-- emporix-api-sync-skill fingerprint=abc head=- -->"), []));

test("isWorkflowBot accepts GitHub Actions' login forms only", () => {
  for (const l of ["app/github-actions", "github-actions", "github-actions[bot]"]) {
    assert.equal(isWorkflowBot(l), true, l);
  }
  for (const l of ["app/github-actions-evil", "github-actions-bot", "dominic", ""]) assert.equal(isWorkflowBot(l), false, l);
});

test("writeOutputs rejects a multi-line value", () =>
  assert.throws(() => writeOutputs({ a: "x\ny" }, "/dev/null")));

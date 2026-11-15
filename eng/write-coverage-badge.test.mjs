// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { badgeFor } from "./write-coverage-badge.mjs";

const script = fileURLToPath(new URL("./write-coverage-badge.mjs", import.meta.url));

test("full coverage is reported in green", () => {
  assert.deepEqual(badgeFor({ overall: { line: 1, branch: 1 } }), {
    schemaVersion: 1,
    label: "compiler coverage",
    message: "100% lines, 100% branches",
    color: "brightgreen",
  });
});

test("rates round down and the lower rate picks the colour", () => {
  assert.equal(badgeFor({ overall: { line: 0.99999, branch: 0.96 } }).message, "99.9% lines, 96% branches");
  assert.equal(badgeFor({ overall: { line: 0.99999, branch: 0.96 } }).color, "green");
  assert.equal(badgeFor({ overall: { line: 1, branch: 0.91 } }).color, "yellow");
  assert.equal(badgeFor({ overall: { line: 0.5, branch: 1 } }).color, "red");
});

test("a report without overall rates is rejected", () => {
  assert.throws(() => badgeFor({}), /no overall line and branch rates/);
  assert.throws(() => badgeFor({ overall: { line: 1 } }), /no overall line and branch rates/);
});

test("the command writes the badge beside the report", () => {
  const directory = mkdtempSync(join(tmpdir(), "coverage-badge-"));
  try {
    const report = join(directory, "report.json");
    const badge = join(directory, "badge.json");
    writeFileSync(report, JSON.stringify({ overall: { line: 1, branch: 1 } }));
    const result = spawnSync(process.execPath, [script, report, badge], { encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(JSON.parse(readFileSync(badge, "utf8")).message, "100% lines, 100% branches");
    assert.notEqual(spawnSync(process.execPath, [script, report], { encoding: "utf8" }).status, 0);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

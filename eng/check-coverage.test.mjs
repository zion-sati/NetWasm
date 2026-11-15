// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import test from "node:test";

const checker = fileURLToPath(new URL("./check-coverage.mjs", import.meta.url));

test("wrapped nonzero counts remain covered and untracked modules do not affect overall coverage", () => {
  withFixture(
    {
      "Tracked.dll": moduleCoverage(-1, -2),
      "Untracked.dll": moduleCoverage(0, 0),
    },
    baselineFor("Tracked.dll"),
    ({ result, reportPath }) => {
      assert.equal(result.status, 0, result.stderr);
      const report = JSON.parse(readFileSync(reportPath, "utf8"));
      assert.equal(report["Tracked.dll"].line, 1);
      assert.equal(report["Tracked.dll"].branch, 1);
      assert.equal(report.overall.line, 1);
      assert.equal(report.overall.branch, 1);
    },
  );
});

test("all threshold failures are reported and the report is still written", () => {
  withFixture(
    {
      "First.dll": moduleCoverage(0, 0),
      "Second.dll": moduleCoverage(1, 0),
    },
    {
      modules: {
        "First.dll": { line: 1, branch: 1 },
        "Second.dll": { line: 1, branch: 1 },
      },
      overall: { line: 1, branch: 1 },
    },
    ({ result, reportPath }) => {
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /First\.dll line coverage/);
      assert.match(result.stderr, /First\.dll branch coverage/);
      assert.match(result.stderr, /Second\.dll branch coverage/);
      assert.match(result.stderr, /overall line coverage/);
      assert.match(result.stderr, /overall branch coverage/);
      assert.doesNotThrow(() => JSON.parse(readFileSync(reportPath, "utf8")));
    },
  );
});

function withFixture(coverage, baseline, verify) {
  const directory = mkdtempSync(join(tmpdir(), "netwasm-coverage-"));
  const coveragePath = join(directory, "coverage.json");
  const baselinePath = join(directory, "baseline.json");
  const reportPath = join(directory, "report.json");
  try {
    writeFileSync(coveragePath, JSON.stringify(coverage));
    writeFileSync(baselinePath, JSON.stringify(baseline));
    const result = spawnSync(
      process.execPath,
      [checker, coveragePath, baselinePath, reportPath],
      { encoding: "utf8" },
    );
    verify({ result, reportPath });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

function moduleCoverage(lineHits, branchHits) {
  return {
    "source.cs": {
      Type: {
        Method: {
          Lines: { 1: lineHits },
          Branches: [{ Hits: branchHits }],
        },
      },
    },
  };
}

function baselineFor(moduleName) {
  return {
    modules: { [moduleName]: { line: 1, branch: 1 } },
    overall: { line: 1, branch: 1 },
  };
}

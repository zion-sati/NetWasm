// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { readFile, writeFile } from "node:fs/promises";

if (process.argv.length !== 5) {
  throw new Error(
    "usage: node check-coverage.mjs <coverlet-json> <baseline-json> <report-json>");
}

const [coveragePath, baselinePath, reportPath] = process.argv.slice(2);
const coverage = JSON.parse(await readFile(coveragePath, "utf8"));
const baseline = JSON.parse(await readFile(baselinePath, "utf8"));
const report = {};
let overall = {
  coveredLines: 0,
  lines: 0,
  coveredBranches: 0,
  branches: 0,
};

for (const [moduleName, files] of Object.entries(coverage)) {
  const totals = {
    coveredLines: 0,
    lines: 0,
    coveredBranches: 0,
    branches: 0,
  };
  for (const classes of Object.values(files)) {
    for (const methods of Object.values(classes)) {
      for (const method of Object.values(methods)) {
        for (const hits of Object.values(method.Lines ?? {})) {
          totals.lines++;
          if (hits !== 0) totals.coveredLines++;
        }
        for (const branch of method.Branches ?? []) {
          totals.branches++;
          if (branch.Hits !== 0) totals.coveredBranches++;
        }
      }
    }
  }
  report[moduleName] = rates(totals);
  if (Object.hasOwn(baseline.modules, moduleName)) {
    overall.coveredLines += totals.coveredLines;
    overall.lines += totals.lines;
    overall.coveredBranches += totals.coveredBranches;
    overall.branches += totals.branches;
  }
}
report.overall = rates(overall);

const failures = [];
for (const [moduleName, minimum] of Object.entries(baseline.modules)) {
  if (!report[moduleName]) {
    failures.push(`Coverage report did not contain ${moduleName}.`);
    continue;
  }
  failures.push(...findFailures(moduleName, report[moduleName], minimum));
}
failures.push(...findFailures("overall", report.overall, baseline.overall));
await writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`);
if (failures.length !== 0) {
  throw new Error(failures.join("\n"));
}
console.log(JSON.stringify(report, null, 2));

function rates(totals) {
  return {
    ...totals,
    line: totals.lines === 0 ? 1 : totals.coveredLines / totals.lines,
    branch: totals.branches === 0
      ? 1
      : totals.coveredBranches / totals.branches,
  };
}

function findFailures(name, actual, minimum) {
  const failures = [];
  for (const metric of ["line", "branch"]) {
    if (actual[metric] + Number.EPSILON < minimum[metric]) {
      failures.push(
        `${name} ${metric} coverage ${(actual[metric] * 100).toFixed(2)}% is below ${(minimum[metric] * 100).toFixed(2)}%`,
      );
    }
  }
  return failures;
}

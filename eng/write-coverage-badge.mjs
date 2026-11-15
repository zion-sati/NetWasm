// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Writes a shields.io endpoint badge from check-coverage.mjs's report: the
// overall line and branch coverage of the compiler assemblies the baseline
// tracks. Rates round down, so the badge never shows 100% for less.
import { readFile, writeFile } from "node:fs/promises";

export function badgeFor(report) {
  const overall = report?.overall;
  if (!overall || !Number.isFinite(overall.line) || !Number.isFinite(overall.branch)) {
    throw new Error("The coverage report has no overall line and branch rates.");
  }
  const percent = rate => `${Math.floor(rate * 1000) / 10}%`;
  const lowest = Math.min(overall.line, overall.branch);
  return {
    schemaVersion: 1,
    label: "compiler coverage",
    message: `${percent(overall.line)} lines, ${percent(overall.branch)} branches`,
    color: lowest >= 1 ? "brightgreen" : lowest >= 0.95 ? "green" : lowest >= 0.9 ? "yellow" : "red",
  };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  if (process.argv.length !== 4) {
    throw new Error("usage: node write-coverage-badge.mjs <coverage-report-json> <badge-json>");
  }
  const report = JSON.parse(await readFile(process.argv[2], "utf8"));
  await writeFile(process.argv[3], `${JSON.stringify(badgeFor(report), null, 2)}\n`);
}

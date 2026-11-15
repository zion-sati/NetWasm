// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import { run } from "./browser/BrowserTimeZone.mjs";

function check(condition, label) {
  if (!condition) throw new Error(label);
}

async function zone(runner, TZ) {
  const lines = [];
  const outcome = await runner({ environment: { TZ }, stdout: line => lines.push(line) });
  return { outcome, output: lines.join("\n") };
}

const result = document.querySelector("#result");
try {
  document.body.dataset.testCase = "selected";
  const melbourne = await zone(run, "Australia/Melbourne");
  check(melbourne.outcome.exitCode === 0, `Selected zone failed: ${JSON.stringify(melbourne.outcome)}`);
  check(melbourne.output === "Australia/Melbourne 2026-01-15 11:00", `Melbourne output: ${melbourne.output}`);
  const newYork = await zone(run, "America/New_York");
  check(newYork.output === "America/New_York 2026-01-14 19:00", `New York output: ${newYork.output}`);

  document.body.dataset.testCase = "utc";
  const utc = await zone(run, "UTC");
  check(utc.outcome.exitCode === 0 && utc.output.endsWith("2026-01-15 00:00"), `UTC output: ${utc.output}`);

  // A zone the project did not select fails before managed code runs.
  document.body.dataset.testCase = "unselected";
  const tokyo = await zone(run, "Asia/Tokyo");
  check(tokyo.outcome.primaryFailure !== null && tokyo.output === "",
    `Unselected zone did not fail: ${JSON.stringify(tokyo.outcome)}`);

  // The same deployment from another path: the server alters only its sidecar.
  document.body.dataset.testCase = "tampered";
  const copy = await import("./tampered/browser/BrowserTimeZone.mjs");
  const tampered = await zone(copy.run, "Australia/Melbourne");
  check(tampered.outcome.primaryFailure !== null && tampered.output === "",
    `Tampered sidecar was accepted: ${JSON.stringify(tampered.outcome)}`);
  const copiedUtc = await zone(copy.run, "UTC");
  check(copiedUtc.outcome.exitCode === 0, `Copied deployment failed: ${JSON.stringify(copiedUtc.outcome)}`);

  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  result.textContent = String(error?.stack ?? error);
  document.body.dataset.state = "failed";
}

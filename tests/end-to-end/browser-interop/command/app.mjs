// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import { executeNetWasm } from "./browser/netwasm.browser.mjs";

const result = document.querySelector("#result");
try {
  const bytes = await (await fetch("./browser/deployment.json")).arrayBuffer();
  const manifest = JSON.parse(new TextDecoder().decode(bytes));
  const digest = [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))]
    .map(value => value.toString(16).padStart(2, "0")).join("");
  const errors = [];
  const outcome = await executeNetWasm({
    request: {
      schemaVersion: 1,
      buildFingerprint: manifest.buildFingerprint,
      deploymentManifestSha256: digest,
      arguments: [],
      environment: [],
      grants: { clocks: ["wall", "monotonic"], environment: [], network: "denyAll",
        preopens: [], randomness: true, temporaryDirectory: false, workingDirectory: false },
      applicationImports: [],
    },
    signal: null,
    stdin: null,
    stdout: Object.freeze({ write() {} }),
    stderr: Object.freeze({ write(value) { errors.push(new TextDecoder().decode(value)); } }),
  });
  if (outcome.completionKind !== "normal" || outcome.exitCode !== 0 || errors.length !== 0)
    throw new Error("Command failed: " + JSON.stringify({ outcome, errors }));
  if (document.querySelector("#managed").textContent !== "completed:42"
      || document.querySelector("#released").textContent !== "released")
    throw new Error("Page callback or subscription disposal failed.");
  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  result.textContent = describe(error);
  document.body.dataset.state = "failed";
}

// The error's message and stack: WebKit's and Firefox's stacks leave out the message, which V8's starts with.
function describe(error) {
  const text = String(error);
  return error?.stack?.startsWith(text) ? error.stack : [text, error?.stack].filter(Boolean).join("\n");
}

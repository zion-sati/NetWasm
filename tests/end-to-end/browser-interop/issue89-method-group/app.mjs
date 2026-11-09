import { executeNetWasm } from "./browser/netwasm.browser.mjs";

// The request code from issue #89, which builds the request by hand.
async function start() {
  const bytes = await (await fetch("./browser/deployment.json")).arrayBuffer();
  const manifest = JSON.parse(new TextDecoder().decode(bytes));
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  const sha256 = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
  const sink = Object.freeze({ write(b) { console.log(new TextDecoder().decode(b)); } });
  return executeNetWasm({
    request: {
      schemaVersion: 1,
      buildFingerprint: manifest.buildFingerprint,
      deploymentManifestSha256: sha256,
      arguments: [], environment: [],
      grants: { environment: [], preopens: [], network: "denyAll", clocks: ["monotonic"], randomness: true },
      applicationImports: [],
    },
    stdout: sink, stderr: sink, stdin: null, signal: null,
  });
}

function finish(state, text) {
  document.querySelector("#result").textContent = text;
  document.body.dataset.state = state;
}

try {
  const outcome = await start();
  if (outcome.exitCode !== 0 || globalThis.reproReported !== "value of x") {
    throw new Error(`Unexpected outcome ${JSON.stringify(outcome)} reporting ${globalThis.reproReported}`);
  }
  finish("passed", "passed");
} catch (error) {
  finish("failed", String(error?.stack ?? error));
}

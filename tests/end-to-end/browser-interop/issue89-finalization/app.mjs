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
      grants: { environment: [], preopens: [], network: "denyAll", clocks: ["monotonic"], randomness: true, temporaryDirectory: false, workingDirectory: false },
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
  const layout = await (await fetch("./browser/runtime-layout.json")).json();
  if (!layout.runtimeFeatures.includes("finalization")) {
    throw new Error(`The fixture no longer reaches finalization: ${layout.runtimeFeatures}`);
  }
  const ready = new Promise(resolve => { globalThis.reproReady = resolve; });
  const outcome = await Promise.race([ready.then(() => "ready"), start()]);
  if (outcome !== "ready") throw new Error(`Main finished first: ${JSON.stringify(outcome)}`);
  const doubled = globalThis.reproCallback(21);
  if (doubled !== 42) throw new Error(`callback(21) = ${doubled}`);
  finish("passed", `callback(21) = ${doubled}`);
} catch (error) {
  finish("failed", String(error?.stack ?? error));
}

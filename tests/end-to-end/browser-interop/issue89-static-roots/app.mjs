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
  const ready = new Promise(resolve => { globalThis.reproReady = resolve; });
  const outcome = await Promise.race([ready.then(() => "ready"), start()]);
  if (outcome !== "ready") throw new Error(`Main finished first: ${JSON.stringify(outcome)}`);
  const callback = globalThis.reproCallback;
  for (let i = 0; i < 1000; i++) callback(i);
  // The median of five timed runs of 20,000 calls, as microseconds per call.
  const runs = [];
  for (let run = 0; run < 5; run++) {
    const start = performance.now();
    for (let i = 0; i < 20_000; i++) callback(i);
    runs.push((performance.now() - start) * 1000 / 20_000);
  }
  const microseconds = runs.sort((a, b) => a - b)[2];
  if (callback(41) !== 42) throw new Error("callback returned the wrong value");
  document.body.dataset.metrics = JSON.stringify({ microsecondsPerCall: microseconds });
  finish("passed", `${microseconds.toFixed(2)} us per JS -> C# callback`);
} catch (error) {
  finish("failed", String(error?.stack ?? error));
}

import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { loadStackTraceSymbols } from "./stack-trace-symbol-loader.mjs";

// A method-symbols sidecar with one frame, 7: "Example.Worker.Run()".
const name = "Example.Worker.Run()";
const artifact = new Uint8Array(92 + name.length * 2);
const view = new DataView(artifact.buffer);
view.setUint32(0, 0x5953574E, true);
view.setUint16(4, 1, true);
view.setUint16(6, 2, true);
view.setInt32(72, 1, true);
view.setInt32(76, 1, true);
view.setInt32(80, 7, true);
view.setInt32(84, 0, true);
view.setInt32(88, name.length, true);
for (let index = 0; index < name.length; index++) view.setUint16(92 + index * 2, name.charCodeAt(index), true);

test("loadStackTraceSymbols loads and validates a sidecar", async () => {
  assert.deepEqual(await loadStackTraceSymbols("App.wasm.method-symbols", async url => {
    assert.equal(url, "App.wasm.method-symbols");
    return { ok: true, arrayBuffer: async () => artifact.buffer.slice(0) };
  }), [{ id: 7, name: "Example.Worker.Run()" }]);
});

test("loadStackTraceSymbols uses the platform fetch by default", { concurrency: false }, async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async url => {
    assert.equal(url, "default.method-symbols");
    return { ok: true, arrayBuffer: async () => artifact.buffer.slice(0) };
  };
  try {
    assert.deepEqual(await loadStackTraceSymbols("default.method-symbols"), [
      { id: 7, name: "Example.Worker.Run()" },
    ]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("loadStackTraceSymbols rejects invalid dependencies and transport failures", async () => {
  for (const url of [null, ""]) {
    await assert.rejects(
      () => loadStackTraceSymbols(url, async () => ({})),
      error => error instanceof NetWasmHostError && /URL is required/i.test(error.message));
  }
  await assert.rejects(
    () => loadStackTraceSymbols("App.wasm.method-symbols", null),
    error => error instanceof NetWasmHostError && /requires fetch/i.test(error.message));
  await assert.rejects(
    () => loadStackTraceSymbols("App.wasm.method-symbols", async () => {
      throw new Error("offline");
    }),
    error => error instanceof NetWasmHostError && /offline/i.test(error.message));
  await assert.rejects(
    () => loadStackTraceSymbols("App.wasm.method-symbols", async () => ({ ok: false, status: 404 })),
    error => error instanceof NetWasmHostError && /HTTP 404/i.test(error.message));
});

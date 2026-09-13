import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { loadStackTraceSymbols } from "./stack-trace-symbol-loader.mjs";

const artifact = JSON.stringify({
  schemaVersion: 1,
  methods: [{ id: 7, name: "Example.Worker.Run()" }],
});

test("loadStackTraceSymbols loads and validates a sidecar", async () => {
  assert.deepEqual(await loadStackTraceSymbols("symbols.json", async url => {
    assert.equal(url, "symbols.json");
    return { ok: true, text: async () => artifact };
  }), [{ id: 7, name: "Example.Worker.Run()" }]);
});

test("loadStackTraceSymbols uses the platform fetch by default", { concurrency: false }, async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async url => {
    assert.equal(url, "default.json");
    return { ok: true, text: async () => artifact };
  };
  try {
    assert.deepEqual(await loadStackTraceSymbols("default.json"), [
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
    () => loadStackTraceSymbols("symbols.json", null),
    error => error instanceof NetWasmHostError && /requires fetch/i.test(error.message));
  await assert.rejects(
    () => loadStackTraceSymbols("symbols.json", async () => {
      throw new Error("offline");
    }),
    error => error instanceof NetWasmHostError && /offline/i.test(error.message));
  await assert.rejects(
    () => loadStackTraceSymbols("symbols.json", async () => ({ ok: false, status: 404 })),
    error => error instanceof NetWasmHostError && /HTTP 404/i.test(error.message));
});

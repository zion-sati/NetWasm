import assert from "node:assert/strict";
import test from "node:test";
import { importLocalVerifiedModule } from "./local-verified-module-importer.mjs";

const encoder = new TextEncoder();

test("imports an owned byte snapshot under an isolated verified URL", async () => {
  const bytes = encoder.encode("export const value = 41;\n");
  const loading = importLocalVerifiedModule(bytes, { relativePath: "assets/module #1.mjs" });
  bytes.fill(0);
  const namespace = await loading;
  assert.equal(namespace.value, 41);
});

test("rejects invalid verified-module inputs before registering hooks", async () => {
  for (const bytes of [null, new ArrayBuffer(1), [], "source"]) {
    await assert.rejects(() => importLocalVerifiedModule(bytes, { relativePath: "module.mjs" }), /bytes/);
  }
  for (const artifact of [null, 1, {}, { relativePath: null }, { relativePath: "" }]) {
    await assert.rejects(() => importLocalVerifiedModule(Uint8Array.of(), artifact), /artifact/);
  }
});

test("rejects dependencies while allowing unrelated module resolution and deregisters afterward", async () => {
  let signalReady;
  let releaseImport;
  const ready = new Promise(resolve => { signalReady = resolve; });
  globalThis.__netwasmVerifiedReady = signalReady;
  globalThis.__netwasmVerifiedGate = new Promise(resolve => { releaseImport = resolve; });
  const loading = importLocalVerifiedModule(encoder.encode(`
globalThis.__netwasmVerifiedReady();
await globalThis.__netwasmVerifiedGate;
export const value = 42;
`), { relativePath: "active.mjs" });
  try {
    await ready;
    assert.equal((await import("data:text/javascript,export const value=43#verified-unrelated")).value, 43);
    releaseImport();
    assert.equal((await loading).value, 42);
  } finally {
    releaseImport();
    delete globalThis.__netwasmVerifiedReady;
    delete globalThis.__netwasmVerifiedGate;
  }

  await assert.rejects(() => importLocalVerifiedModule(encoder.encode(`
import "node:assert/strict";
export const value = 1;
`), { relativePath: "dependent.mjs" }), /cannot import dependencies/);
  assert.equal((await import("node:assert/strict")).default.ok(true), undefined);
});

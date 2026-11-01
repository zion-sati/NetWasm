import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import { NetWasmUnsupportedBrowserError, webPlatform } from "./browser-web-platform.mjs";

// Replaces a global for one test and puts the original back afterwards.
async function withGlobal(name, value, action) {
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, name);
  Object.defineProperty(globalThis, name, { configurable: true, enumerable: false, value, writable: true });
  try {
    return await action();
  } finally {
    if (descriptor === undefined) delete globalThis[name];
    else Object.defineProperty(globalThis, name, descriptor);
  }
}

const empty = Uint8Array.of(0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00);
const hex = digest => Buffer.from(digest).toString("hex");

test("exposes exactly the web actions a browser host takes", () => {
  assert.deepEqual(Object.keys(webPlatform).sort(),
    ["compileCoreModule", "createModuleUrl", "digest", "fetch", "importModule", "revokeModuleUrl"]);
  assert.ok(Object.isFrozen(webPlatform));
});

// Issue #139: browsers provide crypto.subtle only in a secure context.
test("hashes with crypto.subtle, and in JavaScript where a page has none", async () => {
  const bytes = new TextEncoder().encode("NetWasm");
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  assert.equal(hex(await webPlatform.digest("SHA-256", bytes)), sha256);

  const real = globalThis.crypto;
  let subtleCalls = 0;
  const subtle = { digest: (algorithm, data) => { subtleCalls++; return real.subtle.digest(algorithm, data); } };
  await withGlobal("crypto", { subtle, getRandomValues: real.getRandomValues.bind(real) }, async () => {
    assert.equal(hex(await webPlatform.digest("SHA-256", bytes)), sha256);
  });
  assert.equal(subtleCalls, 1);

  await withGlobal("crypto", { getRandomValues: real.getRandomValues.bind(real) }, async () => {
    const digest = await webPlatform.digest("SHA-256", bytes);
    assert.ok(digest instanceof ArrayBuffer);
    assert.equal(hex(digest), sha256);
  });
  await withGlobal("crypto", undefined, async () => {
    assert.equal(hex(await webPlatform.digest("SHA-256", bytes)), sha256);
  });
  await assert.rejects(webPlatform.digest("SHA-1", bytes), { name: "TypeError", message: /SHA-1/u });
});

test("compiles a module and names a browser that lacks NetWasm's WebAssembly features", async () => {
  assert.ok(await webPlatform.compileCoreModule(empty) instanceof WebAssembly.Module);

  // A compile error in a browser that has the features is the module's own.
  const invalid = Uint8Array.of(0x00, 0x61, 0x73, 0x6d, 0x02, 0x00, 0x00, 0x00);
  await assert.rejects(webPlatform.compileCoreModule(invalid), { name: "CompileError" });

  const compileError = new WebAssembly.CompileError("invalid opcode 0x1f");
  const older = { compile: async () => { throw compileError; }, validate: () => false };
  await withGlobal("WebAssembly", older, async () => {
    const failure = await webPlatform.compileCoreModule(empty).catch(error => error);
    assert.ok(failure instanceof NetWasmUnsupportedBrowserError);
    assert.equal(failure.name, "NetWasmUnsupportedBrowserError");
    assert.match(failure.message, /exception handling and SIMD.*Safari 18\.4/u);
    assert.equal(failure.cause, compileError);
  });
  // Other failures, and compile errors where the features are present, pass through.
  const linkError = new TypeError("not a module");
  await withGlobal("WebAssembly", { compile: async () => { throw linkError; }, validate: () => false }, async () => {
    await assert.rejects(webPlatform.compileCoreModule(empty), error => error === linkError);
  });
  await withGlobal("WebAssembly", { compile: async () => { throw compileError; }, validate: () => true }, async () => {
    await assert.rejects(webPlatform.compileCoreModule(empty), error => error === compileError);
  });
  for (const missing of [undefined, null]) {
    await withGlobal("WebAssembly", missing, async () => {
      await assert.rejects(webPlatform.compileCoreModule(empty),
        { name: "NetWasmUnsupportedBrowserError", message: /WebAssembly is unavailable/u });
    });
  }
});

test("looks every browser API up when the action runs", async () => {
  const calls = [];
  await withGlobal("fetch", async (input, init) => { calls.push(["fetch", input, init]); return "response"; }, async () => {
    assert.equal(await webPlatform.fetch("https://example.test/a", { signal: null }), "response");
  });
  const url = webPlatform.createModuleUrl(new TextEncoder().encode("export const value = 42;"));
  assert.match(url, /^blob:/u);
  assert.equal((await webPlatform.importModule("data:text/javascript,export const value = 7;")).value, 7);
  webPlatform.revokeModuleUrl(url);
  await withGlobal("URL", { revokeObjectURL: value => calls.push(["revoke", value]) }, () => {
    webPlatform.revokeModuleUrl("blob:revoked");
  });
  assert.deepEqual(calls, [["fetch", "https://example.test/a", { signal: null }], ["revoke", "blob:revoked"]]);
});

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLocalComponentLoader } from "./local-component-loader.mjs";

const adapterSource = new TextEncoder().encode(`
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generatedModule) {
  return Object.freeze({ contractKey, instantiate: generatedModule.instantiate });
}
`);
const generatedSource = new TextEncoder().encode(`
let invocation = 0;
export function instantiate() {
  invocation++;
  return { command: { run() { return invocation; } } };
}
`);
const coreModule = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);

test("loads verified local files once and isolates generated module state", async () => {
  await withDeployment(async fixture => {
    const load = createLocalComponentLoader({ manifestPath: fixture.manifestPath });
    assert.equal(Object.isFrozen(load), true);
    const [first, second] = await Promise.all([
      load(fixture.request),
      load({ ...fixture.request, signal: new AbortController().signal }),
    ]);
    assert.equal(Object.isFrozen(first), true);
    assert.equal(first.adapter.contractKey, "wasi-command@0.2.11");
    assert.equal((await first.adapter.instantiate()).command.run(), 1);
    assert.equal((await second.adapter.instantiate()).command.run(), 1);
    assert.equal(first.loadCoreModule("app-component.core.wasm") instanceof WebAssembly.Module, true);
  });
});

test("rejects tampered artifacts before evaluating JavaScript", async () => {
  await withDeployment(async fixture => {
    const load = createLocalComponentLoader({ manifestPath: fixture.manifestPath });
    globalThis.__netwasmLocalImportCount = 0;
    await writeFile(fixture.adapterPath, new TextEncoder().encode(`
globalThis.__netwasmLocalImportCount++;
export const contractKey = "wasi-command@0.2.11";
export function createAdapter() { return {}; }
`));
    await assert.rejects(() => load(fixture.request), /integrity/);
    assert.equal(globalThis.__netwasmLocalImportCount, 0);
    delete globalThis.__netwasmLocalImportCount;
  });
});

test("rejects static dependencies in verified local JavaScript", async () => {
  await withDeployment(async fixture => {
    const load = createLocalComponentLoader({ manifestPath: fixture.manifestPath });
    const source = new TextEncoder().encode(`
import "node:assert/strict";
export const contractKey = "wasi-command@0.2.11";
export function createAdapter() { return { contractKey, instantiate() {} }; }
`);
    await writeFile(fixture.adapterPath, source);
    const request = fixture.requestWithArtifactDigest("component-adapter", sha256(source));
    await assert.rejects(() => load(request), /cannot import dependencies/);
    assert.equal((await import("node:assert/strict")).default.ok(true), undefined);
  });
});

test("chains unrelated process module loads while a verified import is active", async () => {
  await withDeployment(async fixture => {
    const load = createLocalComponentLoader({ manifestPath: fixture.manifestPath });
    let signalReady;
    let releaseImport;
    const ready = new Promise(resolve => { signalReady = resolve; });
    globalThis.__netwasmLocalHookReady = signalReady;
    globalThis.__netwasmLocalHookGate = new Promise(resolve => { releaseImport = resolve; });
    const source = new TextEncoder().encode(`
globalThis.__netwasmLocalHookReady();
await globalThis.__netwasmLocalHookGate;
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generatedModule) {
  return { contractKey, instantiate: generatedModule.instantiate };
}
`);
    await writeFile(fixture.adapterPath, source);
    const request = fixture.requestWithArtifactDigest("component-adapter", sha256(source));
    const loading = load(request);
    try {
      await ready;
      const unrelated = await import("data:text/javascript,export const value=42#netwasm-unrelated");
      assert.equal(unrelated.value, 42);
      releaseImport();
      assert.equal((await loading).adapter.contractKey, "wasi-command@0.2.11");
    } finally {
      releaseImport();
      delete globalThis.__netwasmLocalHookReady;
      delete globalThis.__netwasmLocalHookGate;
    }
  });
});

test("validates and binds the canonical manifest path at construction", () => {
  for (const options of [null, 1, [], {}, { manifestPath: "/tmp/x", extra: true },
    Object.create({ manifestPath: "/tmp/x" })]) {
    assert.throws(() => createLocalComponentLoader(options), /options/);
  }
  const withSymbol = { manifestPath: "/tmp/x", [Symbol("invalid")]: true };
  assert.throws(() => createLocalComponentLoader(withSymbol), /options/);
  const withAccessor = {};
  Object.defineProperty(withAccessor, "manifestPath", { get: () => "/tmp/x", enumerable: true });
  assert.throws(() => createLocalComponentLoader(withAccessor), /options/);

  for (const manifestPath of [null, "", "relative/manifest.json", "/tmp/../tmp/manifest.json", "/tmp/bad\0manifest.json"]) {
    assert.throws(() => createLocalComponentLoader({ manifestPath }), /canonical and absolute/);
  }
});

test("rejects invalid bound-loader requests before filesystem access", () => {
  const load = createLocalComponentLoader({ manifestPath: "/tmp/manifest.json" });
  for (const request of [null, 1, [], {}, { artifacts: [], extra: true },
    Object.create({ artifacts: [] })]) {
    assert.throws(() => load(request), /request/);
  }
  const withSymbol = { artifacts: [], [Symbol("invalid")]: true };
  assert.throws(() => load(withSymbol), /request/);
  const withAccessor = {};
  Object.defineProperty(withAccessor, "artifacts", { get: () => [], enumerable: true });
  assert.throws(() => load(withAccessor), /request/);
});

async function withDeployment(action) {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-component-"));
  try {
    const manifestPath = join(root, "app.netwasm.deployment.json");
    const adapterPath = join(root, "app.wasm.adapter.mjs");
    const generatedPath = join(root, "app-component.js");
    const corePath = join(root, "app-component.core.wasm");
    await Promise.all([
      writeFile(adapterPath, adapterSource),
      writeFile(generatedPath, generatedSource),
      writeFile(corePath, coreModule),
    ]);
    const artifacts = [
      artifact("app.wasm", "application", "application/wasm", sha256(Uint8Array.of(1))),
      artifact("app.wasm.adapter.mjs", "component-adapter", "text/javascript", sha256(adapterSource)),
      artifact("app-component.js", "component-javascript", "text/javascript", sha256(generatedSource)),
      artifact("app-component.core.wasm", "component-core-module", "application/wasm", sha256(coreModule)),
    ];
    const request = { artifacts };
    await action({
      request,
      manifestPath,
      adapterPath,
      requestWithArtifactDigest(role, digest) {
        return {
          ...request,
          artifacts: artifacts.map(value => value.role === role ? { ...value, sha256: digest } : value),
        };
      },
    });
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

function artifact(relativePath, role, mediaType, digest) {
  return { relativePath, role, mediaType, sha256: digest, schemaVersion: null };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

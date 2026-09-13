import assert from "node:assert/strict";
import test from "node:test";
import { loadComponentArtifacts } from "./component-artifact-loader.mjs";

const digest = character => character.repeat(64);
const artifact = (relativePath, role, mediaType, sha256, schemaVersion = null) => ({
  relativePath,
  role,
  mediaType,
  sha256,
  schemaVersion,
});
const artifacts = () => [
  artifact("publish/app.wasm", "application", "application/wasm", digest("1")),
  artifact("publish/app.wasm.adapter.mjs", "component-adapter", "text/javascript", digest("2")),
  artifact("publish/app-component.js", "component-javascript", "text/javascript", digest("3")),
  artifact("publish/app-component.core2.wasm", "component-core-module", "application/wasm", digest("4")),
  artifact("publish/app-component.core.wasm", "component-core-module", "application/wasm", digest("5")),
];
const markerDigests = new Map([[2, digest("2")], [3, digest("3")], [4, digest("4")], [5, digest("5")]]);

test("verifies the complete closure before importing and compiling exact owned bytes", async () => {
  const calls = [];
  const transportBuffers = new Map();
  const adapter = Object.freeze({ contractKey: "wasi-command@0.2.11", instantiate() {} });
  const loaded = await loadComponentArtifacts({
    deploymentKind: "component",
    artifacts: artifacts(),
    async readArtifact(value, signal) {
      assert.equal(signal, null);
      const marker = Number(value.sha256[0]);
      const bytes = Uint8Array.of(marker, 9);
      transportBuffers.set(value.relativePath, bytes);
      calls.push(`read:${marker}`);
      return bytes;
    },
    async hashBytes(bytes) {
      const marker = bytes[0];
      bytes[0] = 255;
      calls.push(`hash:${marker}`);
      return markerDigests.get(marker);
    },
    async importModule(bytes, value) {
      const marker = bytes[0];
      assert.equal(bytes[1], 9);
      calls.push(`import:${marker}`);
      if (value.role === "component-adapter") {
        return {
          contractKey: "wasi-command@0.2.11",
          createAdapter(generatedModule) {
            assert.equal(this.contractKey, "wasi-command@0.2.11");
            assert.equal(generatedModule.marker, 3);
            return adapter;
          },
        };
      }
      return { marker };
    },
    async compileCoreModule(bytes) {
      calls.push(`compile:${bytes[0]}`);
      return { marker: bytes[0] };
    },
  });

  assert.equal(Object.isFrozen(loaded), true);
  assert.equal(loaded.adapter, adapter);
  assert.deepEqual(loaded.loadCoreModule("app-component.core.wasm"), { marker: 5 });
  assert.deepEqual(loaded.loadCoreModule("app-component.core2.wasm"), { marker: 4 });
  assert.throws(() => loaded.loadCoreModule("APP-component.core.wasm"), /unavailable/);
  const firstUse = calls.findIndex(value => value.startsWith("import:") || value.startsWith("compile:"));
  assert.equal(calls.slice(0, firstUse).filter(value => value.startsWith("hash:")).length, 4);
  assert.equal(calls.slice(firstUse).some(value => value.startsWith("hash:")), false);
  for (const bytes of transportBuffers.values()) assert.notEqual(bytes[0], 255);
});

test("loads browser artifacts through the identical shared contract", async () => {
  const loaded = await loadComponentArtifacts(createRequest({ deploymentKind: "browser" }));
  assert.equal(loaded.adapter.contractKey, "wasi-command@0.2.11");
});

test("rejects integrity failures before importing or compiling", async () => {
  const calls = [];
  const request = createRequest({
    hashBytes: async bytes => {
      calls.push(`hash:${bytes[0]}`);
      return bytes[0] === 4 ? digest("f") : markerDigests.get(bytes[0]);
    },
    importModule: async () => { calls.push("import"); return {}; },
    compileCoreModule: async () => { calls.push("compile"); return {}; },
  });
  await assert.rejects(() => loadComponentArtifacts(request), /integrity/);
  assert.equal(calls.includes("import"), false);
  assert.equal(calls.includes("compile"), false);
});

test("rejects invalid hasher output and transport bytes before use", async () => {
  for (const invalidDigest of [null, "", digest("A"), "a".repeat(63), "g".repeat(64)]) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({
      hashBytes: async () => invalidDigest,
    })), /hasher/);
  }
  for (const bytes of [null, new ArrayBuffer(2), [1, 2], "bytes"]) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({
      readArtifact: async () => bytes,
    })), /reader returned invalid bytes/);
  }
});

test("cancellation prevents I/O or stops before hashing and module use", async () => {
  const before = new AbortController();
  before.abort();
  let reads = 0;
  await assert.rejects(() => loadComponentArtifacts(createRequest({
    signal: before.signal,
    readArtifact: async () => { reads++; return Uint8Array.of(2); },
  })), error => error.name === "AbortError");
  assert.equal(reads, 0);

  const during = new AbortController();
  let hashes = 0;
  await assert.rejects(() => loadComponentArtifacts(createRequest({
    signal: during.signal,
    async readArtifact() {
      during.abort();
      return Uint8Array.of(2);
    },
    hashBytes: async () => { hashes++; return digest("2"); },
  })), error => error.name === "AbortError");
  assert.equal(hashes, 0);
});

test("validates the complete request and dependencies before I/O", async () => {
  for (const request of [null, 1, [], {}, { ...createRequest(), extra: true }, Object.create(createRequest())]) {
    await assert.rejects(() => loadComponentArtifacts(request), /request/);
  }
  const withSymbol = { ...createRequest(), [Symbol("invalid")]: true };
  await assert.rejects(() => loadComponentArtifacts(withSymbol), /request/);
  const withAccessor = { ...createRequest() };
  Object.defineProperty(withAccessor, "readArtifact", { get: () => async () => {}, enumerable: true });
  await assert.rejects(() => loadComponentArtifacts(withAccessor), /request/);
  for (const key of ["readArtifact", "hashBytes", "importModule", "compileCoreModule"]) {
    await assert.rejects(() => loadComponentArtifacts({ ...createRequest(), [key]: null }), /required/);
  }
  for (const signal of [{}, { aborted: false }, {
    aborted: false,
    addEventListener() {},
  }]) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({ signal })), /AbortSignal/);
  }
});

test("rejects invalid adapter namespaces and adapter factory results", async () => {
  const invalidNamespaces = [null, 1, {}, { contractKey: "x", createAdapter() {}, extra: true },
    { contractKey: "", createAdapter() {} }, { contractKey: "x", createAdapter: null }];
  for (const namespace of invalidNamespaces) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({
      importModule: async (_bytes, value) => value.role === "component-adapter" ? namespace : { marker: 3 },
    })), /adapter module/);
  }

  for (const result of [null, 1, [], {}, { contractKey: "x", instantiate() {}, extra: true },
    { contractKey: "", instantiate() {} }, { contractKey: "x", instantiate: null }]) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({
      importModule: async (_bytes, value) => value.role === "component-adapter"
        ? { contractKey: "x", createAdapter: () => result }
        : { marker: 3 },
    })), /adapter factory/);
  }
});

test("rejects an invalid compiled core module", async () => {
  for (const compiled of [null, undefined, 1, "module"]) {
    await assert.rejects(() => loadComponentArtifacts(createRequest({
      compileCoreModule: async () => compiled,
    })), /compiler returned/);
  }
});

function createRequest(overrides = {}) {
  return {
    deploymentKind: "component",
    artifacts: artifacts(),
    readArtifact: async value => Uint8Array.of(Number(value.sha256[0]), 9),
    hashBytes: async bytes => markerDigests.get(bytes[0]),
    importModule: async (_bytes, value) => value.role === "component-adapter"
      ? {
        contractKey: "wasi-command@0.2.11",
        createAdapter: () => ({ contractKey: "wasi-command@0.2.11", instantiate() {} }),
      }
      : { marker: 3 },
    compileCoreModule: async bytes => ({ marker: bytes[0] }),
    ...overrides,
  };
}

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import {
  loadRawArtifacts,
  projectRawModuleInventory,
} from "./raw-artifact-loader.mjs";

const encoder = new TextEncoder();
const application = Buffer.from(
  "AGFzbQEAAAABBQFgAAF/AhsBD25ldHdhc20uaG9zdC52MQdzZXJ2aWNlAAADAgEABQMBAAEHEAIGbWVtb3J5AgADcnVuAAEKBgEEABAACwAdBG5hbWUBCgEAB3NlcnZpY2UECgEAB3NlcnZpY2U=",
  "base64");
const adapterSource = encoder.encode("export const rawAdapterMetadata = {}; export function createAdapter() {}\n");
const layout = Object.freeze({
  schemaVersion: 2,
  target: "wasm32",
  applicationStaticDataEnd: 64,
  managedExecutableEntryPoint: Object.freeze({
    parameterShape: "none",
    returnShape: "exitCode",
    completionShape: "synchronous",
  }),
});
const interop = Object.freeze({
  version: 1,
  target: "wasm32",
  statusAbi: Object.freeze({ successStatus: 0 }),
  imports: Object.freeze([]),
  exports: Object.freeze([]),
});
const layoutBytes = encoder.encode(JSON.stringify(layout));
const interopBytes = encoder.encode(JSON.stringify(interop));
const adapter = Object.freeze({ rawAdapterMetadata: Object.freeze({}), createAdapter() {} });

test("verifies the complete closure before loading and returns the inspected immutable ABI", async () => {
  const calls = [];
  const transportBuffers = [];
  const request = createRequest({
    async readArtifact(value, signal) {
      assert.equal(signal, null);
      const bytes = new Uint8Array(content().get(value.role));
      transportBuffers.push(bytes);
      calls.push(`read:${value.role}`);
      return bytes;
    },
    async hashBytes(bytes) {
      const hash = sha256(bytes);
      bytes.fill(255);
      calls.push("hash");
      return hash;
    },
    async compileModule(bytes, value) {
      calls.push(`compile:${value.role}`);
      assert.deepEqual(bytes, new Uint8Array(application));
      return WebAssembly.compile(bytes);
    },
    async importModule(bytes, value) {
      calls.push(`import:${value.role}`);
      assert.deepEqual(bytes, adapterSource);
      return adapter;
    },
  });
  const loaded = await loadRawArtifacts(request);

  assert.equal(Object.isFrozen(loaded), true);
  assert.equal(loaded.adapter, adapter);
  assert.equal(loaded.module instanceof WebAssembly.Module, true);
  assert.deepEqual(loaded.abi, {
    target: "wasm32",
    entryPoint: layout.managedExecutableEntryPoint,
    imports: [{ module: "netwasm.host.v1", name: "service", kind: "function" }],
    exports: [{ name: "memory", kind: "memory" }, { name: "run", kind: "function" }],
  });
  assert.equal(Object.isFrozen(loaded.abi), true);
  assert.equal(Object.isFrozen(loaded.abi.imports), true);
  assert.equal(Object.isFrozen(loaded.abi.imports[0]), true);
  assert.equal(Object.isFrozen(loaded.abi.exports), true);
  assert.equal(Object.isFrozen(loaded.abi.exports[0]), true);
  assert.equal(Object.isFrozen(loaded.runtimeLayout), true);
  assert.equal(Object.isFrozen(loaded.runtimeLayout.managedExecutableEntryPoint), true);
  assert.equal(Object.isFrozen(loaded.interopManifest), true);
  assert.equal(Object.isFrozen(loaded.interopManifest.statusAbi), true);
  const firstUse = calls.findIndex(value => value.startsWith("compile:") || value.startsWith("import:"));
  assert.equal(calls.slice(0, firstUse).filter(value => value === "hash").length, 4);
  assert.equal(calls.slice(firstUse).includes("hash"), false);
  for (const bytes of transportBuffers) assert.notEqual(bytes[0], 255);
});

test("rejects integrity and transport failures before loading executable content", async () => {
  const calls = [];
  await assert.rejects(() => loadRawArtifacts(createRequest({
    hashBytes: async bytes => bytes[0] === application[0] ? "f".repeat(64) : sha256(bytes),
    compileModule: async () => { calls.push("compile"); return {}; },
    importModule: async () => { calls.push("import"); return {}; },
  })), /integrity/);
  assert.deepEqual(calls, []);

  for (const invalidDigest of [null, "", "A".repeat(64), "a".repeat(63), "g".repeat(64)]) {
    await assert.rejects(() => loadRawArtifacts(createRequest({
      hashBytes: async () => invalidDigest,
    })), /hasher/);
  }
  for (const bytes of [null, new ArrayBuffer(2), [1, 2], "bytes"]) {
    await assert.rejects(() => loadRawArtifacts(createRequest({
      readArtifact: async () => bytes,
    })), /reader returned invalid bytes/);
  }
});

test("honors cancellation before I/O and between read, hash, and load stages", async () => {
  const before = new AbortController();
  before.abort();
  let reads = 0;
  await assert.rejects(() => loadRawArtifacts(createRequest({
    signal: before.signal,
    readArtifact: async () => { reads++; return application; },
  })), error => error.name === "AbortError");
  assert.equal(reads, 0);

  const afterRead = new AbortController();
  let hashes = 0;
  await assert.rejects(() => loadRawArtifacts(createRequest({
    signal: afterRead.signal,
    async readArtifact(value) {
      afterRead.abort();
      return content().get(value.role);
    },
    hashBytes: async () => { hashes++; return "a".repeat(64); },
  })), error => error.name === "AbortError");
  assert.equal(hashes, 0);

  const afterHash = new AbortController();
  let loads = 0;
  await assert.rejects(() => loadRawArtifacts(createRequest({
    signal: afterHash.signal,
    async hashBytes(bytes) {
      afterHash.abort();
      return sha256(bytes);
    },
    compileModule: async () => { loads++; return {}; },
  })), error => error.name === "AbortError");
  assert.equal(loads, 0);
});

test("validates the exact loading request and dependencies before I/O", async () => {
  for (const request of [null, 1, [], {}, { ...createRequest(), extra: true }, Object.create(createRequest())]) {
    await assert.rejects(() => loadRawArtifacts(request), /request/);
  }
  const withSymbol = { ...createRequest(), [Symbol("invalid")]: true };
  await assert.rejects(() => loadRawArtifacts(withSymbol), /request/);
  const withAccessor = { ...createRequest() };
  Object.defineProperty(withAccessor, "readArtifact", { get: () => async () => {}, enumerable: true });
  await assert.rejects(() => loadRawArtifacts(withAccessor), /request/);
  for (const key of ["readArtifact", "hashBytes", "importModule", "compileModule"]) {
    await assert.rejects(() => loadRawArtifacts({ ...createRequest(), [key]: null }), /required/);
  }
  for (const signal of [{}, { aborted: false }, {
    aborted: false,
    addEventListener() {},
  }]) {
    await assert.rejects(() => loadRawArtifacts(createRequest({ signal })), /AbortSignal/);
  }
});

test("rejects invalid compiled modules and generated adapter namespaces", async () => {
  for (const module of [null, undefined, {}, 1]) {
    await assert.rejects(() => loadRawArtifacts(createRequest({
      compileModule: async () => module,
    })), /compiler returned/);
  }
  const accessor = {};
  Object.defineProperty(accessor, "createAdapter", { enumerable: true, get() { return () => {}; } });
  Object.defineProperty(accessor, "rawAdapterMetadata", { enumerable: true, value: {} });
  for (const namespace of [null, 1, {},
    { createAdapter() {}, rawAdapterMetadata: {}, extra: true },
    { createAdapter: null, rawAdapterMetadata: {} },
    { createAdapter() {}, rawAdapterMetadata: null },
    accessor]) {
    await assert.rejects(() => loadRawArtifacts(createRequest({
      importModule: async () => namespace,
    })), /adapter module/);
  }
});

test("projects duplicate physical imports into one logical ABI identity", () => {
  const inventory = projectRawModuleInventory([
    { module: "host", name: "member", kind: "function" },
    { module: "host", name: "member", kind: "function" },
    { module: "host", name: "other", kind: "memory" },
  ], [{ name: "run", kind: "function" }]);
  assert.deepEqual(inventory, {
    imports: [
      { module: "host", name: "member", kind: "function" },
      { module: "host", name: "other", kind: "memory" },
    ],
    exports: [{ name: "run", kind: "function" }],
  });
  assert.equal(Object.isFrozen(inventory), true);
  assert.equal(Object.isFrozen(inventory.imports), true);
  assert.throws(() => projectRawModuleInventory([
    { module: "host", name: "member", kind: "function" },
    { module: "host", name: "member", kind: "memory" },
  ], []), /conflicting kinds/);
});

test("preserves import tuples containing separator characters", () => {
  const inventory = projectRawModuleInventory([
    { module: "host\u0000member", name: "tail", kind: "function" },
    { module: "host", name: "member\u0000tail", kind: "memory" },
  ], []);
  assert.deepEqual(inventory.imports, [
    { module: "host\u0000member", name: "tail", kind: "function" },
    { module: "host", name: "member\u0000tail", kind: "memory" },
  ]);
});

test("rejects malformed runtime layouts and mismatched interop manifests", async () => {
  const invalidLayouts = [
    Uint8Array.of(255),
    encoder.encode("null"),
    encoder.encode(JSON.stringify({ ...layout, extra: true })),
    encoder.encode(JSON.stringify({ ...layout, schemaVersion: 1 })),
    encoder.encode(JSON.stringify({ ...layout, target: "wasm128" })),
    encoder.encode(JSON.stringify({ ...layout, applicationStaticDataEnd: -1 })),
    encoder.encode(JSON.stringify({ ...layout, applicationStaticDataEnd: 1.5 })),
    encoder.encode(JSON.stringify({ ...layout, managedExecutableEntryPoint: null })),
    encoder.encode(JSON.stringify({ ...layout, managedExecutableEntryPoint: {
      ...layout.managedExecutableEntryPoint, extra: true,
    } })),
  ];
  for (const bytes of invalidLayouts) {
    await assert.rejects(() => loadRawArtifacts(requestWithContent("runtime-layout", bytes)), /layout|entry point/);
  }

  for (const bytes of [
    Uint8Array.of(255),
    encoder.encode("[]"),
    encoder.encode(JSON.stringify({ ...interop, version: 2 })),
    encoder.encode(JSON.stringify({ ...interop, target: "wasm64" })),
  ]) {
    await assert.rejects(() => loadRawArtifacts(requestWithContent("interop-manifest", bytes)), /interop manifest/);
  }
});

function createRequest(overrides = {}) {
  const values = content();
  return {
    deploymentKind: "raw",
    artifacts: artifacts(values),
    readArtifact: async value => values.get(value.role),
    hashBytes: async bytes => sha256(bytes),
    importModule: async () => adapter,
    compileModule: async bytes => WebAssembly.compile(bytes),
    ...overrides,
  };
}

function requestWithContent(role, bytes) {
  const values = content();
  values.set(role, bytes);
  return createRequest({
    artifacts: artifacts(values),
    readArtifact: async value => values.get(value.role),
  });
}

function content() {
  return new Map([
    ["application", new Uint8Array(application)],
    ["raw-adapter", adapterSource],
    ["runtime-layout", layoutBytes],
    ["interop-manifest", interopBytes],
  ]);
}

function artifacts(values) {
  return [
    artifact("publish/app.wasm", "application", "application/wasm", values.get("application")),
    artifact("publish/app.raw-adapter.mjs", "raw-adapter", "text/javascript", values.get("raw-adapter")),
    artifact("publish/runtime-layout.json", "runtime-layout", "application/json", values.get("runtime-layout"), 2),
    artifact("publish/interop.json", "interop-manifest", "application/json", values.get("interop-manifest"), 1),
  ];
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import { readFile } from "node:fs/promises";
import { isAbsolute } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";

const executionRootPath = process.env.NETWASM_HOSTING_BROWSER_EXECUTION_ROOT;
if (executionRootPath !== undefined && !isAbsolute(executionRootPath)) {
  throw new TypeError("NETWASM_HOSTING_BROWSER_EXECUTION_ROOT must be absolute");
}
const executionRootUrl = executionRootPath === undefined
  ? new URL("./browser-execution-root.mjs", import.meta.url)
  : pathToFileURL(executionRootPath);
const { createBrowserNetWasmExecution } = await import(executionRootUrl);
const { createBrowserExecutionComposition } = await import(
  new URL("./browser-execution-composition.mjs", import.meta.url));
const { createBrowserComponentNetWasmExecution } = await import(
  new URL("./browser-component-execution-root.mjs", import.meta.url));
const { createBrowserRawNetWasmExecution } = await import(
  new URL("./browser-raw-execution-root.mjs", import.meta.url));

const digest = "1".repeat(64);
const encoder = new TextEncoder();
const manifestUrl = "https://example.test/deploy/deployment.json";
const application = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);
const rawApplication = Buffer.from(
  "AGFzbQEAAAABDQJgBH9/f38Bf2AAAX8DAwIAAQUDAQABBzEEBm1lbW9yeQIADWNtMzJwMl9tZW1vcnkCAA5jbTMycDJfcmVhbGxvYwAAA3J1bgABCgsCBABBCAsEAEElCw==",
  "base64");
const adapter = encoder.encode(`
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generated) {
  return Object.freeze({ contractKey, instantiate: generated.instantiate });
}
`);
const generated = encoder.encode(`
export function instantiate() { return { command: { run: () => 41 } }; }
`);
const rawAdapter = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({
  abiVersion: 1,
  bindingIdentities: Object.freeze([]),
  requiredCapabilities: Object.freeze([]),
  target: "wasm32",
  witSourceFingerprint: "sha256:${digest}",
});
export function createAdapter() {
  return Object.freeze({ imports: Object.freeze(Object.create(null)), metadata: rawAdapterMetadata });
}
`);
const runtimeLayout = encoder.encode(JSON.stringify({
  schemaVersion: 2,
  target: "wasm32",
  applicationStaticDataEnd: 64,
  managedExecutableEntryPoint: {
    parameterShape: "none", returnShape: "exitCode", completionShape: "synchronous",
  },
}));
const interopManifest = encoder.encode(JSON.stringify({
  version: 1,
  target: "wasm32",
  statusAbi: { successStatus: 0, hostFailureStatus: 1, scalarResultOffset: 0 },
  targetLayout: {
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
  },
  imports: [],
  exports: [],
  callbacks: [],
}));
class Descriptor { openAt() { return new Descriptor(); } read() { return [new Uint8Array(), true]; } }
const sink = Object.freeze({ write() {} });

test("executes a verified URL-backed browser deployment", async () => {
  const artifacts = [
    artifact("app.wasm", "application", "application/wasm", application),
    artifact("app.wasm.adapter.mjs", "component-adapter", "text/javascript", adapter),
    artifact("app-component.js", "component-javascript", "text/javascript", generated),
    artifact("app-component.core.wasm", "component-core-module", "application/wasm", application),
  ];
  const manifestBytes = encoder.encode(JSON.stringify(deployment("browser", artifacts)));
  const fixture = browserPlatform(manifestBytes, new Map([
    ["app.wasm.adapter.mjs", adapter],
    ["app-component.js", generated],
    ["app-component.core.wasm", application],
  ]));
  for (const createExecution of [
    createBrowserNetWasmExecution,
    createBrowserComponentNetWasmExecution,
  ]) {
    fixture.fetches.length = 0;
    const executeNetWasm = createExecution({ manifestUrl, platform: fixture.platform });
    const outcome = await executeNetWasm({
      request: executionRequest(sha256(manifestBytes)), signal: null, stderr: sink, stdout: sink,
    });
    assert.deepEqual([outcome.completionKind, outcome.exitCode], ["normal", 41]);
    assert.deepEqual(fixture.fetches, [
      manifestUrl,
      "https://example.test/deploy/app.wasm.adapter.mjs",
      "https://example.test/deploy/app-component.js",
      "https://example.test/deploy/app-component.core.wasm",
    ]);
  }
});

test("executes a verified URL-backed raw deployment", async () => {
  const artifacts = [
    artifact("app.wasm", "application", "application/wasm", rawApplication),
    artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
    artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
    artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
  ];
  const manifestBytes = encoder.encode(JSON.stringify(deployment("raw", artifacts)));
  const fixture = browserPlatform(manifestBytes, new Map([
    ["app.wasm", rawApplication],
    ["app.raw-adapter.mjs", rawAdapter],
    ["app.runtime-layout.json", runtimeLayout],
    ["app.interop.json", interopManifest],
  ]));
  for (const createExecution of [createBrowserNetWasmExecution, createBrowserRawNetWasmExecution]) {
    fixture.fetches.length = 0;
    const executeNetWasm = createExecution({ manifestUrl, platform: fixture.platform });
    const outcome = await executeNetWasm({
      request: executionRequest(sha256(manifestBytes)), signal: null, stderr: sink, stdout: sink,
    });
    assert.deepEqual([outcome.completionKind, outcome.exitCode], ["normal", 37]);
    assert.deepEqual(fixture.fetches, [
      manifestUrl,
      "https://example.test/deploy/app.wasm",
      "https://example.test/deploy/app.raw-adapter.mjs",
      "https://example.test/deploy/app.runtime-layout.json",
      "https://example.test/deploy/app.interop.json",
    ]);
  }
});

test("rejects host paths at the browser boundary before fetching", async () => {
  const artifacts = [artifact("app.wasm", "application", "application/wasm", application)];
  const manifestBytes = encoder.encode(JSON.stringify(deployment("browser", artifacts)));
  const fixture = browserPlatform(manifestBytes, new Map());
  const executeNetWasm = createBrowserNetWasmExecution({ manifestUrl, platform: fixture.platform });
  const request = executionRequest(sha256(manifestBytes));
  const value = Object.freeze({
    ...request,
    grants: Object.freeze({
      ...request.grants,
      preopens: Object.freeze([Object.freeze({
        hostPath: "/host/data", guestPath: "/data", access: "readOnly",
      })]),
    }),
  });
  const outcome = await executeNetWasm({ request: value, signal: null, stderr: sink, stdout: sink });
  assert.equal(outcome.primaryFailure.code, "contract.invalid");
  assert.deepEqual(fixture.fetches, []);
});

test("rejects local component deployments at the browser Strategy boundary", async () => {
  const artifacts = [artifact("app.wasm", "application", "application/wasm", application)];
  const manifestBytes = encoder.encode(JSON.stringify(deployment("component", artifacts)));
  const fixture = browserPlatform(manifestBytes, new Map());
  for (const createExecution of [
    createBrowserNetWasmExecution,
    createBrowserComponentNetWasmExecution,
    createBrowserRawNetWasmExecution,
  ]) {
    fixture.fetches.length = 0;
    const executeNetWasm = createExecution({ manifestUrl, platform: fixture.platform });
    const outcome = await executeNetWasm({
      request: executionRequest(sha256(manifestBytes)), signal: null, stderr: sink, stdout: sink,
    });
    assert.equal(outcome.primaryFailure.code, "contract.invalid");
    assert.deepEqual(fixture.fetches, [manifestUrl]);
  }
});

test("validates browser composition and contains no Node production imports", async () => {
  const valid = { manifestUrl, platform: browserPlatform(new Uint8Array(), new Map()).platform };
  for (const value of [null, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createBrowserNetWasmExecution(value), TypeError);
  }
  for (const value of [null, {}, { ...valid.platform, extra: () => {} }, Object.create(valid.platform)]) {
    assert.throws(() => createBrowserNetWasmExecution({ ...valid, platform: value }), TypeError);
  }
  for (const name of Object.keys(valid.platform)) {
    assert.throws(() => createBrowserNetWasmExecution({
      ...valid, platform: { ...valid.platform, [name]: null },
    }), /action/);
  }
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserNetWasmExecution(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "manifestUrl", { enumerable: true, get: () => manifestUrl });
  assert.throws(() => createBrowserNetWasmExecution(accessor), TypeError);
  assert.throws(() => createBrowserExecutionComposition(valid, null, null), TypeError);
  assert.throws(() => createBrowserExecutionComposition(valid, () => () => {}, {}), TypeError);
  assert.throws(() => createBrowserExecutionComposition(valid, () => null, null), TypeError);
  const source = await readFile(executionRootUrl, "utf8");
  assert.doesNotMatch(source, /(?:from\s+|import\s*\()["']node:/u);
  assert.doesNotMatch(source, /local-/u);
});

function browserPlatform(manifestBytes, artifacts) {
  const moduleSources = new Map();
  const fetches = [];
  let sequence = 0;
  return {
    fetches,
    platform: {
      compileCoreModule: WebAssembly.compile.bind(WebAssembly),
      createFilesystem: () => ({ types: { Descriptor }, preopens: { getDirectories: () => [] } }),
      createModuleUrl(bytes) {
        const url = `blob:netwasm-root/${++sequence}`;
        moduleSources.set(url, new Uint8Array(bytes));
        return url;
      },
      createShim() { assert.fail("an empty WIT provider set must not create a shim"); },
      digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
      async fetch(url) {
        fetches.push(url);
        const name = url === manifestUrl ? null : url.slice(url.lastIndexOf("/") + 1);
        const bytes = name === null ? manifestBytes : artifacts.get(name);
        return {
          ok: bytes !== undefined,
          async arrayBuffer() {
            return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
          },
        };
      },
      async importModule(url) {
        const source = Buffer.from(moduleSources.get(url)).toString("base64");
        return import(`data:text/javascript;base64,${source}#${encodeURIComponent(url)}`);
      },
      revokeModuleUrl(url) { moduleSources.delete(url); },
    },
  };
}

function deployment(kind, artifacts) {
  return {
    schemaVersion: 1, semanticBuildId: digest, deploymentKind: kind,
    profile: "netwasm0.1", target: "wasm32", featureSet: "default",
    executionContract: "wasi-command@0.2.11",
    versions: { compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1" },
    buildFingerprint: digest, runtimeFeatures: [], artifacts,
    requiredImportModules: [], requiredImports: [], exports: [],
  };
}

function executionRequest(deploymentManifestSha256) {
  return Object.freeze({
    schemaVersion: 1, buildFingerprint: digest, deploymentManifestSha256,
    arguments: Object.freeze([]), environment: Object.freeze([]),
    grants: Object.freeze({
      environment: Object.freeze([]), preopens: Object.freeze([]), network: "denyAll",
      clocks: Object.freeze([]), randomness: false,
    }),
    applicationImports: Object.freeze([]),
  });
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function sha256(bytes) { return createHash("sha256").update(bytes).digest("hex"); }

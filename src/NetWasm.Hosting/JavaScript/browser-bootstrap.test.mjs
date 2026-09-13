import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";

import { createBrowserNetWasmBootstrap } from "./browser-bootstrap.mjs";
import { createBrowserBootstrapComposition } from "./browser-bootstrap-composition.mjs";
import { createBrowserNetWasmBootstrap as createComponentBootstrap } from "./browser-component-bootstrap.mjs";
import { createBrowserNetWasmBootstrap as createRawBootstrap } from "./browser-raw-bootstrap.mjs";

const digest = "1".repeat(64);
const encoder = new TextEncoder();
const manifestUrl = "https://example.test/deploy/deployment.json";
const application = Buffer.from(
  "AGFzbQEAAAABDQJgBH9/f38Bf2AAAX8DAwIAAQUDAQABBzEEBm1lbW9yeQIADWNtMzJwMl9tZW1vcnkCAA5jbTMycDJfcmVhbGxvYwAAA3J1bgABCgsCBABBCAsEAEElCw==",
  "base64");
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

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function deployment(artifacts) {
  return {
    schemaVersion: 1, semanticBuildId: digest, deploymentKind: "raw",
    profile: "netwasm0.1", target: "wasm32", featureSet: "default",
    executionContract: "wasi-command@0.2.11",
    versions: { compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1" },
    buildFingerprint: digest, runtimeFeatures: [], artifacts,
    requiredImportModules: [], requiredImports: [], exports: [],
  };
}

function request(deploymentManifestSha256) {
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

function fixture() {
  const artifacts = [
    artifact("app.wasm", "application", "application/wasm", application),
    artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
    artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
    artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
  ];
  const manifestBytes = encoder.encode(JSON.stringify(deployment(artifacts)));
  const files = new Map([
    [manifestUrl, manifestBytes],
    [new URL("app.wasm", manifestUrl).href, application],
    [new URL("app.raw-adapter.mjs", manifestUrl).href, rawAdapter],
    [new URL("app.runtime-layout.json", manifestUrl).href, runtimeLayout],
    [new URL("app.interop.json", manifestUrl).href, interopManifest],
  ]);
  const modules = new Map();
  let sequence = 0;
  const createFilesystem = () => ({
    types: { Descriptor }, preopens: { getDirectories: () => [] },
  });
  const createShim = () => { throw new Error("empty provider set must not create a shim"); };
  return {
    createFilesystem,
    createShim,
    manifestBytes,
    options: {
      manifestUrl,
      preview2: { createFilesystem, createShim },
      web: {
        compileCoreModule: WebAssembly.compile.bind(WebAssembly),
        createModuleUrl(bytes) {
          const url = `blob:netwasm-bootstrap/${++sequence}`;
          modules.set(url, new Uint8Array(bytes));
          return url;
        },
        digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
        async fetch(url) {
          const bytes = files.get(url);
          return {
            ok: bytes !== undefined,
            async arrayBuffer() {
              return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
            },
          };
        },
        async importModule(url) {
          const source = Buffer.from(modules.get(url)).toString("base64");
          return import(`data:text/javascript;base64,${source}#${encodeURIComponent(url)}`);
        },
        revokeModuleUrl(url) { modules.delete(url); },
      },
    },
  };
}

function malformedDataObjects(value) {
  const first = Object.keys(value)[0];
  const withSymbol = { ...value, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), value);
  const extra = { ...value, extra: true };
  const missing = { ...value };
  delete missing[first];
  const nonEnumerable = { ...value };
  Object.defineProperty(nonEnumerable, first, { value: value[first], enumerable: false });
  const accessor = { ...value };
  Object.defineProperty(accessor, first, { get: () => value[first], enumerable: true });
  return { invalid: [null, 1, [], withSymbol], malformed: [inherited, extra, missing, nonEnumerable, accessor] };
}

test("browser bootstrap composes selected Preview 2 and Web mechanisms", async () => {
  for (const createBootstrap of [createBrowserNetWasmBootstrap, createRawBootstrap]) {
    const value = fixture();
    const executeNetWasm = createBootstrap(value.options);
    assert.equal(Object.isFrozen(executeNetWasm), true);
    const outcome = await executeNetWasm({
      request: request(sha256(value.manifestBytes)),
      signal: null,
      stderr: sink,
      stdout: sink,
    });
    assert.deepEqual([outcome.completionKind, outcome.exitCode], ["normal", 37]);
  }
  assert.equal(typeof createComponentBootstrap(fixture().options), "function");
});

test("browser bootstrap validates its exact three-part composition", () => {
  const base = fixture().options;
  const { invalid, malformed } = malformedDataObjects(base);
  for (const value of invalid) {
    assert.throws(() => createBrowserNetWasmBootstrap(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createBrowserNetWasmBootstrap(value), /options shape/i);
  }

  for (const [key, label] of [["preview2", "Preview 2"], ["web", "Web"]]) {
    const nested = base[key];
    const values = malformedDataObjects(nested);
    for (const value of values.invalid) {
      assert.throws(
        () => createBrowserNetWasmBootstrap({ ...base, [key]: value }),
        new RegExp(`${label} platform is invalid`, "i"));
    }
    for (const value of values.malformed) {
      assert.throws(
        () => createBrowserNetWasmBootstrap({ ...base, [key]: value }),
        new RegExp(`${label} platform shape`, "i"));
    }
    for (const name of Object.keys(nested)) {
      assert.throws(
        () => createBrowserNetWasmBootstrap({
          ...base,
          [key]: { ...nested, [name]: null },
        }),
        new RegExp(`${label} '${name}' action is required`, "i"));
    }
  }
  assert.throws(() => createBrowserBootstrapComposition(base, null), TypeError);
});

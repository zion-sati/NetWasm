import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { isAbsolute, join } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";

const executionRootPath = process.env.NETWASM_HOSTING_LOCAL_EXECUTION_ROOT;
if (executionRootPath !== undefined && !isAbsolute(executionRootPath)) {
  throw new TypeError("NETWASM_HOSTING_LOCAL_EXECUTION_ROOT must be absolute");
}
const executionRootUrl = executionRootPath === undefined
  ? new URL("./local-execution-root.mjs", import.meta.url)
  : pathToFileURL(executionRootPath);
const { createLocalNetWasmExecution } = await import(executionRootUrl);
const { createLocalExecutionComposition } = await import(
  new URL("./local-execution-composition.mjs", import.meta.url));
const { createLocalComponentNetWasmExecution } = await import(
  new URL("./local-component-execution-root.mjs", import.meta.url));
const { createLocalRawNetWasmExecution } = await import(
  new URL("./local-raw-execution-root.mjs", import.meta.url));

const digest = "1".repeat(64);
const encoder = new TextEncoder();
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
const componentAdapter = encoder.encode(`
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generated) {
  return Object.freeze({ contractKey, instantiate: generated.instantiate });
}
`);
const generatedComponent = encoder.encode(`
export function instantiate() { return { command: { run: () => 41 } }; }
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

test("executes a verified raw deployment through the local composition root", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-root-"));
  try {
    const manifestPath = join(root, "deployment.json");
    const artifacts = [
      artifact("app.wasm", "application", "application/wasm", application),
      artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
      artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
      artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
    ];
    const manifest = deployment("raw", artifacts);
    const manifestBytes = encoder.encode(JSON.stringify(manifest));
    await Promise.all([
      writeFile(manifestPath, manifestBytes),
      writeFile(join(root, "app.wasm"), application),
      writeFile(join(root, "app.raw-adapter.mjs"), rawAdapter),
      writeFile(join(root, "app.runtime-layout.json"), runtimeLayout),
      writeFile(join(root, "app.interop.json"), interopManifest),
    ]);
    for (const createExecution of [createLocalNetWasmExecution, createLocalRawNetWasmExecution]) {
      const executeNetWasm = createExecution({
        manifestPath,
        platform: {
          createFilesystem({ preopens }) {
            assert.deepEqual({ ...preopens }, {});
            return { types: { Descriptor }, preopens: { getDirectories: () => [] } };
          },
          createShim() { assert.fail("an empty WIT provider set must not create a shim"); },
        },
      });
      const outcome = await executeNetWasm({
        request: executionRequest(sha256(manifestBytes)), signal: null, stderr: sink, stdout: sink,
      });
      assert.deepEqual([outcome.completionKind, outcome.exitCode], ["normal", 37]);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("executes a verified component deployment through the local composition root", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-component-root-"));
  try {
    const manifestPath = join(root, "deployment.json");
    const artifacts = [
      artifact("app.wasm", "application", "application/wasm", application),
      artifact("app.wasm.adapter.mjs", "component-adapter", "text/javascript", componentAdapter),
      artifact("app-component.js", "component-javascript", "text/javascript", generatedComponent),
      artifact("app-component.core.wasm", "component-core-module", "application/wasm", application),
    ];
    const manifestBytes = encoder.encode(JSON.stringify(deployment("component", artifacts)));
    await Promise.all([
      writeFile(manifestPath, manifestBytes),
      writeFile(join(root, "app.wasm.adapter.mjs"), componentAdapter),
      writeFile(join(root, "app-component.js"), generatedComponent),
      writeFile(join(root, "app-component.core.wasm"), application),
    ]);
    for (const createExecution of [
      createLocalNetWasmExecution,
      createLocalComponentNetWasmExecution,
    ]) {
      const executeNetWasm = createExecution({ manifestPath, platform: platform() });
      const outcome = await executeNetWasm({
        request: executionRequest(sha256(manifestBytes)), signal: null, stderr: sink, stdout: sink,
      });
      assert.deepEqual([outcome.completionKind, outcome.exitCode], ["normal", 41]);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("rejects deployment kinds unavailable to the local host", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-kind-"));
  try {
    const manifestPath = join(root, "deployment.json");
    const bytes = encoder.encode(JSON.stringify(deployment("browser", [
      artifact("app.wasm", "application", "application/wasm", application),
    ])));
    await writeFile(manifestPath, bytes);
    for (const createExecution of [
      createLocalNetWasmExecution,
      createLocalComponentNetWasmExecution,
      createLocalRawNetWasmExecution,
    ]) {
      const executeNetWasm = createExecution({ manifestPath, platform: platform() });
      const outcome = await executeNetWasm({
        request: executionRequest(sha256(bytes)), signal: null, stderr: sink, stdout: sink,
      });
      assert.equal(outcome.primaryFailure.code, "contract.invalid");
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("maps an invalid local request to the public contract result", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-request-"));
  try {
    const manifestPath = join(root, "deployment.json");
    const bytes = encoder.encode(JSON.stringify(deployment("component", [
      artifact("app.wasm", "application", "application/wasm", application),
    ])));
    await writeFile(manifestPath, bytes);
    const executeNetWasm = createLocalNetWasmExecution({ manifestPath, platform: platform() });
    const invalid = Object.freeze({
      ...executionRequest(sha256(bytes)),
      buildFingerprint: "invalid",
    });
    const outcome = await executeNetWasm({
      request: invalid, signal: null, stderr: sink, stdout: sink,
    });
    assert.equal(outcome.primaryFailure.code, "contract.invalid");
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("validates the complete local composition before creating execution", () => {
  const valid = { manifestPath: "/tmp/deployment.json", platform: platform() };
  for (const value of [null, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createLocalNetWasmExecution(value), TypeError);
  }
  for (const value of [null, {}, { ...valid.platform, extra: () => {} }, Object.create(valid.platform)]) {
    assert.throws(() => createLocalNetWasmExecution({ ...valid, platform: value }), TypeError);
  }
  for (const name of Object.keys(valid.platform)) {
    assert.throws(() => createLocalNetWasmExecution({
      ...valid, platform: { ...valid.platform, [name]: null },
    }), /action/);
  }
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createLocalNetWasmExecution(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "manifestPath", { enumerable: true, get: () => valid.manifestPath });
  assert.throws(() => createLocalNetWasmExecution(accessor), TypeError);
  assert.throws(() => createLocalExecutionComposition(valid, null, null), TypeError);
  assert.throws(() => createLocalExecutionComposition(valid, () => () => {}, {}), TypeError);
  assert.throws(() => createLocalExecutionComposition(valid, () => null, null), TypeError);
});

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

function platform() {
  return {
    createFilesystem: () => ({ types: { Descriptor }, preopens: { getDirectories: () => [] } }),
    createShim: () => ({ getImportObject: () => ({}) }),
  };
}

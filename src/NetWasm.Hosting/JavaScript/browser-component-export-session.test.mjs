import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import {
  createBrowserComponentExportSession,
} from "./browser-component-export-session.mjs";

const encoder = new TextEncoder();
const buildFingerprint = "1".repeat(64);
const manifestUrl = "https://example.test/workers/deployment.json";
const application = Uint8Array.of(0);
const coreModule = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);
const adapterSource = encoder.encode([
  "export const contractKey = \"netwasm:worker/wit@1.0.0\";",
  "export function createAdapter(generatedModule) {",
  "  return Object.freeze({",
  "    contractKey,",
  "    async instantiate(request) {",
  "      const root = await generatedModule.instantiate(",
  "        request.loadCoreModule, request.imports, request.instantiateCore);",
  "      return Object.freeze({",
  "        exports: Object.freeze({",
  "          double: value => root.mathAlias.double(value),",
  "          rootValue: value => root.rootValue(value),",
  "        }),",
  "        guestWake: null,",
  "      });",
  "    },",
  "  });",
  "}",
  "",
].join("\n"));
const invalidBoundaryAdapterSource = encoder.encode([
  "export const contractKey = \"netwasm:worker/wit@1.0.0\";",
  "export function createAdapter() {",
  "  return Object.freeze({",
  "    contractKey,",
  "    async instantiate() {",
  "      return Object.freeze({ exports: Object.freeze({}), guestWake: null });",
  "    },",
  "  });",
  "}",
  "",
].join("\n"));
const generatedSource = encoder.encode([
  "export async function instantiate(loadCoreModule) {",
  "  await loadCoreModule(\"worker.core.wasm\");",
  "  let total = 0;",
  "  return Object.freeze({",
  "    mathAlias: Object.freeze({ double: value => value * 2 }),",
  "    rootValue(value) { total += value; return total; },",
  "  });",
  "}",
  "",
].join("\n"));

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function fixture({
  adapterBytes = adapterSource,
  configuration = {},
  executionContract = "netwasm:worker/wit@1.0.0",
  filesystemDisposeError = null,
  mutateOptions = null,
  requiredImportModules = [],
  requiredImports = [],
  timeZoneBytes = null,
} = {}) {
  const artifacts = [
    artifact("worker.wasm", "application", "application/wasm", application),
    artifact("worker.adapter.mjs", "component-adapter", "text/javascript", adapterBytes),
    artifact("worker.js", "component-javascript", "text/javascript", generatedSource),
    artifact("worker.core.wasm", "component-core-module", "application/wasm", coreModule),
  ];
  if (timeZoneBytes !== null) {
    artifacts.push(artifact(
      "worker.wasm.tz-info", "timezone-data", "application/octet-stream", timeZoneBytes, 1));
  }
  const manifest = {
    schemaVersion: 1,
    semanticBuildId: "2".repeat(64),
    deploymentKind: "browser",
    profile: "netwasm0.1",
    target: "wasm32",
    featureSet: "default",
    executionContract,
    versions: {
      compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1",
    },
    buildFingerprint,
    runtimeFeatures: timeZoneBytes === null ? [] : ["local-time"],
    artifacts,
    requiredImportModules,
    requiredImports,
    exports: [],
  };
  const manifestBytes = encoder.encode(JSON.stringify(manifest));
  const manifestSha256 = sha256(manifestBytes);
  const files = new Map([
    [manifestUrl, manifestBytes],
    ...artifacts.map(value => [
      new URL(value.relativePath, manifestUrl).href,
      value.relativePath === "worker.adapter.mjs" ? adapterBytes
        : value.relativePath === "worker.js" ? generatedSource
          : value.relativePath === "worker.core.wasm" ? coreModule
            : value.relativePath === "worker.wasm.tz-info" ? timeZoneBytes : application,
    ]),
  ]);
  const modules = new Map();
  let moduleId = 0;
  let filesystemDisposals = 0;
  class Descriptor {
    openAt() { return new Descriptor(); }
    read() { return [new Uint8Array(), true]; }
  }
  const output = Object.freeze({ write() {} });
  const options = {
    configuration: {
      applicationImports: [],
      arguments: [],
      environment: [],
      grants: {
        clocks: [], environment: [], network: "denyAll", preopens: [], randomness: false,
      },
      ...configuration,
    },
    manifestUrl,
    platform: {
      compileCoreModule: WebAssembly.compile.bind(WebAssembly),
      createFilesystem() {
        return {
          types: { Descriptor },
          preopens: { getDirectories: () => [] },
          dispose() {
            filesystemDisposals++;
            if (filesystemDisposeError !== null) throw filesystemDisposeError;
          },
        };
      },
      createModuleUrl(bytes) {
        const url = "blob:netwasm-worker/" + ++moduleId;
        modules.set(url, new Uint8Array(bytes));
        return url;
      },
      createShim() { throw new Error("shim should not be needed"); },
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
        return import("data:text/javascript;base64," + source + "#" + encodeURIComponent(url));
      },
      revokeModuleUrl(url) { modules.delete(url); },
    },
    stderr: output,
    stdout: output,
  };
  mutateOptions?.(options);
  return {
    filesystemDisposals: () => filesystemDisposals,
    manifestSha256,
    open: createBrowserComponentExportSession(options),
  };
}

test("opens an independent persistent WIT browser worker session", async () => {
  const state = fixture();
  const first = await state.open({
    startup: { buildFingerprint, manifestSha256: state.manifestSha256 },
  });
  const second = await state.open({
    startup: { buildFingerprint, manifestSha256: state.manifestSha256 },
  });

  assert.equal(first.exports.double(21), 42);
  assert.equal(first.exports.rootValue(40), 40);
  assert.equal(first.exports.rootValue(2), 42);
  assert.equal(second.exports.rootValue(1), 1);
  await first.close();
  await second.close();
  assert.equal(await first.failure, null);
  assert.equal(state.filesystemDisposals(), 2);
  assert.throws(() => first.exports.rootValue(1), /closed/);
});

test("pins the WIT worker deployment identity and releases startup scopes", async () => {
  const wrongContract = fixture({ executionContract: "wasi-command@0.2.11" });
  await assert.rejects(() => wrongContract.open({
    startup: {
      buildFingerprint,
      manifestSha256: wrongContract.manifestSha256,
    },
  }), /identity/);
  assert.equal(wrongContract.filesystemDisposals(), 0);

  const wrongBuild = fixture();
  await assert.rejects(() => wrongBuild.open({
    startup: {
      buildFingerprint: "3".repeat(64),
      manifestSha256: wrongBuild.manifestSha256,
    },
  }), /identity/);
});

test("retains timezone resources and transfers cleanup to the session", async () => {
  const state = fixture({
    configuration: {
      environment: [{ name: "TZ", value: "Australia/Melbourne" }],
      grants: {
        clocks: [], environment: ["TZ"], network: "denyAll", preopens: [], randomness: false,
      },
    },
    timeZoneBytes: Uint8Array.of(1, 2, 3),
  });
  const session = await state.open({
    startup: { buildFingerprint, manifestSha256: state.manifestSha256 },
  });
  assert.equal(state.filesystemDisposals(), 0);
  await session.close();
  assert.equal(state.filesystemDisposals(), 1);
});

test("validates browser composition and startup requests", async () => {
  for (const value of [null, 1, []]) {
    assert.throws(() => createBrowserComponentExportSession(value), /options/);
  }
  assert.throws(() => createBrowserComponentExportSession({}), /shape/);
  assert.throws(() => fixture({
    mutateOptions(options) {
      options.platform.fetch = null;
    },
  }), /fetch/);

  const state = fixture();
  for (const startup of [
    null,
    {},
    { buildFingerprint, manifestSha256: state.manifestSha256, extra: true },
    { buildFingerprint: "invalid", manifestSha256: state.manifestSha256 },
    { buildFingerprint, manifestSha256: 1 },
  ]) {
    await assert.rejects(() => state.open({ startup }), TypeError);
  }
  await assert.rejects(() => state.open(null), TypeError);
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint,
    manifestSha256: state.manifestSha256,
  }, extra: true }), TypeError);

  const hostPath = fixture({
    configuration: {
      grants: {
        clocks: [],
        environment: [],
        network: "denyAll",
        preopens: [{ access: "readOnly", guestPath: "/data", hostPath: "/tmp/data" }],
        randomness: false,
      },
    },
  });
  await assert.rejects(() => hostPath.open({ startup: {
    buildFingerprint,
    manifestSha256: hostPath.manifestSha256,
  } }), TypeError);
});

test("reports cleanup failure with a later startup failure", async () => {
  const wallClock = "wasi:clocks/wall-clock@0.2.11";
  const state = fixture({
    filesystemDisposeError: new Error("filesystem cleanup failed"),
    requiredImportModules: [wallClock],
    requiredImports: [{
      interface: wallClock,
      name: "now",
      parameters: [],
      results: [`${wallClock}#datetime`],
    }],
  });
  await assert.rejects(() => state.open({
    startup: { buildFingerprint, manifestSha256: state.manifestSha256 },
  }), error => error instanceof AggregateError && error.errors.length === 2);
  assert.equal(state.filesystemDisposals(), 1);
});

test("does not release transferred scopes twice when session opening fails", async () => {
  const state = fixture({ adapterBytes: invalidBoundaryAdapterSource });
  await assert.rejects(() => state.open({
    startup: { buildFingerprint, manifestSha256: state.manifestSha256 },
  }), /exports are required/);
  assert.equal(state.filesystemDisposals(), 1);
});

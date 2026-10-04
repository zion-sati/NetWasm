import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import { createBrowserRawExportSession } from "./browser-raw-export-session.mjs";

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const buildFingerprint = "1".repeat(64);
const manifestUrl = "https://example.test/workers/deployment.json";
const application = Buffer.from(
  "AGFzbQEAAAABFgRgBH9/f38Bf2AAAGACf38Bf2AAAX8DBQQAAQIDBQMBAAEGBgF/AUEACwdKBQ1jbTMycDJfbWVtb3J5AgAOY20zMnAyX3JlYWxsb2MAABFjbTMycDJfaW5pdGlhbGl6ZQABA2FkZAACC2luaXRpYWxpemVkAAMKGgQEAEEACwYAQQEkAAsHACAAIAFqCwQAIwALABUEbmFtZQcOAQALaW5pdGlhbGl6ZWQ=",
  "base64");
const reportingApplication = Buffer.from(
  "AGFzbQEAAAABEgNgA39/fwBgBH9/f38Bf2AAAAIwAQ9uZXR3YXNtLmhvc3QudjEccmVwb3J0X3Rlcm1pbmFsX2V4Y2VwdGlvbl92MQAAAwQDAQICBQMBAAEGBgF/AUEACwc/BA1jbTMycDJfbWVtb3J5AgAOY20zMnAyX3JlYWxsb2MAARFjbTMycDJfaW5pdGlhbGl6ZQACBnJlcG9ydAADChgDBABBAAsGAEEBJAALCgBBB0EAQQAQAAs=",
  "base64");
const rawAdapter = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({
  abiVersion: 1,
  bindingIdentities: Object.freeze([]),
  requiredCapabilities: Object.freeze([]),
  target: "wasm32",
  witSourceFingerprint: "sha256:${"2".repeat(64)}",
});
export function createAdapter(request) {
  return Object.freeze({
    imports: Object.freeze(Object.create(null)),
    metadata: request.metadata,
  });
}
`);
const runtimeLayout = encoder.encode(JSON.stringify({
  schemaVersion: 2,
  target: "wasm32",
  applicationStaticDataEnd: 0,
  managedExecutableEntryPoint: null,
}));

function interop(imports = [], exports = [
  { name: "add", parameters: ["i32", "i32"], result: "i32" },
  { name: "initialized", parameters: [], result: "i32" },
]) {
  return encoder.encode(JSON.stringify({
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
    imports,
    exports,
    callbacks: [],
    witImports: [],
  }));
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function fixture({
  applicationBytes = application,
  applicationImports = [],
  configuration = {},
  consumerModules = Object.freeze(Object.create(null)),
  executionContract = "netwasm:worker/jsexport@1.0.0",
  filesystemDisposeError = null,
  interopImports = [],
  interopExports = undefined,
  manifestTarget = "wasm32",
  mutateOptions = null,
  requiredImportModules = [],
  requiredImports = [],
  timeZoneBytes = null,
} = {}) {
  const interopManifest = interop(interopImports, interopExports);
  const artifacts = [
    artifact("worker.wasm", "application", "application/wasm", applicationBytes),
    artifact("worker.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
    artifact("runtime-layout.json", "runtime-layout", "application/json", runtimeLayout, 1),
    artifact("interop.json", "interop-manifest", "application/json", interopManifest, 1),
  ];
  if (timeZoneBytes !== null) {
    artifacts.push(artifact(
      "worker.wasm.tz-info", "timezone-data", "application/octet-stream", timeZoneBytes, 1));
  }
  const manifest = {
    schemaVersion: 1,
    semanticBuildId: "3".repeat(64),
    deploymentKind: "raw",
    profile: "netwasm0.1",
    target: manifestTarget,
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
    ...artifacts.map(value => [new URL(value.relativePath, manifestUrl).href,
      value.relativePath === "worker.wasm" ? applicationBytes
        : value.relativePath === "worker.raw-adapter.mjs" ? rawAdapter
          : value.relativePath === "runtime-layout.json" ? runtimeLayout
            : value.relativePath === "interop.json" ? interopManifest : timeZoneBytes]),
  ]);
  const modules = new Map();
  let moduleId = 0;
  let filesystemDisposals = 0;
  let shimCalls = 0;
  const outputMessages = [];
  class Descriptor {
    openAt() { return new Descriptor(); }
    read() { return [new Uint8Array(), true]; }
  }
  const output = Object.freeze({ write(bytes) { outputMessages.push(decoder.decode(bytes)); } });
  const options = {
    configuration: {
      applicationImports,
      arguments: [],
      environment: [],
      grants: {
        clocks: [], environment: [], network: "denyAll", preopens: [], randomness: false,
      },
      ...configuration,
    },
    consumerModules,
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
        const url = `blob:netwasm-worker/${++moduleId}`;
        modules.set(url, new Uint8Array(bytes));
        return url;
      },
      createShim() { shimCalls++; throw new Error("shim should not be needed"); },
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
    stderr: output,
    stdout: output,
  };
  mutateOptions?.(options);
  const open = createBrowserRawExportSession(options);
  return {
    filesystemDisposals: () => filesystemDisposals,
    manifestSha256,
    open,
    outputMessages,
    shimCalls: () => shimCalls,
  };
}

test("opens a verified persistent browser library session", async () => {
  const state = fixture();
  const session = await state.open({ startup: { buildFingerprint, manifestSha256: state.manifestSha256 } });
  assert.equal(session.exports.initialized(), 1);
  assert.equal(session.exports.add(20, 22), 42);
  assert.equal(session.exports.add(1, 2), 3);
  assert.equal(state.shimCalls(), 0);
  await session.close();
  assert.equal(await session.failure, null);
  assert.equal(state.filesystemDisposals(), 1);
  assert.throws(() => session.exports.add(1, 1), /closed/);
});

test("pins deployment identity and the library hosting contract", async () => {
  const state = fixture();
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint: "4".repeat(64), manifestSha256: state.manifestSha256,
  } }), /identity/);
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint, manifestSha256: "5".repeat(64),
  } }), /integrity/);

  const command = fixture({ executionContract: "wasi-command@0.2.11" });
  await assert.rejects(() => command.open({ startup: {
    buildFingerprint, manifestSha256: command.manifestSha256,
  } }), /identity/);

  const wrongTarget = fixture({ manifestTarget: "wasm64" });
  await assert.rejects(() => wrongTarget.open({ startup: {
    buildFingerprint, manifestSha256: wrongTarget.manifestSha256,
  } }), /target/);
});

test("routes managed exception diagnostics to the worker stderr sink", async () => {
  const state = fixture({
    applicationBytes: reportingApplication,
    interopExports: [{ name: "report", parameters: [], result: "void" }],
  });
  const session = await state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } });
  session.exports.report();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(state.outputMessages[0], "Managed exception #7: <no stored message>\n");
  assert.match(state.outputMessages[1], /Managed exception #7 type: <exception type #7 unavailable>/);
  await session.close();
});

test("retains timezone resources until the persistent session closes", async () => {
  const state = fixture({
    configuration: {
      environment: [{ name: "TZ", value: "Australia/Melbourne" }],
      grants: {
        clocks: [], environment: ["TZ"], network: "denyAll", preopens: [], randomness: false,
      },
    },
    timeZoneBytes: Uint8Array.of(1, 2, 3),
  });
  const session = await state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } });
  assert.equal(state.filesystemDisposals(), 0);
  await session.close();
  assert.equal(state.filesystemDisposals(), 1);
});

test("keeps ordinary JS imports separate from WIT providers", async () => {
  const imported = {
    module: "consumer.worker",
    name: "increment",
    parameters: ["i32"],
    result: "i32",
    asyncReturn: null,
    resolveExport: null,
    rejectExport: null,
    cancelExport: null,
  };
  const missing = fixture({ interopImports: [imported] });
  await assert.rejects(() => missing.open({ startup: {
    buildFingerprint, manifestSha256: missing.manifestSha256,
  } }), /JS import modules/);

  const supplied = fixture({
    consumerModules: { "consumer.worker": { increment: value => value + 1 } },
    interopImports: [imported],
  });
  const session = await supplied.open({ startup: {
    buildFingerprint, manifestSha256: supplied.manifestSha256,
  } });
  assert.equal(session.exports.add(20, 22), 42);
  await session.close();

  assert.throws(() => fixture({ consumerModules: { "wasi:cli/run@0.2.11": {} } }), /reserved/);
  assert.throws(() => fixture({ consumerModules: { "netwasm.host.v1": {} } }), /reserved/);
  assert.throws(() => fixture({ consumerModules: { "consumer.worker": { "": () => {} } } }),
    /invalid/);
  assert.throws(() => fixture({ consumerModules: { "consumer.worker": { increment: 1 } } }),
    /invalid/);
});

test("releases acquired scopes when provider preparation fails", async () => {
  const wallClock = "wasi:clocks/wall-clock@0.2.11";
  const state = fixture({
    requiredImportModules: [wallClock],
    requiredImports: [{
      interface: wallClock,
      name: "now",
      parameters: [],
      results: [`${wallClock}#datetime`],
    }],
  });
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } }));
  assert.equal(state.filesystemDisposals(), 1);
});

test("reports startup and cleanup failures together", async () => {
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
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } }), error => {
    assert.equal(error instanceof AggregateError, true);
    assert.match(error.message, /startup failed/);
    assert.equal(error.errors.length, 2);
    return true;
  });
  assert.equal(state.filesystemDisposals(), 1);
});

test("does not release transferred scopes twice when session opening fails", async () => {
  const state = fixture({
    interopExports: [{ name: "missing", parameters: [], result: "void" }],
  });
  await assert.rejects(() => state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } }), /required/);
  assert.equal(state.filesystemDisposals(), 1);
});

test("rejects browser host filesystem paths", () => {
  const state = fixture({ configuration: {
    grants: {
      clocks: [], environment: [], network: "denyAll", randomness: false,
      preopens: [{ access: "readOnly", guestPath: "/data", hostPath: "/host/data" }],
    },
  } });
  return assert.rejects(() => state.open({ startup: {
    buildFingerprint, manifestSha256: state.manifestSha256,
  } }), /absolute local path/);
});

test("validates composition and startup shapes", async () => {
  assert.throws(() => fixture({ mutateOptions(options) { options.platform.fetch = null; } }),
    /fetch/);
  assert.throws(() => fixture({ mutateOptions(options) {
    Object.defineProperty(options, "stdout", { enumerable: true, get: () => ({ write() {} }) });
  } }), /plain data object/);
  assert.throws(() => fixture({ consumerModules: Object.create({ inherited: true }) }),
    /plain data object/);
  assert.throws(() => fixture({ consumerModules: { [Symbol("invalid")]: true } }), /invalid/);
  const valid = fixture();
  for (const request of [null, {}, { startup: null }, { startup: {} }, {
    startup: { buildFingerprint: "invalid", manifestSha256: valid.manifestSha256 },
  }, {
    startup: { buildFingerprint, manifestSha256: "invalid" },
  }]) {
    await assert.rejects(() => valid.open(request), TypeError);
  }
});

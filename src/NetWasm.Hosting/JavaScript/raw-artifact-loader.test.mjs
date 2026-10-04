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
const exceptionTypeMapBytes = encoder.encode(JSON.stringify({
  schemaVersion: 2,
  buildId: "build",
  entries: [],
}));
const stackTraceSymbolsBytes = encoder.encode(JSON.stringify({
  schemaVersion: 1,
  methods: [{ id: 7, name: "EntryPoint.Run in Program.cs:line 12" }],
}));
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
  assert.equal(loaded.diagnosticArtifacts.manifest.buildId, "build");
  assert.equal(loaded.diagnosticArtifacts.manifest.wasmSha256, sha256(application));
  assert.equal(loaded.diagnosticArtifacts.manifest.exceptionTypeMapSha256,
    sha256(exceptionTypeMapBytes));
  assert.deepEqual(loaded.diagnosticArtifacts.wasmBytes, new Uint8Array(application));
  assert.deepEqual(loaded.diagnosticArtifacts.mapBytes, exceptionTypeMapBytes);
  assert.equal(loaded.diagnosticArtifacts.mapMediaType,
    "application/vnd.netwasm.exception-types+json;version=2");
  assert.deepEqual(loaded.stackTraceSymbols, [
    { id: 7, name: "EntryPoint.Run in Program.cs:line 12" },
  ]);
  const firstUse = calls.findIndex(value => value.startsWith("compile:") || value.startsWith("import:"));
  assert.equal(calls.slice(0, firstUse).filter(value => value === "hash").length, 6);
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

test("loads an older raw closure without diagnostic artifacts", async () => {
  const request = createRequest();
  request.artifacts = request.artifacts.filter(
    value => value.role !== "exception-type-map" && value.role !== "stack-trace-symbols");
  const loaded = await loadRawArtifacts(request);
  assert.equal(loaded.diagnosticArtifacts, undefined);
  assert.deepEqual(loaded.stackTraceSymbols, []);
});

test("rejects a malformed stack-trace sidecar", async () => {
  const values = content();
  values.set("stack-trace-symbols", encoder.encode(JSON.stringify({
    schemaVersion: 2,
    methods: [],
  })));
  await assert.rejects(() => loadRawArtifacts(createRequest({
    artifacts: artifacts(values),
    readArtifact: async value => values.get(value.role),
  })), /stack-trace symbol sidecar schema/u);
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

test("accepts schema-three reached native signatures and explicit empty imports", async () => {
  for (const nativeImports of [[], [
    { libraryName: "mule", entryPoint: "compute", parameters: ["i32", "i64", "f32", "f64"], returnType: "f64" },
    { libraryName: "__Internal", entryPoint: "reset", parameters: [], returnType: null },
  ]]) {
    const runtimeFeatures = [
      "ephemeron-handles",
      "local-time",
      "structured-command-diagnostics",
    ];
    const bytes = encoder.encode(JSON.stringify({
      ...layout,
      schemaVersion: 3,
      runtimeFeatures,
      nativeImports,
    }));
    const loaded = await loadRawArtifacts(requestWithContent("runtime-layout", bytes));
    assert.deepEqual(loaded.runtimeLayout.runtimeFeatures, runtimeFeatures);
    assert.equal(Object.isFrozen(loaded.runtimeLayout.runtimeFeatures), true);
    assert.deepEqual(loaded.runtimeLayout.nativeImports, nativeImports);
    assert.equal(Object.isFrozen(loaded.runtimeLayout.nativeImports), true);
  }
});

test("rejects noncanonical runtime feature evidence before loading", async () => {
  for (const runtimeFeatures of [
    null,
    ["future"],
    ["local-time", "local-time"],
    ["structured-command-diagnostics", "local-time"],
  ]) {
    const request = requestWithContent("runtime-layout", encoder.encode(JSON.stringify({
      ...layout,
      schemaVersion: 3,
      runtimeFeatures,
      nativeImports: [],
    })));
    const calls = [];
    request.compileModule = async () => { calls.push("compile"); return {}; };
    request.importModule = async () => { calls.push("import"); return {}; };
    await assert.rejects(() => loadRawArtifacts(request), /runtime features/);
    assert.deepEqual(calls, []);
  }
});

test("accepts and freezes schema-four compiler callback evidence", async () => {
  const callbacks = [{
    nativeSymbol: "__netwasm_native_callback_0",
    runtimeImportSymbol: "__netwasm_native_callback_0",
    applicationExportName: "__netwasm_application_callback_0",
    runtimeGetterExportName: "__netwasm_callback_address_0",
    parameters: ["i32", "i64"],
    returnType: "i32",
  }, {
    nativeSymbol: "named_callback",
    runtimeImportSymbol: "__netwasm_named_callback_import_1",
    applicationExportName: "named_callback",
    runtimeGetterExportName: null,
    parameters: ["i32"],
    returnType: "i32",
  }];
  const nativeCallbackSupport = {
    fileName: "application.callbacks.o",
    sha256: "a".repeat(64),
    callbacks,
    temporaryApplicationExports: [callbacks[0].applicationExportName],
    temporaryRuntimeExports: [callbacks[0].runtimeGetterExportName],
  };
  const bytes = encoder.encode(JSON.stringify({
    ...layout,
    schemaVersion: 4,
    nativeImports: [],
    nativeCallbackSupport,
  }));

  const loaded = await loadRawArtifacts(requestWithContent("runtime-layout", bytes));

  assert.deepEqual(loaded.runtimeLayout.nativeCallbackSupport, nativeCallbackSupport);
  assert.equal(Object.isFrozen(loaded.runtimeLayout.nativeCallbackSupport), true);
  assert.equal(Object.isFrozen(loaded.runtimeLayout.nativeCallbackSupport.callbacks), true);
  assert.equal(Object.isFrozen(loaded.runtimeLayout.nativeCallbackSupport.callbacks[0]), true);
});

test("rejects incomplete or contradictory schema-four callback evidence before loading", async () => {
  const callback = {
    nativeSymbol: "native_callback",
    runtimeImportSymbol: "runtime_callback",
    applicationExportName: "application_callback",
    runtimeGetterExportName: "runtime_getter",
    parameters: ["i32"],
    returnType: null,
  };
  const support = {
    fileName: "application.callbacks.o",
    sha256: "a".repeat(64),
    callbacks: [callback],
    temporaryApplicationExports: [callback.applicationExportName],
    temporaryRuntimeExports: [callback.runtimeGetterExportName],
  };
  const invalidSupport = [
    null,
    { ...support, fileName: "../application.callbacks.o" },
    { ...support, sha256: "A".repeat(64) },
    { ...support, callbacks: [] },
    { ...support, callbacks: [{ ...callback, parameters: ["v128"] }] },
    { ...support, callbacks: [{ ...callback, runtimeImportSymbol: "" }] },
    { ...support, callbacks: [{ ...callback, runtimeGetterExportName: "" }] },
    {
      ...support,
      callbacks: [callback, {
        ...callback,
        nativeSymbol: "native_callback_2",
        applicationExportName: "application_callback_2",
        runtimeGetterExportName: "runtime_getter_2",
      }],
      temporaryApplicationExports: [callback.applicationExportName, "application_callback_2"],
      temporaryRuntimeExports: [callback.runtimeGetterExportName, "runtime_getter_2"],
    },
    { ...support, temporaryApplicationExports: ["other"] },
    { ...support, temporaryRuntimeExports: [] },
    { ...support, extra: true },
  ];
  for (const nativeCallbackSupport of invalidSupport) {
    const request = requestWithContent("runtime-layout", encoder.encode(JSON.stringify({
      ...layout,
      schemaVersion: 4,
      nativeImports: [],
      nativeCallbackSupport,
    })));
    const calls = [];
    request.compileModule = async () => { calls.push("compile"); return {}; };
    request.importModule = async () => { calls.push("import"); return {}; };
    await assert.rejects(() => loadRawArtifacts(request), /callback|layout/);
    assert.deepEqual(calls, []);
  }
});

test("rejects incomplete and unqualified schema-three native contracts before loading", async () => {
  const validImport = { libraryName: "mule", entryPoint: "compute", parameters: [], returnType: null };
  const invalidImports = [null, {}, { ...validImport, libraryName: "" }, { ...validImport, entryPoint: null },
    { ...validImport, parameters: null }, { ...validImport, parameters: ["v128"] },
    { ...validImport, returnType: "nativeInt" }, { ...validImport, returnType: undefined }];
  const invalidLayouts = [
    { ...layout, schemaVersion: 3 },
    { ...layout, schemaVersion: 3, nativeImports: null },
    ...invalidImports.map(value => ({ ...layout, schemaVersion: 3, nativeImports: [value] })),
  ];
  for (const value of invalidLayouts) {
    const request = requestWithContent("runtime-layout", encoder.encode(JSON.stringify(value)));
    const calls = [];
    request.compileModule = async () => { calls.push("compile"); return {}; };
    request.importModule = async () => { calls.push("import"); return {}; };
    await assert.rejects(() => loadRawArtifacts(request), /layout|native import/);
    assert.deepEqual(calls, []);
  }
});

test("loads entryless runtime metadata without inventing a process entry", async () => {
  const bytes = encoder.encode(JSON.stringify({ ...layout, managedExecutableEntryPoint: null }));
  const loaded = await loadRawArtifacts(requestWithContent("runtime-layout", bytes));
  assert.equal(loaded.runtimeLayout.managedExecutableEntryPoint, null);
  assert.equal(loaded.abi.entryPoint, null);
  assert.equal(Object.isFrozen(loaded.abi), true);
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
    encoder.encode(JSON.stringify({ ...layout, managedExecutableEntryPoint: [] })),
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

test("rejects malformed raw exception type maps before loading modules", async () => {
  for (const bytes of [
    encoder.encode("{"),
    encoder.encode("null"),
    encoder.encode("[]"),
    encoder.encode(JSON.stringify({ schemaVersion: 1, buildId: "build", entries: [] })),
    encoder.encode(JSON.stringify({ schemaVersion: 2, buildId: 7, entries: [] })),
    encoder.encode(JSON.stringify({ schemaVersion: 2, buildId: "", entries: [] })),
    encoder.encode(JSON.stringify({ schemaVersion: 2, buildId: "build", entries: null })),
  ]) {
    const request = requestWithContent("exception-type-map", bytes);
    const calls = [];
    request.compileModule = async () => { calls.push("compile"); return {}; };
    request.importModule = async () => { calls.push("import"); return {}; };

    await assert.rejects(() => loadRawArtifacts(request), /exception type map/);
    assert.deepEqual(calls, []);
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
    ["exception-type-map", exceptionTypeMapBytes],
    ["runtime-layout", layoutBytes],
    ["interop-manifest", interopBytes],
    ["stack-trace-symbols", stackTraceSymbolsBytes],
  ]);
}

function artifacts(values) {
  return [
    artifact("publish/app.wasm", "application", "application/wasm", values.get("application")),
    artifact("publish/app.raw-adapter.mjs", "raw-adapter", "text/javascript", values.get("raw-adapter")),
    artifact("publish/app.exceptions.json", "exception-type-map",
      "application/vnd.netwasm.exception-types+json;version=2",
      values.get("exception-type-map"), 2),
    artifact("publish/runtime-layout.json", "runtime-layout", "application/json", values.get("runtime-layout"), 2),
    artifact("publish/interop.json", "interop-manifest", "application/json", values.get("interop-manifest"), 1),
    artifact("publish/app.netwasm.stacktrace.json", "stack-trace-symbols",
      "application/vnd.netwasm.stack-trace-symbols+json;version=1",
      values.get("stack-trace-symbols"), 1),
  ];
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

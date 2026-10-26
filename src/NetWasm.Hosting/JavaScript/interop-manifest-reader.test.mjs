import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { parseInteropManifest, requiredImportModules } from "./interop-manifest-reader.mjs";

const layouts = {
  wasm32: {
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
    spanReferenceOffset: 0,
    spanLengthOffset: 4,
  },
  wasm64: {
    managedReferenceSize: 8,
    stringLengthOffset: 8,
    stringDataOffset: 12,
    arrayLengthOffset: 8,
    arrayDataPointerOffset: 16,
    spanReferenceOffset: 0,
    spanLengthOffset: 8,
  },
};

function createManifest(target = "wasm32") {
  return {
    version: 2,
    target,
    statusAbi: { successStatus: 0, hostFailureStatus: 1, scalarResultOffset: 0 },
    targetLayout: { ...layouts[target] },
    imports: [],
    exports: [],
  };
}

function expectInvalid(manifest, pattern) {
  assert.throws(
    () => parseInteropManifest(manifest),
    error => error instanceof NetWasmHostError && pattern.test(error.message));
}

test("parseInteropManifest accepts both target layouts and serialized null optionals", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const manifest = createManifest(target);
    manifest.imports.push({ call: "status",
      module: "consumer",
      name: "sync",
      parameters: ["i32"],
      result: "i32",
      asyncReturn: null,
    });
    manifest.exports.push({
      name: "sync",
      parameters: ["i32"],
      result: "i32",
      asyncReturn: null,
    });
    assert.strictEqual(parseInteropManifest(manifest), manifest);
  }
});

test("parseInteropManifest rejects invalid versions, targets, envelopes and layouts", () => {
  for (const manifest of [null, {}, { version: 1 }, { version: 3 }]) {
    expectInvalid(manifest, /version/i);
  }
  expectInvalid({ ...createManifest(), target: "wasm16" }, /wasm32 or wasm64/i);

  const envelopeMutations = [
    manifest => { manifest.statusAbi = null; },
    manifest => { manifest.statusAbi.successStatus = 1; },
    manifest => { manifest.statusAbi.hostFailureStatus = 2; },
    manifest => { manifest.statusAbi.scalarResultOffset = 1; },
    manifest => { manifest.targetLayout = null; },
    manifest => { manifest.imports = null; },
    manifest => { manifest.exports = null; },
    manifest => { manifest.callbacks = {}; },
  ];
  for (const mutate of envelopeMutations) {
    const manifest = createManifest();
    mutate(manifest);
    expectInvalid(manifest, /imports and exports arrays/i);
  }

  for (const name of Object.keys(layouts.wasm32)) {
    for (const value of [1.5, layouts.wasm32[name] + 1]) {
      const manifest = createManifest();
      manifest.targetLayout[name] = value;
      expectInvalid(manifest, new RegExp(name));
    }
  }
});

test("parseInteropManifest validates import descriptors and uniqueness", () => {
  const base = { call: "status", module: "consumer", name: "call", parameters: [], result: "void" };
  const invalid = [
    null,
    { ...base, module: 1 },
    { ...base, module: "" },
    { ...base, name: 1 },
    { ...base, name: "" },
    { ...base, parameters: null },
    { ...base, result: "bogus" },
    { ...base, parameters: ["bogus"] },
  ];
  for (const descriptor of invalid) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    expectInvalid(manifest, /invalid import descriptor/i);
  }

  const manifest = createManifest();
  manifest.imports = [base, { ...base }];
  expectInvalid(manifest, /duplicate host import consumer.call/i);
});

// A span is a view valid only during a synchronous call, so it is only ever
// a status import's parameter.
test("parseInteropManifest accepts span views only as synchronous import parameters", () => {
  const base = { call: "status", module: "consumer", name: "upload", parameters: ["u8-view", "f64-view"], result: "i32" };
  const accepted = createManifest();
  accepted.imports = [base];
  assert.doesNotThrow(() => parseInteropManifest(accepted));
  for (const [descriptor, message] of [
    [{ ...base, result: "u8-view" }, /invalid span import descriptor/i],
    [{ ...base, asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject", cancelExport: "cancel" },
      /invalid span import descriptor/i],
    [{ ...base, call: "direct" }, /invalid direct import descriptor/i],
  ]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    expectInvalid(manifest, message);
  }
  const exported = createManifest();
  exported.exports = [{ name: "run", parameters: ["u8-view"], result: "void" }];
  expectInvalid(exported, /invalid export descriptor/i);
});

// A direct import passes primitive scalars as WebAssembly values: anything
// else, or an asynchronous direct import, is not a direct import.
test("parseInteropManifest validates direct import descriptors", () => {
  const base = { call: "direct", module: "consumer", name: "add", parameters: ["i32", "f64"], result: "i32" };
  for (const descriptor of [
    { ...base, call: "bogus" },
    { ...base, parameters: ["string"] },
    { ...base, result: "string" },
    { ...base, asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject", cancelExport: "cancel" },
  ]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    expectInvalid(manifest, /invalid (direct )?import descriptor/i);
  }
  for (const descriptor of [base, { ...base, result: "void" }]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    assert.doesNotThrow(() => parseInteropManifest(manifest));
  }
});

test("parseInteropManifest validates asynchronous import descriptors", () => {
  const base = { call: "status",
    module: "consumer",
    name: "call",
    parameters: [],
    result: "i32",
    asyncReturn: "task",
    resolveExport: "resolve",
    rejectExport: "reject",
    cancelExport: "cancel",
  };
  for (const descriptor of [
    { ...base, asyncReturn: "other" },
    { ...base, resolveExport: 1 },
    { ...base, resolveExport: "" },
    { ...base, rejectExport: 1 },
    { ...base, rejectExport: "" },
    { ...base, cancelExport: 1 },
    { ...base, cancelExport: "" },
    { ...base, result: "subscription" },
  ]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    expectInvalid(manifest, /invalid async import descriptor/i);
  }

  for (const descriptor of [
    base,
    { ...base, asyncReturn: "value-task" },
    { ...base, result: "void" },
    { ...base, result: "string" },
    { ...base, result: "bytes" },
    { ...base, result: "f64[]" },
    { ...base, result: "object" },
  ]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    assert.strictEqual(parseInteropManifest(manifest), manifest);
  }
});

test("parseInteropManifest validates export descriptors and uniqueness", () => {
  const base = { name: "call", parameters: [], result: "void" };
  for (const descriptor of [
    null,
    { ...base, name: 1 },
    { ...base, name: "" },
    { ...base, parameters: null },
    { ...base, result: "bogus" },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    expectInvalid(manifest, /invalid export descriptor/i);
  }

  const manifest = createManifest();
  manifest.exports = [base, { ...base }];
  expectInvalid(manifest, /duplicate managed export call/i);

  for (const descriptor of [
    { ...base, parameters: ["string"] },
    { ...base, result: "string" },
    { ...base, parameters: ["bytes"] },
    { ...base, result: "bytes" },
    { ...base, parameters: ["i32[]", "u64[]"], result: "f64[]" },
    { ...base, parameters: ["object"], result: "object" },
    { ...base, parameters: ["i32", "string"], parameterNames: ["count", "label"],
      nonNullableParameters: [true, false], nonNullableResult: true },
    { ...base, parameters: ["i32"], parameterNames: [] },
  ]) {
    const stringManifest = createManifest();
    stringManifest.exports = [descriptor];
    assert.strictEqual(parseInteropManifest(stringManifest), stringManifest);
  }
});

test("parseInteropManifest validates export parameter names", () => {
  const base = { name: "call", parameters: ["i32", "i32"], result: "i32" };
  for (const parameterNames of [null, "a", ["a"], ["a", 1]]) {
    const manifest = createManifest();
    manifest.exports = [{ ...base, parameterNames }];
    expectInvalid(manifest, /invalid export descriptor/i);
  }
  for (const metadata of [{ nonNullableParameters: [true] }, { nonNullableParameters: [true, "no"] },
    { nonNullableResult: "true" }]) {
    const manifest = createManifest();
    manifest.exports = [{ ...base, ...metadata }];
    expectInvalid(manifest, /invalid export descriptor/i);
  }
});

test("parseInteropManifest validates asynchronous export descriptors", () => {
  const base = {
    name: "call",
    parameters: ["i32"],
    result: "i32",
    asyncReturn: "task",
    statusExport: "status",
    completeExport: "complete", completionResult: "exception-handle-v1",
    resultExport: "result",
  };
  for (const descriptor of [
    { ...base, asyncReturn: "other" },
    { ...base, statusExport: 1 },
    { ...base, statusExport: "" },
    { ...base, completeExport: 1 },
    { ...base, completeExport: "" },
    { ...base, resultExport: 1 },
    { ...base, completionResult: undefined },
    { ...base, completionResult: "void" },
    { ...base, resultExport: "" },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    expectInvalid(manifest, /invalid async export descriptor/i);
  }

  for (const descriptor of [
    base,
    { ...base, parameters: ["string", "bytes", "f32[]", "i32", "object"] },
    { ...base, asyncReturn: "value-task" },
    { ...base, result: "void", resultExport: undefined },
    // Issue #127: an async export settles with what a synchronous one returns.
    { ...base, result: "string" },
    { ...base, result: "bytes" },
    { ...base, result: "f32[]" },
    { ...base, result: "object" },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    assert.strictEqual(parseInteropManifest(manifest), manifest);
  }
});

test("parseInteropManifest validates callback descriptors", () => {
  const callbackImport = { call: "status",
    module: "consumer",
    name: "listen",
    parameters: ["callback"],
    result: "subscription",
  };
  const base = {
    module: "consumer",
    importName: "listen",
    parameterIndex: 0,
    exportName: "invoke",
    parameters: ["i32"],
    result: "void",
  };
  const invalid = [
    null,
    { ...base, module: 1 },
    { ...base, module: "" },
    { ...base, importName: 1 },
    { ...base, importName: "" },
    { ...base, parameterIndex: 0.5 },
    { ...base, parameterIndex: -1 },
    { ...base, exportName: 1 },
    { ...base, exportName: "" },
    { ...base, parameters: null },
    { ...base, result: "subscription" },
    { ...base, parameters: ["u8-view"] },
    { ...base, parameters: ["callback"] },
    { ...base, parameters: ["bool[]"] },
  ];
  for (const callback of invalid) {
    const manifest = createManifest();
    manifest.imports = [callbackImport];
    manifest.callbacks = [callback];
    expectInvalid(manifest, /invalid callback descriptor/i);
  }
  // A callback takes and returns what a synchronous export does.
  for (const callback of [
    { ...base, result: "string" },
    { ...base, result: "object" },
    { ...base, result: "f64[]" },
    { ...base, parameters: ["object", "string", "bytes", "i32[]", "u16[]", "f64"] },
  ]) {
    const manifest = createManifest();
    manifest.imports = [callbackImport];
    manifest.callbacks = [callback];
    assert.strictEqual(parseInteropManifest(manifest), manifest);
  }
});

test("parseInteropManifest binds each callback to one import parameter and export", () => {
  const callbackImport = { call: "status",
    module: "consumer",
    name: "listen",
    parameters: ["callback", "i32", "callback"],
    result: "subscription",
  };
  const first = {
    module: "consumer",
    importName: "listen",
    parameterIndex: 0,
    exportName: "invokeFirst",
    parameters: ["string", "i32"],
    result: "i32",
  };
  const second = {
    ...first,
    parameterIndex: 2,
    exportName: "invokeSecond",
    parameters: ["bytes"],
    result: "void",
  };
  const valid = createManifest();
  valid.imports = [callbackImport];
  valid.callbacks = [first, second];
  assert.strictEqual(parseInteropManifest(valid), valid);

  for (const callbacks of [
    [{ ...first, module: "missing" }],
    [{ ...first, parameterIndex: 1 }],
    [first, { ...second, exportName: first.exportName }],
    [first],
  ]) {
    const manifest = createManifest();
    manifest.imports = [callbackImport];
    manifest.callbacks = callbacks;
    expectInvalid(manifest, /does not match|has no callback descriptor/i);
  }
});

test("parseInteropManifest accepts the modules a host supplies to the final module", () => {
  const manifest = { ...createManifest(), importModules: ["host_gl", "host_ui"] };
  assert.strictEqual(parseInteropManifest(manifest), manifest);
  assert.strictEqual(parseInteropManifest(createManifest()).importModules, undefined);
  for (const importModules of ["host_ui", [""], [1], ["host_ui", "host_ui"],
    ["netwasm.host.v1"], ["netwasm:runtime/reactor-host"], ["wasi:io/streams"], ["wasi_snapshot_preview1"]]) {
    expectInvalid({ ...createManifest(), importModules }, /import module/i);
  }
});

test("requiredImportModules names the JSImport modules and the import modules", () => {
  const manifest = {
    ...createManifest(),
    imports: [
      { call: "direct", module: "consumer", name: "add", parameters: [], result: "i32" },
      { call: "direct", module: "netwasm.host.v1", name: "queue_microtask", parameters: [], result: "void" },
    ],
    importModules: ["host_ui"],
  };
  assert.deepEqual([...requiredImportModules(manifest)].sort(), ["consumer", "host_ui"]);
  // A manifest from a build that declares no import modules omits the key.
  const { importModules, ...withoutImportModules } = manifest;
  assert.deepEqual([...requiredImportModules(withoutImportModules)], ["consumer"]);
});

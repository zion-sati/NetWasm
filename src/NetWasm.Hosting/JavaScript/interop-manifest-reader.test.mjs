import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { parseInteropManifest } from "./interop-manifest-reader.mjs";

const layouts = {
  wasm32: {
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
  },
  wasm64: {
    managedReferenceSize: 8,
    stringLengthOffset: 8,
    stringDataOffset: 12,
    arrayLengthOffset: 8,
    arrayDataPointerOffset: 16,
  },
};

function createManifest(target = "wasm32") {
  return {
    version: 1,
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
    manifest.imports.push({
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
  for (const manifest of [null, {}, { version: 2 }]) {
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
  const base = { module: "consumer", name: "call", parameters: [], result: "void" };
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

test("parseInteropManifest validates asynchronous import descriptors", () => {
  const base = {
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
    { ...base, result: "object" },
  ]) {
    const manifest = createManifest();
    manifest.imports = [descriptor];
    expectInvalid(manifest, /invalid async import descriptor/i);
  }

  for (const descriptor of [
    base,
    { ...base, asyncReturn: "value-task" },
    { ...base, result: "void" },
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
    { ...base, parameters: ["string"] },
    { ...base, result: "string" },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    expectInvalid(manifest, /invalid export descriptor/i);
  }

  const manifest = createManifest();
  manifest.exports = [base, { ...base }];
  expectInvalid(manifest, /duplicate managed export call/i);
});

test("parseInteropManifest validates asynchronous export descriptors", () => {
  const base = {
    name: "call",
    parameters: ["i32"],
    result: "i32",
    asyncReturn: "task",
    statusExport: "status",
    completeExport: "complete",
    resultExport: "result",
  };
  for (const descriptor of [
    { ...base, asyncReturn: "other" },
    { ...base, statusExport: 1 },
    { ...base, statusExport: "" },
    { ...base, completeExport: 1 },
    { ...base, completeExport: "" },
    { ...base, resultExport: 1 },
    { ...base, resultExport: "" },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    expectInvalid(manifest, /invalid async export descriptor/i);
  }

  for (const descriptor of [
    base,
    { ...base, asyncReturn: "value-task" },
    { ...base, result: "void", resultExport: undefined },
  ]) {
    const manifest = createManifest();
    manifest.exports = [descriptor];
    assert.strictEqual(parseInteropManifest(manifest), manifest);
  }
});

test("parseInteropManifest validates callback descriptors", () => {
  const callbackImport = {
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
    { ...base, result: "string" },
    { ...base, parameters: ["object"] },
    { ...base, parameters: ["string", "bytes"] },
  ];
  for (const callback of invalid) {
    const manifest = createManifest();
    manifest.imports = [callbackImport];
    manifest.callbacks = [callback];
    expectInvalid(manifest, /invalid callback descriptor/i);
  }
});

test("parseInteropManifest binds each callback to one import parameter and export", () => {
  const callbackImport = {
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

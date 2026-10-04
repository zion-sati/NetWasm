import assert from "node:assert/strict";
import test from "node:test";
import { buildRawLibraryAbiPlan } from "./raw-library-abi-plan-builder.mjs";

const layouts = Object.freeze({
  wasm32: Object.freeze({
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
  }),
  wasm64: Object.freeze({
    managedReferenceSize: 8,
    stringLengthOffset: 8,
    stringDataOffset: 12,
    arrayLengthOffset: 8,
    arrayDataPointerOffset: 16,
  }),
});
const statusAbi = Object.freeze({ successStatus: 0, hostFailureStatus: 1, scalarResultOffset: 0 });
const functionExport = name => ({ name, kind: "function" });
const memoryExport = name => ({ name, kind: "memory" });
const importDescriptor = (module = "consumer", name = "value") =>
  ({ module, name, kind: "function" });
const managedExport = (name = "add", result = "i32") => ({
  name,
  parameters: ["i32"],
  result,
  asyncReturn: null,
  statusExport: null,
  resultExport: null,
  completeExport: null,
});
const hostImport = (name = "increment") => ({
  module: "consumer",
  name,
  parameters: ["i32"],
  result: "i32",
  asyncReturn: null,
  resolveExport: null,
  rejectExport: null,
  cancelExport: null,
});
const manifest = (target = "wasm32", overrides = {}) => ({
  version: 1,
  target,
  statusAbi,
  targetLayout: layouts[target],
  imports: [],
  exports: [],
  callbacks: [],
  ...overrides,
});
const abi = (target = "wasm32", overrides = {}) => {
  const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
  return {
    target,
    entryPoint: null,
    imports: [],
    exports: [
      memoryExport(`${prefix}_memory`),
      functionExport(`${prefix}_realloc`),
      functionExport(`${prefix}_initialize`),
    ],
    ...overrides,
  };
};

test("plans immutable wasm32 and wasm64 library boundaries", () => {
  for (const [target, prefix] of [["wasm32", "cm32p2"], ["wasm64", "cm64p2"]]) {
    const synchronous = managedExport("add");
    const asynchronous = {
      ...managedExport("wait"),
      asyncReturn: "task",
      statusExport: "wait_status",
      resultExport: "wait_result",
      completeExport: "wait_complete", completionResult: "exception-handle-v1",
    };
    const asyncImport = {
      ...hostImport("fetch"),
      asyncReturn: "value-task",
      resolveExport: "fetch_resolve",
      rejectExport: "fetch_reject",
      cancelExport: "fetch_cancel",
    };
    const callback = {
      module: "consumer",
      importName: "listen",
      parameterIndex: 0,
      exportName: "listen_invoke",
      parameters: ["i32"],
      result: "void",
    };
    const contract = manifest(target, {
      imports: [asyncImport, {
        ...hostImport("listen"), parameters: ["callback"], result: "subscription",
      }],
      exports: [synchronous, asynchronous],
      callbacks: [callback],
    });
    const source = abi(target, {
      imports: [importDescriptor()],
      exports: [
        ...abi(target).exports,
        ...["add", "wait", "wait_status", "wait_result", "wait_complete",
          "fetch_resolve", "fetch_reject", "fetch_cancel", "listen_invoke"]
          .map(functionExport),
      ],
    });
    const plan = buildRawLibraryAbiPlan({ abi: source, manifest: contract });

    assert.deepEqual(plan, {
      target,
      imports: source.imports,
      exports: source.exports,
      reactorHostModule: `${prefix}|netwasm:runtime/reactor-host@1`,
      reactorGuestExport: `${prefix}|netwasm:runtime/reactor-guest@1|wake`,
      canonicalMemoryExport: `${prefix}_memory`,
      canonicalReallocateExport: `${prefix}_realloc`,
      canonicalInitializeExport: `${prefix}_initialize`,
      managedExportNames: ["add", "wait", "wait_status", "wait_complete", "wait_result",
        "listen_invoke", "fetch_resolve", "fetch_reject", "fetch_cancel"],
    });
    assert.equal(Object.isFrozen(plan), true);
    assert.equal(Object.isFrozen(plan.imports), true);
    assert.equal(Object.isFrozen(plan.exports), true);
    assert.equal(Object.isFrozen(plan.managedExportNames), true);
  }
});

test("retains void async exports without inventing a result helper", () => {
  const descriptor = {
    ...managedExport("notify", "void"),
    asyncReturn: "task",
    statusExport: "notify_status",
    completeExport: "notify_complete", completionResult: "exception-handle-v1",
  };
  delete descriptor.resultExport;
  const plan = buildRawLibraryAbiPlan({
    abi: abi("wasm32", { exports: [
      ...abi().exports,
      functionExport("notify"),
      functionExport("notify_status"),
      functionExport("notify_complete"),
    ] }),
    manifest: manifest("wasm32", { exports: [descriptor] }),
  });
  assert.deepEqual(plan.managedExportNames,
    ["notify", "notify_status", "notify_complete"]);
});

test("accepts compact synchronous manifests without optional members", () => {
  const contract = manifest();
  contract.imports = [{
    module: "consumer", name: "increment", parameters: ["i32"], result: "i32",
  }];
  contract.exports = [{ name: "add", parameters: ["i32"], result: "i32" }];
  delete contract.callbacks;
  const plan = buildRawLibraryAbiPlan({
    abi: abi("wasm32", { exports: [...abi().exports, functionExport("add")] }),
    manifest: contract,
  });
  assert.deepEqual(plan.managedExportNames, ["add"]);
});

test("rejects malformed planning requests and executable entry points", () => {
  const valid = { abi: abi(), manifest: manifest() };
  assert.throws(() => buildRawLibraryAbiPlan(), /planning request/);
  for (const request of [null, [], {}, { ...valid, extra: true }, {
    ...valid, [Symbol("invalid")]: true,
  }]) {
    assert.throws(() => buildRawLibraryAbiPlan(request), /planning request/);
  }
  const accessor = { ...valid };
  Object.defineProperty(accessor, "abi", { enumerable: true, get: () => abi() });
  assert.throws(() => buildRawLibraryAbiPlan(accessor), /planning request/);
  assert.throws(() => buildRawLibraryAbiPlan({
    ...valid,
    abi: abi("wasm32", { entryPoint: {
      parameterShape: "none", returnShape: "exitCode", completionShape: "synchronous",
    } }),
  }), /executable entry point/);
});

test("rejects mismatched targets and missing canonical exports", () => {
  assert.throws(() => buildRawLibraryAbiPlan({
    abi: abi("wasm32"),
    manifest: manifest("wasm64"),
  }), /target does not match/);

  for (const [name, replacement, expected] of [
    ["cm32p2_memory", functionExport("cm32p2_memory"), /memory export/],
    ["cm32p2_realloc", memoryExport("cm32p2_realloc"), /function export/],
    ["cm32p2_initialize", memoryExport("cm32p2_initialize"), /function export/],
  ]) {
    const exports = abi().exports.map(value => value.name === name ? replacement : value);
    assert.throws(() => buildRawLibraryAbiPlan({
      abi: abi("wasm32", { exports }), manifest: manifest(),
    }), expected);
    assert.throws(() => buildRawLibraryAbiPlan({
      abi: abi("wasm32", { exports: exports.filter(value => value.name !== name) }),
      manifest: manifest(),
    }), expected);
  }
});

test("rejects every missing or non-function manifest-owned export", () => {
  const descriptor = {
    ...managedExport("wait"),
    asyncReturn: "task",
    statusExport: "wait_status",
    resultExport: "wait_result",
    completeExport: "wait_complete", completionResult: "exception-handle-v1",
  };
  const imported = {
    ...hostImport("fetch"),
    asyncReturn: "task",
    resolveExport: "fetch_resolve",
    rejectExport: "fetch_reject",
    cancelExport: "fetch_cancel",
  };
  const callback = {
    module: "consumer", importName: "listen", parameterIndex: 0,
    exportName: "listen_invoke", parameters: [], result: "void",
  };
  const contract = manifest("wasm32", {
    imports: [imported, { ...hostImport("listen"), parameters: ["callback"], result: "subscription" }],
    exports: [descriptor], callbacks: [callback],
  });
  const names = ["wait", "wait_status", "wait_result", "wait_complete", "fetch_resolve",
    "fetch_reject", "fetch_cancel", "listen_invoke"];
  for (const name of names) {
    const base = [...abi().exports, ...names.map(functionExport)];
    for (const exports of [
      base.filter(value => value.name !== name),
      base.map(value => value.name === name ? memoryExport(name) : value),
    ]) {
      assert.throws(() => buildRawLibraryAbiPlan({
        abi: abi("wasm32", { exports }), manifest: contract,
      }), /function export/);
    }
  }
});

test("rejects fabricated executable wrappers but allows a declared run export", () => {
  for (const name of ["run", "netwasm.process.status", "netwasm.process.result",
    "netwasm.process.complete"]) {
    assert.throws(() => buildRawLibraryAbiPlan({
      abi: abi("wasm32", { exports: [...abi().exports, functionExport(name)] }),
      manifest: manifest(),
    }), /executable export/);
  }

  const plan = buildRawLibraryAbiPlan({
    abi: abi("wasm32", { exports: [...abi().exports, functionExport("run")] }),
    manifest: manifest("wasm32", { exports: [managedExport("run")] }),
  });
  assert.deepEqual(plan.managedExportNames, ["run"]);
});

import assert from "node:assert/strict";
import test from "node:test";
import { createLinkedCorpusProgram } from "./linked-corpus-program.mjs";

const digest = character => character.repeat(64);
const baseRequest = {
  schemaVersion: 1,
  target: "wasm32",
  modulePath: "module.wasm",
  manifestPath: "manifest.json",
  moduleSha256: digest("a"),
  manifestSha256: digest("b"),
  inputs: [-2147483648, 0, 2147483647],
  exposesLegacyTrace: true,
  usesTypedTrace: true,
};

function fixture(overrides = {}) {
  const instances = [];
  const calls = [];
  const dependencies = {
    async readBinary(path) { calls.push(["binary", path]); return new Uint8Array([1]); },
    async readText(path) { calls.push(["text", path]); return JSON.stringify({ target: baseRequest.target }); },
    async compileModule(bytes) { calls.push(["compile", bytes]); return { compiled: true }; },
    listImports(module) { calls.push(["imports", module]); return []; },
    hash(bytes) { return bytes[0] === 1 ? digest("a") : digest("b"); },
    createRuntimeImports(target, getMemory, getExports, sidecar) {
      calls.push(["runtime", target]);
      if (sidecar != null) calls.push(["sidecar", sidecar.fileName, [...sidecar.bytes]]);
      return { target, getMemory, getExports };
    },
    async instantiate(options) {
      calls.push(["instantiate", options]);
      const inputState = { disposed: false };
      const exports = {
        memory: { id: "memory" },
        run(input) {
          assert.equal(options.runtimeModules.getMemory(), exports.memory);
          assert.equal(options.runtimeModules.getExports(), exports);
          assert.equal(inputState.disposed, false);
          return input;
        },
        trace() { return -7; },
        trace_count() { return 1; },
        trace_kind() { return 2; },
        trace_event_id() { return 3; },
        trace_payload_low() { return 4; },
        trace_payload_high() { return -1; },
      };
      const managed = { instance: { exports }, dispose() { inputState.disposed = true; } };
      instances.push(inputState);
      return managed;
    },
    ...overrides,
  };
  return { program: createLinkedCorpusProgram(dependencies), calls, instances };
}

test("both targets preserve ordered value observations, traces, identity and fresh instances", async () => {
  for (const target of ["wasm32", "wasm64"]) {
    const state = fixture({
      async readText(path) {
        state.calls.push(["text", path]);
        return JSON.stringify({ target });
      },
    });
    const response = await state.program.run({ ...baseRequest, target });
    assert.equal(response.schemaVersion, 1);
    assert.equal(response.target, target);
    assert.equal(response.moduleSha256, digest("a"));
    assert.equal(response.manifestSha256, digest("b"));
    assert.deepEqual(response.observations.map(item => item.input), baseRequest.inputs);
    assert.deepEqual(response.observations.map(({ kind, value, trace, traceRecords }) =>
      ({ kind, value, trace, traceRecords })), baseRequest.inputs.map(value => ({
        kind: "value", value, trace: -7,
        traceRecords: [{ kind: 2, eventId: 3, payloadLow: 4, payloadHigh: -1 }],
      })));
    assert.equal(state.instances.length, 3);
    assert.ok(state.instances.every(instance => instance.disposed));
    assert.deepEqual(state.calls.filter(([kind]) => kind === "runtime").map(([, value]) => value),
      [target, target, target]);
  }
});

test("a method-symbols sidecar is read once and handed to every instance's runtime imports", async () => {
  const state = fixture();
  await state.program.run({
    ...baseRequest, methodSymbols: { path: "app.method-symbols", fileName: "App.wasm.method-symbols" } });
  assert.deepEqual(state.calls.filter(([kind]) => kind === "binary").map(([, path]) => path),
    [baseRequest.modulePath, "app.method-symbols"]);
  const sidecars = state.calls.filter(([kind]) => kind === "sidecar");
  assert.equal(sidecars.length, 3);
  assert.ok(sidecars.every(([, fileName, bytes]) => fileName === "App.wasm.method-symbols" && bytes.length === 1));
  assert.ok(fixture().calls.every(([kind]) => kind !== "sidecar"));
});

test("managed exceptions and raw traps remain distinct observations", async () => {
  let invocation = 0;
  const state = fixture({
    async instantiate(options) {
      const current = invocation++;
      return {
        instance: { exports: {
          run() {
            if (current === 0) options.managedExceptionReporting.reportImmediate({ typeId: 17 });
            throw new WebAssembly.RuntimeError("terminal");
          },
        } },
        dispose() {},
      };
    },
  });
  const response = await state.program.run({ ...baseRequest, inputs: [1, 2],
    exposesLegacyTrace: false, usesTypedTrace: false });
  assert.deepEqual(response.observations, [
    { input: 1, kind: "exception", value: null, exceptionTypeId: 17, trace: 0, traceRecords: [] },
    { input: 2, kind: "trap", value: null, exceptionTypeId: null, trace: 0, traceRecords: [] },
  ]);
});

test("invalid requests fail before I/O", async () => {
  const invalid = [null, {}, { ...baseRequest, schemaVersion: 2 },
    { ...baseRequest, target: "component" }, { ...baseRequest, modulePath: "" },
    { ...baseRequest, manifestPath: "" }, { ...baseRequest, moduleSha256: "A".repeat(64) },
    { ...baseRequest, manifestSha256: "short" }, { ...baseRequest, exposesLegacyTrace: null },
    { ...baseRequest, usesTypedTrace: null }, { ...baseRequest, inputs: [] },
    { ...baseRequest, inputs: [0, 0] }, { ...baseRequest, inputs: [1.5] },
    { ...baseRequest, inputs: [-2147483649] }, { ...baseRequest, inputs: [2147483648] },
    { ...baseRequest, methodSymbols: {} }, { ...baseRequest, methodSymbols: { path: "", fileName: "App.wasm.method-symbols" } },
    { ...baseRequest, methodSymbols: { path: "app.method-symbols", fileName: "" } },
    { ...baseRequest, methodSymbols: { path: "app.method-symbols", fileName: "dir/App.wasm.method-symbols" } }];
  for (const request of invalid) {
    const state = fixture();
    await assert.rejects(state.program.run(request), TypeError);
    assert.deepEqual(state.calls, []);
  }
});

test("artifact, target and Preview 1 mismatches fail before instantiation", async () => {
  for (const overrides of [
    { hash: () => digest("c") },
    { readText: async () => JSON.stringify({ target: "wasm64" }) },
    { listImports: () => [{ module: "wasi_snapshot_preview1" }] },
    { listImports: () => [{ module: "wasi_unstable" }] },
  ]) {
    let instantiated = false;
    const state = fixture({ ...overrides, instantiate: async () => { instantiated = true; } });
    await assert.rejects(state.program.run(baseRequest), TypeError);
    assert.equal(instantiated, false);
  }
});

test("ordinary Preview 2 imports are accepted and dependency failures propagate", async () => {
  const accepted = fixture({ listImports: () => [{ module: "cm32p2|wasi:cli/environment@0.2" }] });
  assert.equal((await accepted.program.run({ ...baseRequest, inputs: [0] })).observations.length, 1);
  const cause = new Error("read failed");
  const failed = fixture({ readBinary: async () => { throw cause; } });
  await assert.rejects(failed.program.run(baseRequest), error => error === cause);
});

test("all capabilities are required", () => {
  const valid = fixture();
  const names = ["readBinary", "readText", "compileModule", "listImports", "hash",
    "instantiate", "createRuntimeImports"];
  for (const name of names) {
    const dependencies = Object.fromEntries(names.map(capability =>
      [capability, capability === name ? null : () => {}]));
    assert.throws(() => createLinkedCorpusProgram(dependencies), TypeError);
  }
  assert.ok(valid.program);
});

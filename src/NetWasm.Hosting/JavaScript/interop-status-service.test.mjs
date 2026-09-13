import assert from "node:assert/strict";
import test from "node:test";

import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { createInteropStatusService } from "./interop-status-service.mjs";
import {
  writeManagedInteropBytes,
  writeManagedInteropString,
} from "./managed-interop-memory-codec.mjs";

const layout = Object.freeze({
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
});
const statusAbi = Object.freeze({
  successStatus: 0,
  hostFailureStatus: 1,
  scalarResultOffset: 0,
});
const baseDescriptor = Object.freeze({
  module: "example:app/interop@1.0.0",
  name: "service",
  parameters: Object.freeze([]),
  result: "void",
});

function descriptor(overrides = {}) {
  return { ...baseDescriptor, ...overrides };
}

function request(overrides = {}) {
  const memory = new WebAssembly.Memory({ initial: 1 });
  return {
    callbacks: [],
    descriptor: descriptor(),
    exceptionReporter: { consumeTerminalEvent: () => null },
    getInstance: () => ({ exports: {} }),
    getMemory: () => memory,
    handles: createInteropHandleTable(),
    pendingAsyncOperations: new Map(),
    service() {},
    statusAbi,
    target: "wasm32",
    targetLayout: layout,
    ...overrides,
  };
}

const tick = () => new Promise(resolve => setImmediate(resolve));

test("lifts every host-service argument family and writes a scalar result", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const view = new DataView(memory.buffer);
  view.setInt32(32 + layout.stringLengthOffset, 4, true);
  writeManagedInteropString({
    memory, reference: 32, target: "wasm32", targetLayout: layout, value: "text",
  });
  view.setInt32(64 + layout.arrayLengthOffset, 3, true);
  view.setUint32(64 + layout.arrayDataPointerOffset, 128, true);
  writeManagedInteropBytes({
    memory, reference: 64, target: "wasm32", targetLayout: layout,
    value: new Uint8Array([1, 2, 3]),
  });
  const handles = createInteropHandleTable();
  const object = {};
  const subscription = { dispose() {} };
  const objectHandle = handles.acquire(object);
  const subscriptionHandle = handles.acquire(subscription);
  const callbacks = [{
    module: baseDescriptor.module,
    importName: baseDescriptor.name,
    parameterIndex: 4,
    exportName: "callback_1",
    parameters: ["i32"],
    result: "i32",
  }];
  const invoke = createInteropStatusService(request({
    callbacks,
    descriptor: descriptor({
      parameters: ["string", "bytes", "object", "subscription", "callback", "i32"],
      result: "i32",
    }),
    getInstance: () => ({ exports: { callback_1: (handle, value) => handle + value } }),
    getMemory: () => memory,
    handles,
    service(string, bytes, objectValue, subscriptionValue, callback, scalar) {
      assert.equal(string, "text");
      assert.deepEqual([...bytes], [1, 2, 3]);
      assert.equal(objectValue, object);
      assert.equal(subscriptionValue, subscription);
      assert.equal(callback(5), 14);
      assert.equal(scalar, 41);
      return 42;
    },
  }));
  assert.equal(Object.isFrozen(invoke), true);
  assert.equal(invoke(32, 64, objectHandle, subscriptionHandle, 9, 41, 0), 0);
  assert.equal(view.getInt32(0, true), 42);

  const nulls = createInteropStatusService(request({
    callbacks,
    descriptor: descriptor({
      parameters: ["string", "bytes", "object", "subscription", "callback"],
    }),
    service(...values) { assert.deepEqual(values, [null, null, null, null, null]); },
  }));
  assert.equal(nulls(0, 0, 0, 0, 0, 0), 0);
});

test("writes every reference-shaped host-service result", () => {
  const cases = [
    ["string", "value", value => assert.equal(value, "value")],
    ["string", null, value => assert.equal(value, null)],
    ["bytes", new Uint8Array([1, 2]), value => assert.deepEqual([...value], [1, 2])],
    ["bytes", null, value => assert.equal(value, null)],
    ["object", { key: 1 }, value => assert.deepEqual(value, { key: 1 })],
    ["object", () => 1, value => assert.equal(value(), 1)],
    ["object", null, value => assert.equal(value, null)],
  ];
  for (const [result, value, assertValue] of cases) {
    const state = request({ descriptor: descriptor({ result }), service: () => value });
    const invoke = createInteropStatusService(state);
    assert.equal(invoke(0), 0);
    const handle = new DataView(state.getMemory().buffer).getInt32(0, true);
    if (value === null) assert.equal(handle, 0);
    else {
      assertValue(state.handles.get(handle));
      if (result === "bytes") assert.notStrictEqual(state.handles.get(handle), value);
    }
  }

  const calls = [];
  const subscription = { dispose() { calls.push("dispose"); } };
  const state = request({
    descriptor: descriptor({ result: "subscription" }),
    service: () => subscription,
  });
  assert.equal(createInteropStatusService(state)(0), 0);
  const handle = new DataView(state.getMemory().buffer).getInt32(0, true);
  assert.equal(state.handles.get(handle), subscription);
  state.handles.releaseSubscription(handle);
  assert.deepEqual(calls, ["dispose"]);
});

test("maps invalid synchronous service values and invocations to host failure", () => {
  const cases = [
    ["i32", "not an integer"], ["string", 1], ["bytes", []], ["object", 1],
    ["subscription", {}], ["promise", {}], ["unknown", null],
  ];
  for (const [result, value] of cases) {
    const state = request({ descriptor: descriptor({ result }), service: () => value });
    assert.equal(createInteropStatusService(state)(0), 1);
  }
  const failure = request({ service() { throw new Error("private"); } });
  assert.equal(createInteropStatusService(failure)(0), 1);
});

test("settles Promise-style callback services and owns callback releases", async () => {
  for (const rejected of [false, true]) {
    const calls = [];
    const state = request({
      callbacks: [0, 1].map((parameterIndex) => ({
        module: baseDescriptor.module,
        importName: baseDescriptor.name,
        parameterIndex,
        exportName: parameterIndex === 0 ? "success" : "failure",
        parameters: parameterIndex === 0 ? ["i32"] : [],
        result: "void",
      })),
      descriptor: descriptor({ parameters: ["callback", "callback"], result: "promise" }),
      getInstance: () => ({ exports: {
        success: (handle, value) => calls.push(["success", handle, value]),
        failure: handle => calls.push(["failure", handle]),
        handle_release: handle => calls.push(["release", handle]),
      } }),
      service: () => rejected ? Promise.reject(new Error("rejected")) : Promise.resolve(37),
    });
    assert.equal(createInteropStatusService(state)(7, 8, 0), 0);
    const handle = new DataView(state.getMemory().buffer).getInt32(0, true);
    await tick();
    assert.deepEqual(calls[0], rejected ? ["failure", 8] : ["success", 7, 37]);
    state.handles.releaseSubscription(handle);
    assert.deepEqual(calls.slice(1), [["release", 7], ["release", 8]]);
  }

  const invalid = request({
    callbacks: [{
      module: baseDescriptor.module, importName: baseDescriptor.name,
      parameterIndex: 0, exportName: "success", parameters: [], result: "void",
    }],
    descriptor: descriptor({ parameters: ["callback"], result: "promise" }),
  });
  assert.equal(createInteropStatusService(invalid)(7, 0), 1);
});

test("suppresses Promise-style callbacks after subscription disposal", async () => {
  for (const rejected of [false, true]) {
    let settle;
    const calls = [];
    const state = request({
      callbacks: [0, 1].map(parameterIndex => ({
        module: baseDescriptor.module, importName: baseDescriptor.name, parameterIndex,
        exportName: parameterIndex === 0 ? "success" : "failure",
        parameters: parameterIndex === 0 ? ["i32"] : [], result: "void",
      })),
      descriptor: descriptor({ parameters: ["callback", "callback"], result: "promise" }),
      getInstance: () => ({ exports: {
        success: () => calls.push("success"), failure: () => calls.push("failure"),
        handle_release: () => {},
      } }),
      service: () => new Promise((resolve, reject) => {
        settle = rejected ? () => reject(new Error("failure")) : () => resolve(1);
      }),
    });
    createInteropStatusService(state)(7, 8, 0);
    const handle = new DataView(state.getMemory().buffer).getInt32(0, true);
    state.handles.releaseSubscription(handle);
    settle();
    await tick();
    assert.deepEqual(calls, []);
  }
});

test("settles and cancels task-style asynchronous services", async () => {
  for (const mode of ["scalar", "void", "invalid", "rejected"]) {
    const calls = [];
    const state = request({
      descriptor: descriptor({
        asyncReturn: "task",
        resolveExport: "resolve",
        rejectExport: "reject",
        cancelExport: "cancel",
        result: mode === "void" ? "void" : "i32",
      }),
      getInstance: () => ({ exports: {
        resolve: (...values) => calls.push(["resolve", ...values]),
        reject: (...values) => calls.push(["reject", ...values]),
        cancel: (...values) => calls.push(["cancel", ...values]),
      } }),
      service: () => mode === "rejected" ? Promise.reject(new Error("failure"))
        : Promise.resolve(mode === "invalid" ? "bad" : 37),
    });
    assert.equal(createInteropStatusService(state)(5, 0), 0);
    assert.equal(state.pendingAsyncOperations.has(5), true);
    await tick();
    assert.equal(state.pendingAsyncOperations.size, 0);
    assert.deepEqual(calls, mode === "scalar" ? [["resolve", 5, 37]]
      : mode === "void" ? [["resolve", 5]] : [["reject", 5]]);
  }

  for (const rejected of [false, true]) {
    let settle;
    const calls = [];
    const state = request({
      descriptor: descriptor({
        asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject",
        cancelExport: "cancel", result: "i32",
      }),
      getInstance: () => ({ exports: {
        resolve: () => calls.push("resolve"), reject: () => calls.push("reject"),
        cancel: handle => calls.push(["cancel", handle]),
      } }),
      service: () => new Promise((resolve, reject) => {
        settle = rejected ? () => reject(new Error("failure")) : () => resolve(1);
      }),
    });
    createInteropStatusService(state)(9, 0);
    const operation = state.pendingAsyncOperations.get(9);
    operation.cancel();
    operation.cancel();
    settle();
    await tick();
    assert.deepEqual(calls, [["cancel", 9]]);
  }
});

test("rejects unavailable or synchronously failing task completion", () => {
  const asyncDescriptor = descriptor({
    asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject",
    cancelExport: "cancel", result: "i32",
  });
  for (const exports of [
    {}, { resolve() {} }, { resolve() {}, reject() {} },
    { resolve() {}, reject() {}, cancel: null },
  ]) {
    const state = request({ descriptor: asyncDescriptor, getInstance: () => ({ exports }) });
    assert.equal(createInteropStatusService(state)(5, 0), 1);
  }
  const state = request({
    descriptor: asyncDescriptor,
    getInstance: () => ({ exports: { resolve() {}, reject() {}, cancel() {} } }),
    service() { throw new Error("sync failure"); },
  });
  assert.equal(createInteropStatusService(state)(5, 0), 1);
  assert.equal(state.pendingAsyncOperations.size, 0);
});

test("rejects malformed status-service composition", () => {
  const valid = request();
  for (const value of [
    null, [], {}, { ...valid, extra: true }, { ...valid, [Symbol("invalid")]: true },
    Object.defineProperty({ ...valid }, "service", { enumerable: true, get: () => valid.service }),
  ]) assert.throws(() => createInteropStatusService(value), TypeError);
  for (const value of [
    { service: null }, { getMemory: null }, { getInstance: null },
    { pendingAsyncOperations: {} },
  ]) assert.throws(() => createInteropStatusService({ ...valid, ...value }), TypeError);
});

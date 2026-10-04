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
    assertAsyncDeliveryAvailable() {},
    callbacks: [],
    descriptor: descriptor(),
    exceptionReporter: { consumeTerminalEvent: () => null },
    getInstance: () => ({ exports: {} }),
    getMemory: () => memory,
    handles: createInteropHandleTable(),
    observeAsyncFailure() {},
    pendingAsyncOperations: new Map(),
    service() {},
    statusAbi,
    target: "wasm32",
    targetLayout: layout,
    ...overrides,
  };
}

const tick = () => new Promise(resolve => setImmediate(resolve));

test("notifies process observation after an externally invoked subscription callback", async () => {
  let callback;
  const observations = [];
  const handles = createInteropHandleTable();
  const state = request({
    handles,
    callbacks: [{
      module: baseDescriptor.module, importName: baseDescriptor.name,
      parameterIndex: 0, exportName: "callback", parameters: ["bytes"], result: "i32",
    }],
    descriptor: descriptor({ parameters: ["callback"], result: "subscription" }),
    getInstance: () => ({ exports: {
      callback(handle, bytes) {
        assert.equal(handle, 7);
        assert.deepEqual([...handles.get(bytes)], [21]);
        return 42;
      },
    } }),
    service(value) { callback = value; return { dispose() {} }; },
  });
  const invoke = createInteropStatusService(state, error => observations.push({ error, handles: handles.count }));
  assert.equal(invoke(7, 0), 0);
  assert.deepEqual(observations, []);
  assert.equal(callback(new Uint8Array([21])), 42);
  assert.deepEqual(observations, []);
  await tick();
  assert.deepEqual(observations, [{ error: undefined, handles: 1 }]);
});

test("defers reentrant callback observation until the host service returns", async () => {
  const events = [];
  const invoke = createInteropStatusService(request({
    callbacks: [{
      module: baseDescriptor.module, importName: baseDescriptor.name,
      parameterIndex: 0, exportName: "callback", parameters: [], result: "void",
    }],
    descriptor: descriptor({ parameters: ["callback"] }),
    getInstance: () => ({ exports: { callback() { events.push("callback"); } } }),
    service(callback) { callback(); events.push("service-return"); },
  }), () => { events.push("observe"); throw new Error("observer failure"); });
  assert.equal(invoke(7, 0), 0);
  events.push("guest-return");
  assert.deepEqual(events, ["callback", "service-return", "guest-return"]);
  await tick();
  assert.deepEqual(events, ["callback", "service-return", "guest-return", "observe"]);
});

test("preserves callback failures while notifying the process after cleanup", async () => {
  for (const cause of [new WebAssembly.RuntimeError("trap"), new Error("foreign"), undefined]) {
    let callback;
    const observations = [];
    const handles = createInteropHandleTable();
    const state = request({
      handles,
      callbacks: [{
        module: baseDescriptor.module, importName: baseDescriptor.name,
        parameterIndex: 0, exportName: "callback", parameters: ["bytes"], result: "void",
      }],
      descriptor: descriptor({ parameters: ["callback"] }),
      getInstance: () => ({ exports: { callback() { throw cause; } } }),
      service(value) { callback = value; },
    });
    assert.equal(createInteropStatusService(state, error => observations.push(error))(7, 0), 0);
    let failure;
    assert.throws(() => callback(new Uint8Array([21])), error => { failure = error; return true; });
    assert.equal(handles.count, 0);
    assert.deepEqual(observations, []);
    await tick();
    assert.deepEqual(observations, [failure]);
  }
});

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
    const observations = [];
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
    assert.equal(createInteropStatusService(state, error => observations.push(error))(7, 8, 0), 0);
    const handle = new DataView(state.getMemory().buffer).getInt32(0, true);
    await tick();
    assert.deepEqual(calls[0], rejected ? ["failure", 8] : ["success", 7, 37]);
    assert.deepEqual(observations, [undefined]);
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

test("contains Promise-style managed callback failures", async () => {
  for (const rejected of [false, true]) {
    const failures = [];
    const state = request({
      callbacks: [0, 1].map(parameterIndex => ({
        module: baseDescriptor.module, importName: baseDescriptor.name, parameterIndex,
        exportName: parameterIndex === 0 ? "success" : "failure",
        parameters: parameterIndex === 0 ? ["i32"] : [], result: "void",
      })),
      descriptor: descriptor({ parameters: ["callback", "callback"], result: "promise" }),
      getInstance: () => ({ exports: {
        success() { throw new Error("success failed"); },
        failure() { throw new Error("failure failed"); },
        handle_release() {},
      } }),
      observeAsyncFailure: cause => failures.push(cause),
      service: () => rejected ? Promise.reject(new Error("service failed")) : Promise.resolve(37),
    });
    assert.equal(createInteropStatusService(state)(7, 8, 0), 0);
    await tick();
    assert.equal(failures.length, 1);
    assert.match(failures[0].message, rejected ? /failure/ : /success/);
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

test("contains asynchronous guest completion failures and reports them once", async () => {
  for (const mode of ["resolve", "reject", "invalid", "observer-failure"]) {
    const failures = [];
    const state = request({
      descriptor: descriptor({
        asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject",
        cancelExport: "cancel", result: "i32",
      }),
      getInstance: () => ({ exports: {
        resolve() { throw new Error("resolve failed"); },
        reject() { throw new Error("reject failed"); },
        cancel() {},
      } }),
      observeAsyncFailure(cause) {
        failures.push(cause);
        if (mode === "observer-failure") throw new Error("observer failed");
      },
      service: () => mode === "reject" || mode === "observer-failure"
        ? Promise.reject(new Error("service failed"))
        : Promise.resolve(mode === "invalid" ? "invalid" : 42),
    });
    assert.equal(createInteropStatusService(state)(17, 0), 0);
    await tick();
    assert.equal(state.pendingAsyncOperations.size, 0);
    assert.equal(failures.length, 1);
    assert.match(failures[0].message, mode === "resolve" ? /resolve/ : /reject/);
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
    { assertAsyncDeliveryAvailable: null },
    { observeAsyncFailure: null },
    { pendingAsyncOperations: {} },
  ]) assert.throws(() => createInteropStatusService({ ...valid, ...value }), TypeError);
});

test("notifies process observation after asynchronous guest delivery", async () => {
  for (const mode of ["success", "failure", "undefined-failure", "observer-failure"]) {
    const calls = [];
    const cause = mode === "undefined-failure" ? undefined : new Error("delivery failure");
    const state = request({
      descriptor: descriptor({ asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject", cancelExport: "cancel", result: "i32" }),
      getInstance: () => ({ exports: {
        resolve() { calls.push("guest"); if (mode === "failure" || mode === "undefined-failure") throw cause; },
        reject() {}, cancel() {},
      } }),
      observeAsyncFailure(error) { calls.push("failure"); assert.equal(error, cause); },
      service: () => Promise.resolve(42),
    });
    const invoke = createInteropStatusService(state, error => {
      assert.equal(state.pendingAsyncOperations.size, 0);
      calls.push("observe");
      if (mode === "failure") assert.equal(error, cause);
      else if (mode === "undefined-failure") assert.notEqual(error, undefined);
      else assert.equal(error, undefined);
      if (mode === "observer-failure") throw new Error("observer failure");
    });
    assert.equal(invoke(17, 0), 0);
    await tick();
    assert.deepEqual(calls, mode === "failure" || mode === "undefined-failure"
      ? ["guest", "failure", "observe"] : ["guest", "observe"]);
  }
  assert.throws(() => createInteropStatusService(request(), null), /actions are invalid/);
  assert.throws(() => createInteropStatusService(request(), () => {}, null), /actions are invalid/);
});

test("preserves an undefined callback binding failure and reports a nonempty wake failure", async () => {
  let callback;
  const observations = [];
  const state = request({
    callbacks: [{
      module: baseDescriptor.module, importName: baseDescriptor.name,
      parameterIndex: 0, exportName: "callback", parameters: [], result: "void",
    }],
    descriptor: descriptor({ parameters: ["callback"] }),
    getInstance() { throw undefined; },
    service(value) { callback = value; },
  });
  assert.equal(createInteropStatusService(state, error => observations.push(error))(7, 0), 0);
  assert.throws(() => callback(), error => error === undefined);
  await tick();
  assert.equal(observations.length, 1);
  assert.notEqual(observations[0], undefined);
});

test("blocks external callback entry after managed process observation has finished", async () => {
  let callback;
  let available = true;
  let calls = 0;
  const invoke = createInteropStatusService(request({
    callbacks: [{
      module: baseDescriptor.module, importName: baseDescriptor.name,
      parameterIndex: 0, exportName: "callback", parameters: [], result: "void",
    }],
    descriptor: descriptor({ parameters: ["callback"] }),
    assertAsyncDeliveryAvailable() { if (!available) throw new Error("finished"); },
    getInstance: () => ({ exports: { callback() { calls++; } } }),
    service(value) { callback = value; },
  }), () => { available = false; });
  assert.equal(invoke(7, 0), 0);
  callback();
  await tick();
  assert.equal(calls, 1);
  assert.throws(() => callback(), /finished/);
  await tick();
  assert.equal(calls, 1);
});

test("blocks a queued completion when an earlier delivery settles the process", async () => {
  let available = true;
  let guestEntries = 0;
  let notifications = 0;
  const state = request({
    descriptor: descriptor({ asyncReturn: "task", resolveExport: "resolve", rejectExport: "reject", cancelExport: "cancel", result: "i32" }),
    assertAsyncDeliveryAvailable() { if (!available) throw new Error("execution finished"); },
    getInstance: () => ({ exports: {
      resolve() { guestEntries++; throw new Error("guest delivery failed"); },
      reject() { assert.fail("the successful service does not reject"); }, cancel() {},
    } }),
    service: () => Promise.resolve(42),
  });
  const invoke = createInteropStatusService(state, error => {
    if (!available) return;
    assert.notEqual(error, undefined);
    notifications++;
    available = false;
  });
  assert.equal(invoke(17, 0), 0);
  assert.equal(invoke(18, 0), 0);
  await tick();
  assert.equal(guestEntries, 1);
  assert.equal(notifications, 1);
  assert.equal(state.pendingAsyncOperations.size, 0);
});

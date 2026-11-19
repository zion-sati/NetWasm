// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import {
  createManagedInteropCallback,
  releaseManagedInteropCallback,
} from "./managed-interop-callback.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { NetWasmHostError, NetWasmManagedError } from "./managed-errors.mjs";
import { createValueArrayStaging } from "./value-array-staging.mjs";

const callback = (overrides = {}) => ({
  module: "example:app/interop@1.0.0",
  importName: "subscribe",
  parameterIndex: 0,
  exportName: "callback_1",
  parameters: ["i32"],
  result: "i32",
  ...overrides,
});
const descriptor = Object.freeze({ module: callback().module, name: callback().importName });
const reporter = (event = null) => ({ consumeTerminalEvent: () => event });
const memory = new WebAssembly.Memory({ initial: 1 });
const targetLayout = {
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
  spanReferenceOffset: 0,
  spanLengthOffset: 4,
};

// The runtime's allocator over the test memory: blocks from a bump pointer, every freed block recorded.
function stagingOver(stagingMemory, start = 4096) {
  let next = start;
  const freed = [];
  const runtime = {
    native_alloc(size) { const address = next; next += size; return address; },
    native_free(address) { freed.push(address); },
  };
  const staging = createValueArrayStaging({
    getInstance: () => ({ exports: runtime }), getMemory: () => stagingMemory,
    runtimeModules: {}, target: "wasm32",
  });
  return { staging, freed, read: (address, constructor, length) =>
    [...new constructor(stagingMemory.buffer.slice(address, address + length * constructor.BYTES_PER_ELEMENT))] };
}

function request(overrides = {}) {
  const handles = createInteropHandleTable();
  return {
    callbacks: [callback()],
    descriptor,
    exceptionReporter: reporter(),
    getInstance: () => ({ exports: { callback_1: (_handle, value) => value + 1 } }),
    getMemory: () => memory,
    handle: 7,
    handles,
    parameterIndex: 0,
    staging: stagingOver(new WebAssembly.Memory({ initial: 1 })).staging,
    target: "wasm32",
    targetLayout,
    ...overrides,
  };
}

test("binds scalar and void managed callbacks to the exact handle", () => {
  const invoke = createManagedInteropCallback(request());
  assert.equal(Object.isFrozen(invoke), true);
  assert.equal(invoke(41), 42);
  const voidInvoke = createManagedInteropCallback(request({
    callbacks: [callback({ result: "void" })],
    getInstance: () => ({ exports: { callback_1: () => 99 } }),
  }));
  assert.equal(voidInvoke(1), undefined);
  assert.equal(createManagedInteropCallback(request({ handle: 0 })), null);
});

// A string argument is lent through a handle for the call; a number array is staged in linear memory, and the
// managed callback copies and frees its block (NetWasm.UI's events take an Action<int, byte[]>).
test("lends string handles and stages number arrays for one invocation", () => {
  const handles = createInteropHandleTable();
  const { staging, freed, read } = stagingOver(new WebAssembly.Memory({ initial: 1 }));
  const invoke = createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["string", "bytes", "string", "bytes", "u16[]"] })],
    handles,
    staging,
    getInstance: () => ({ exports: {
      callback_1(handle, stringHandle, bytesAddress, bytesLength, nullString, nullAddress, nullLength,
        valuesAddress, valuesLength) {
        assert.equal(handle, 7);
        assert.equal(handles.get(stringHandle), "value");
        assert.deepEqual(read(bytesAddress, Uint8Array, bytesLength), [1, 2, 3]);
        assert.deepEqual([nullString, nullAddress, nullLength], [0, 0, -1]);
        assert.deepEqual(read(valuesAddress, Uint16Array, valuesLength), [1, 65_535]);
        return 5;
      },
    } }),
  }));
  assert.equal(invoke("value", new Uint8Array([1, 2, 3]), null, null, Uint16Array.of(1, 65_535)), 5);
  assert.equal(handles.count, 0);
  assert.deepEqual(freed, []);

  for (const [parameters, value, pattern] of [
    [["string"], 1, /string value must be a string or null/],
    [["bytes"], "text", /byte-array value must be Uint8Array, an array, or null/],
    [["f32[]"], Float64Array.of(1), /f32\[\] value must be Float32Array, an array, or null/],
  ]) {
    const invalid = createManagedInteropCallback(request({
      callbacks: [callback({ parameters })],
    }));
    let caught;
    try { invalid(value); } catch (error) { caught = error; }
    assert.equal(caught instanceof NetWasmManagedError, true);
    assert.match(caught.cause.message, pattern);
  }
});

// A block staged for a call that never reaches the managed callback is freed by the host.
test("frees staged arrays when a later callback argument cannot cross", () => {
  const { staging, freed } = stagingOver(new WebAssembly.Memory({ initial: 1 }));
  let calls = 0;
  const invoke = createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["bytes", "string"] })],
    staging,
    getInstance: () => ({ exports: { callback_1() { calls++; return 0; } } }),
  }));
  assert.throws(() => invoke(Uint8Array.of(9), 3), NetWasmManagedError);
  assert.equal(calls, 0);
  assert.deepEqual(freed, [4096]);
});

// A callback takes and returns what a synchronous export does.
test("passes JSObject arguments and lifts string, array and JSObject callback results", () => {
  const handles = createInteropHandleTable();
  const view = new DataView(memory.buffer);
  const text = 1024;
  view.setInt32(text + targetLayout.stringLengthOffset, 1, true);
  view.setUint16(text + targetLayout.stringDataOffset, "z".charCodeAt(0), true);
  const page = { id: "page" };
  let objectHandle;
  const results = { string: text, "i32[]": 0, object: 0 };
  const callbackFor = result => createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["object", "string", "string"], result })],
    handles,
    getInstance: () => ({ exports: {
      callback_1(handle, element, first, second) {
        objectHandle = element;
        assert.strictEqual(handles.get(element), page);
        assert.deepEqual([handles.get(first), handles.get(second)], ["a", "b"]);
        return result === "object" ? element : results[result];
      },
    } }),
  }));
  assert.equal(callbackFor("string")(page, "a", "b"), "z");
  assert.equal(callbackFor("i32[]")(page, "a", "b"), null);
  assert.strictEqual(callbackFor("object")(page, "a", "b"), page);
  // The managed wrapper owns each JSObject handle once the call starts.
  assert.equal(handles.count, 3);
  assert.strictEqual(handles.get(objectHandle), page);

  // A later invalid argument releases the JSObject handle before the call starts.
  const before = handles.count;
  assert.throws(() => callbackFor("string")(page, 1, "b"), NetWasmManagedError);
  assert.equal(handles.count, before);
});

test("maps callback traps and continues transient cleanup", () => {
  for (const event of [null, { typeId: 47 }]) {
    const handles = createInteropHandleTable();
    const trap = new WebAssembly.RuntimeError("trap");
    const invoke = createManagedInteropCallback(request({
      callbacks: [callback({ parameters: ["string"] })],
      exceptionReporter: reporter(event),
      handles,
      getInstance: () => ({ exports: { callback_1() { throw trap; } } }),
    }));
    assert.throws(() => invoke("temporary"), error => event === null
      ? error === trap
      : error instanceof NetWasmManagedError && error.managedType === 47);
    assert.equal(handles.count, 0);
  }

  const handles = createInteropHandleTable();
  const transientReleaseFailure = {
    acquire: handles.acquire,
    release() { throw new Error("release failed"); },
  };
  const invoke = createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["string"] })],
    handles: transientReleaseFailure,
    getInstance: () => ({ exports: { callback_1: () => { throw new Error("failure"); } } }),
  }));
  assert.throws(() => invoke("temporary"), NetWasmManagedError);

  const reporterHandles = createInteropHandleTable();
  const reporterFailure = new Error("reporter failed");
  const reporterInvoke = createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["string"] })],
    exceptionReporter: { consumeTerminalEvent() { throw reporterFailure; } },
    handles: reporterHandles,
    getInstance: () => ({ exports: {
      callback_1() { throw new WebAssembly.RuntimeError("trap"); },
    } }),
  }));
  assert.throws(() => reporterInvoke("temporary"), error => error === reporterFailure);
  assert.equal(reporterHandles.count, 0);
});

test("rejects unavailable callback exports and missing descriptors", () => {
  for (const instance of [null, {}, { exports: { callback_1: null } }]) {
    const unavailable = createManagedInteropCallback(request({ getInstance: () => instance }));
    assert.throws(() => unavailable(1), NetWasmHostError);
  }
  assert.throws(() => createManagedInteropCallback(request({ callbacks: [] })), /missing callback/);
});

test("releases a managed callback handle through the bound instance", () => {
  const calls = [];
  releaseManagedInteropCallback({
    getInstance: () => ({ exports: { handle_release: value => calls.push(value) } }),
    handle: -1,
  });
  assert.deepEqual(calls, [0xffff_ffff]);
  assert.throws(() => releaseManagedInteropCallback({
    getInstance: () => ({}), handle: 1,
  }), /unavailable/);
  assert.throws(() => releaseManagedInteropCallback({
    getInstance: () => null, handle: 1,
  }), /unavailable/);
  assert.throws(() => releaseManagedInteropCallback({
    getInstance: null, handle: 1,
  }), /reader/);
});

test("rejects malformed callback composition requests", () => {
  const valid = request();
  for (const value of [
    null, [], {}, { ...valid, extra: true }, { ...valid, [Symbol("invalid")]: true },
    Object.defineProperty({ ...valid }, "handle", { enumerable: true, get: () => 7 }),
  ]) assert.throws(() => createManagedInteropCallback(value), TypeError);
  for (const value of [
    { callbacks: null }, { parameterIndex: -1 }, { parameterIndex: 0.5 },
    { getInstance: null }, { handles: null }, { handles: 1 }, { handles: {} },
    { handles: { acquire() {} } },
    { exceptionReporter: null }, { exceptionReporter: 1 }, { exceptionReporter: {} },
    { staging: null }, { staging: 1 }, { staging: {} }, { staging: { stage() {} } },
  ]) assert.throws(() => createManagedInteropCallback({ ...valid, ...value }), TypeError);
  for (const value of [null, [], {}, {
    getInstance: () => ({}), handle: 1, extra: true,
  }]) assert.throws(() => releaseManagedInteropCallback(value), TypeError);
});

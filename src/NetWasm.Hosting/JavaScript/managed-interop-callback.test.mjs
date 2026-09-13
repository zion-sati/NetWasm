import assert from "node:assert/strict";
import test from "node:test";

import {
  createManagedInteropCallback,
  releaseManagedInteropCallback,
} from "./managed-interop-callback.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { NetWasmHostError, NetWasmManagedError } from "./managed-errors.mjs";

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

function request(overrides = {}) {
  const handles = createInteropHandleTable();
  return {
    callbacks: [callback()],
    descriptor,
    exceptionReporter: reporter(),
    getInstance: () => ({ exports: { callback_1: (_handle, value) => value + 1 } }),
    handle: 7,
    handles,
    parameterIndex: 0,
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

test("owns transient string and byte callback handles for one invocation", () => {
  const handles = createInteropHandleTable();
  const sourceBytes = new Uint8Array([1, 2, 3]);
  const invoke = createManagedInteropCallback(request({
    callbacks: [callback({ parameters: ["string", "bytes", "string", "bytes"] })],
    handles,
    getInstance: () => ({ exports: {
      callback_1(handle, stringHandle, bytesHandle, nullString, nullBytes) {
        assert.equal(handle, 7);
        assert.equal(handles.get(stringHandle), "value");
        assert.notStrictEqual(handles.get(bytesHandle), sourceBytes);
        assert.deepEqual([...handles.get(bytesHandle)], [1, 2, 3]);
        assert.deepEqual([nullString, nullBytes], [0, 0]);
        return 5;
      },
    } }),
  }));
  assert.equal(invoke("value", sourceBytes, null, null), 5);
  assert.equal(handles.count, 0);

  for (const [parameters, value, pattern] of [
    [["string"], 1, /string callback/],
    [["bytes"], [], /byte callback/],
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
  ]) assert.throws(() => createManagedInteropCallback({ ...valid, ...value }), TypeError);
  for (const value of [null, [], {}, {
    getInstance: () => ({}), handle: 1, extra: true,
  }]) assert.throws(() => releaseManagedInteropCallback(value), TypeError);
});

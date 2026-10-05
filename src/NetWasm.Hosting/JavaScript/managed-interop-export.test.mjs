import assert from "node:assert/strict";
import test from "node:test";

import {
  createManagedExports,
  createManagedInteropExport,
} from "./managed-interop-export.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { NetWasmHostError, NetWasmManagedError } from "./managed-errors.mjs";

const minimalModule = new Uint8Array([
  0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
  0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7f,
  0x03, 0x02, 0x01, 0x00,
  0x07, 0x07, 0x01, 0x03, 0x72, 0x75, 0x6e, 0x00, 0x00,
  0x0a, 0x06, 0x01, 0x04, 0x00, 0x41, 0x07, 0x0b,
]);

const reporter = (event = null) => ({ consumeTerminalEvent: () => event });
const descriptor = (overrides = {}) => ({
  name: "run", parameters: [], result: "i32", ...overrides,
});
const memory = new WebAssembly.Memory({ initial: 1 });
const targetLayout = {
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
};
const request = (overrides = {}) => ({
  descriptor: descriptor(),
  exceptionReporter: reporter(),
  getMemory: () => memory,
  handles: createInteropHandleTable(),
  instance: { exports: { run: () => 7 } },
  target: "wasm32",
  targetLayout,
  consumeExceptionPayload(handle) { assert.equal(handle, 0); return null; },
  ...overrides,
});

test("retains the low-level managed-export compatibility boundary", async () => {
  const { instance } = await WebAssembly.instantiate(minimalModule);
  const exports = createManagedExports(instance, ["run"]);
  assert.equal(Object.isFrozen(exports), true);
  assert.equal(exports.run(), 7);
  assert.throws(() => createManagedExports({}, ["run"]), /WebAssembly.Instance/);
  assert.throws(() => createManagedExports(instance, null), /array/);
  assert.throws(() => createManagedExports(instance, ["missing"]), /not a function/);
});

test("adapts synchronous scalar and void managed exports", () => {
  const scalar = createManagedInteropExport(request({
    descriptor: descriptor({ parameters: ["bool", "char"], result: "u32" }),
    instance: { exports: { run: (boolean, character) => boolean + character } },
  }));
  assert.equal(Object.isFrozen(scalar), true);
  assert.equal(scalar(true, "A"), 66);
  assert.throws(() => scalar(true), /argument count/);

  const voidExport = createManagedInteropExport(request({
    descriptor: descriptor({ result: "void" }),
    instance: { exports: { run: () => 99 } },
  }));
  assert.equal(voidExport(), undefined);
});

test("copies synchronous strings after memory growth and releases argument handles", () => {
  const exportMemory = new WebAssembly.Memory({ initial: 1 });
  const handles = createInteropHandleTable();
  const result = "copied:\ud800Ω";
  const reference = 65_536;
  const invoke = createManagedInteropExport(request({
    descriptor: descriptor({ parameters: ["string"], result: "string" }),
    getMemory: () => exportMemory,
    handles,
    instance: { exports: {
      run(handle) {
        assert.equal(handles.get(handle), "input\0\udc00");
        exportMemory.grow(1);
        const view = new DataView(exportMemory.buffer);
        view.setInt32(reference + targetLayout.stringLengthOffset, result.length, true);
        for (let index = 0; index < result.length; index++) {
          view.setUint16(
            reference + targetLayout.stringDataOffset + index * 2,
            result.charCodeAt(index),
            true);
        }
        return reference;
      },
    } },
  }));
  assert.equal(invoke("input\0\udc00"), result);
  assert.equal(handles.count, 0);

  const failing = createManagedInteropExport(request({
    descriptor: descriptor({ parameters: ["string"] }),
    handles,
    instance: { exports: {
      run() { throw new Error("failure"); },
      exception_get_active: () => 0,
    } },
  }));
  assert.throws(() => failing("temporary"), NetWasmManagedError);
  assert.equal(handles.count, 0);
  assert.throws(() => invoke(42), /string or null/);
});

test("copies synchronous byte arrays in both directions and releases argument handles", () => {
  const exportMemory = new WebAssembly.Memory({ initial: 1 });
  const handles = createInteropHandleTable();
  const reference = 256;
  const dataAddress = 512;
  const invoke = createManagedInteropExport(request({
    descriptor: descriptor({ parameters: ["bytes"], result: "bytes" }),
    getMemory: () => exportMemory,
    handles,
    instance: { exports: {
      run(handle) {
        assert.deepEqual(handles.get(handle), Uint8Array.of(1, 2, 255));
        const view = new DataView(exportMemory.buffer);
        view.setInt32(reference + targetLayout.arrayLengthOffset, 3, true);
        view.setUint32(reference + targetLayout.arrayDataPointerOffset, dataAddress, true);
        new Uint8Array(exportMemory.buffer, dataAddress, 3).set([9, 8, 7]);
        return reference;
      },
    } },
  }));

  const input = Uint8Array.of(1, 2, 255);
  const result = invoke(input);
  assert.deepEqual(result, Uint8Array.of(9, 8, 7));
  assert.notEqual(result.buffer, exportMemory.buffer);
  new Uint8Array(exportMemory.buffer, dataAddress, 3).fill(0);
  assert.deepEqual(result, Uint8Array.of(9, 8, 7));
  assert.equal(handles.count, 0);
  assert.throws(() => invoke([1, 2, 3]), /Uint8Array or null/);
});

test("passes null buffers without acquiring handles and surfaces release failures", () => {
  for (const type of ["string", "bytes"]) {
    const handles = createInteropHandleTable();
    const invoke = createManagedInteropExport(request({
      descriptor: descriptor({ parameters: [type] }), handles,
      instance: { exports: { run(handle) { assert.equal(handle, 0); return 42; } } },
    }));
    assert.equal(invoke(null), 42);
    assert.equal(handles.count, 0);
  }
  const failure = new Error("handle release failed");
  let released = 0;
  const invoke = createManagedInteropExport(request({
    descriptor: descriptor({ parameters: ["string"] }),
    handles: { acquire: () => 7, release(handle) {
      assert.equal(handle, 7); released++; throw failure;
    } },
  }));
  assert.throws(() => invoke("temporary"), error => error === failure);
  assert.equal(released, 1);
});

test("converts low-level WebAssembly exceptions and preserves unrelated failures", () => {
  const tag = new WebAssembly.Tag({ parameters: [] });
  const wasmException = new WebAssembly.Exception(tag, []);
  let cleared = 0;
  const managed = createManagedInteropExport(request({
    instance: { exports: {
      run() { throw wasmException; },
      exception_get_active: () => 1,
      exception_get_active_type_id: () => 42,
      exception_clear_active: () => { cleared++; },
    } },
  }));
  assert.throws(() => managed(), error => error instanceof NetWasmManagedError
    && error.managedType === 42);
  assert.equal(cleared, 1);

  for (const [cause, active] of [[new Error("ordinary"), 1], [wasmException, 0]]) {
    const direct = createManagedInteropExport(request({
      instance: { exports: {
        run() { throw cause; },
        exception_get_active: () => active,
      } },
    }));
    assert.throws(() => direct(), error => error instanceof NetWasmManagedError);
  }
});

test("maps runtime traps through terminal managed-exception events", () => {
  const trap = new WebAssembly.RuntimeError("trap");
  for (const event of [null, { typeId: 73 }]) {
    let active = 1;
    let finalizerFailure = true;
    let cleared = 0;
    const invoke = createManagedInteropExport(request({
      exceptionReporter: reporter(event),
      instance: { exports: {
        run() {
          if (finalizerFailure) throw trap;
          assert.equal(active, 0);
          return 42;
        },
        exception_get_active: () => active,
        exception_clear_active() {
          cleared++;
          active = 0;
          finalizerFailure = false;
        },
      } },
    }));
    assert.throws(() => invoke(), error => event === null
      ? error === trap
      : error instanceof NetWasmManagedError && error.managedType === 73
        && error.cause === trap && active === 0 && !finalizerFailure);
    assert.equal(cleared, event === null ? 0 : 1);
    if (event !== null) {
      assert.equal(invoke(), 42);
      assert.equal(cleared, 1);
    } else {
      assert.equal(active, 1);
      assert.equal(finalizerFailure, true);
    }
  }
});

test("preserves host/managed errors and wraps ordinary sync and async failures", async () => {
  for (const cause of [new NetWasmHostError("host"), new NetWasmManagedError("managed")]) {
    const invoke = createManagedInteropExport(request({
      instance: { exports: { run() { throw cause; }, exception_get_active: () => 0 } },
    }));
    assert.throws(() => invoke(), error => error === cause);
  }
  const ordinary = new Error("ordinary");
  const sync = createManagedInteropExport(request({
    instance: { exports: { run() { throw ordinary; }, exception_get_active: () => 0 } },
  }));
  assert.throws(() => sync(), error => error instanceof NetWasmManagedError
    && error.cause === ordinary);
  const async = createManagedInteropExport(request({
    descriptor: descriptor({ asyncReturn: "task", statusExport: "status", completeExport: "complete" }),
    instance: { exports: { run() { throw ordinary; }, exception_get_active: () => 0 } },
  }));
  await assert.rejects(() => async(), error => error instanceof NetWasmManagedError
    && error.cause === ordinary);
});

test("observes every managed async completion state and completes once", async () => {
  for (const state of ["success", "void", "cancelled", "failed", "trap", "ordinary"]) {
    let polls = 0;
    let completed = 0;
    const exports = {
      run: () => 5,
      status() {
        polls++;
        if (polls === 1) return 0;
        if (state === "trap") throw new WebAssembly.RuntimeError("trap");
        if (state === "ordinary") throw new Error("ordinary");
        return state === "success" || state === "void" ? 1 : state === "cancelled" ? 3 : 2;
      },
      result: () => 37,
      complete(handle) { assert.equal(handle, 5); completed++; return 0; },
    };
    const invoke = createManagedInteropExport(request({
      descriptor: descriptor({
        asyncReturn: "task", statusExport: "status", completeExport: "complete", completionResult: "exception-handle-v1",
        resultExport: "result", result: state === "void" ? "void" : "i32",
      }),
      instance: { exports },
    }));
    const promise = invoke();
    if (state === "success") assert.equal(await promise, 37);
    else if (state === "void") assert.equal(await promise, undefined);
    else if (state === "cancelled") {
      await assert.rejects(() => promise, error => error.name === "AbortError");
    } else if (state === "failed") {
      await assert.rejects(() => promise, NetWasmManagedError);
    } else if (state === "trap") {
      await assert.rejects(() => promise, WebAssembly.RuntimeError);
    } else {
      await assert.rejects(() => promise, NetWasmManagedError);
    }
    assert.equal(completed, 1);
  }
});

test("rejects malformed managed-export composition", () => {
  const valid = request();
  for (const value of [
    null, [], {}, { ...valid, extra: true }, { ...valid, [Symbol("invalid")]: true },
    Object.defineProperty({ ...valid }, "descriptor", {
      enumerable: true, get: () => valid.descriptor,
    }),
  ]) assert.throws(() => createManagedInteropExport(value), TypeError);
  for (const descriptorValue of [null, [], {}, { name: "run", parameters: null }]) {
    assert.throws(() => createManagedInteropExport({
      ...valid, descriptor: descriptorValue,
    }), /descriptor/);
  }
  for (const instance of [null, [], {}, { exports: null }]) {
    assert.throws(() => createManagedInteropExport({ ...valid, instance }), /instance/);
  }
  for (const exceptionReporter of [null, {}, { consumeTerminalEvent: null }]) {
    assert.throws(() => createManagedInteropExport({
      ...valid, exceptionReporter,
    }), /reporter/);
  }
  for (const key of ["assertAvailable", "observeAsyncExport"]) {
    assert.throws(() => createManagedInteropExport({ ...valid, [key]: null }), /lifetime actions/);
  }
  for (const services of [
    { getMemory: null }, { handles: null }, { handles: 1 },
    { handles: {} }, { handles: { acquire() {}, release: null } },
  ]) {
    assert.throws(() => createManagedInteropExport({ ...valid, ...services }), /memory services/);
  }
  assert.throws(() => createManagedInteropExport(request({
    descriptor: descriptor({ asyncReturn: "task" }), consumeExceptionPayload: null,
  })), /payload consumer/);
});

test("delegates async observation through the injected lifetime capability", async () => {
  let checked = 0;
  let completed = 0;
  const invoke = createManagedInteropExport(request({
    descriptor: descriptor({
      asyncReturn: "task", statusExport: "status", resultExport: "result", completeExport: "complete", completionResult: "exception-handle-v1",
    }),
    instance: { exports: {
      run: () => 5, status: handle => { assert.equal(handle, 5); return 1; },
      result: handle => { assert.equal(handle, 5); return 42; },
      complete: handle => { assert.equal(handle, 5); completed++; return 0; },
    } },
    assertAvailable() { checked++; },
    observeAsyncExport(operation) {
      assert.equal(operation.name, "run");
      assert.equal(operation.readStatus(), 1);
      const result = operation.readResult();
      operation.complete();
      return Promise.resolve(result);
    },
  }));
  assert.equal(await invoke(), 42);
  assert.equal(checked, 1);
  assert.equal(completed, 1);
});

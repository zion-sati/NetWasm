import assert from "node:assert/strict";
import test from "node:test";

import {
  createManagedExports,
  createManagedInteropExport,
} from "./managed-interop-export.mjs";
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
const request = (overrides = {}) => ({
  descriptor: descriptor(),
  exceptionReporter: reporter(),
  instance: { exports: { run: () => 7 } },
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
    const invoke = createManagedInteropExport(request({
      exceptionReporter: reporter(event),
      instance: { exports: {
        run() { throw trap; },
        exception_get_active: () => 0,
      } },
    }));
    assert.throws(() => invoke(), error => event === null
      ? error === trap
      : error instanceof NetWasmManagedError && error.managedType === 73);
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
      complete(handle) { assert.equal(handle, 5); completed++; },
    };
    const invoke = createManagedInteropExport(request({
      descriptor: descriptor({
        asyncReturn: "task", statusExport: "status", completeExport: "complete",
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
});

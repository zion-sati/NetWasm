import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { registerStackTraceSymbols } from "./stack-trace-symbol-registrar.mjs";

function createRuntime(memory, address, registered = [], freed = []) {
  return {
    native_alloc: () => address,
    native_free: value => freed.push(value),
    stack_trace_register_symbol(id, value, length) {
      registered.push({
        id,
        value,
        name: String.fromCharCode(...new Uint16Array(memory.buffer, Number(value), length)),
      });
    },
  };
}

test("registerStackTraceSymbols validates its symbols and required capabilities", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const adapter = { target: "wasm32" };

  assert.throws(
    () => registerStackTraceSymbols({}, memory, {}, adapter),
    error => error instanceof NetWasmHostError && /must be an array/i.test(error.message));
  assert.doesNotThrow(() => registerStackTraceSymbols(null, null, [], adapter));

  for (const runtime of [
    null,
    {},
    { native_alloc() {} },
    { native_alloc() {}, native_free() {} },
  ]) {
    assert.throws(
      () => registerStackTraceSymbols(runtime, memory, [{ id: 1, name: "A" }], adapter),
      error => error instanceof NetWasmHostError && /does not support/i.test(error.message));
  }

  const runtime = createRuntime(memory, 32);
  assert.throws(
    () => registerStackTraceSymbols(runtime, {}, [{ id: 1, name: "A" }], adapter),
    error => error instanceof NetWasmHostError && /requires runtime memory/i.test(error.message));
});

for (const [target, address, expectedSize] of [
  ["wasm32", 64, 6],
  ["wasm64", 64n, 6n],
]) {
  test(`registerStackTraceSymbols writes UTF-16 for ${target}`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const registered = [];
    const freed = [];
    const runtime = createRuntime(memory, address, registered, freed);
    runtime.native_alloc = size => {
      assert.equal(size, expectedSize);
      return address;
    };

    registerStackTraceSymbols(
      runtime,
      memory,
      [{ id: 7, name: "A😀" }],
      { target });

    assert.deepEqual(registered, [{ id: 7, value: address, name: "A😀" }]);
    assert.deepEqual(freed, [address]);
  });
}

test("registerStackTraceSymbols rejects zero and invalid allocation addresses", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const symbol = [{ id: 1, name: "A" }];

  for (const address of [0, 0n]) {
    const freed = [];
    assert.throws(
      () => registerStackTraceSymbols(
        createRuntime(memory, address, [], freed), memory, symbol, { target: "wasm32" }),
      error => error instanceof NetWasmHostError && /failed to allocate/i.test(error.message));
    assert.deepEqual(freed, []);
  }

  for (const address of [NaN, -1, memory.buffer.byteLength, 9007199254740992n]) {
    const freed = [];
    assert.throws(
      () => registerStackTraceSymbols(
        createRuntime(memory, address, [], freed), memory, symbol, { target: "wasm32" }),
      error => error instanceof NetWasmHostError && /invalid memory address/i.test(error.message));
    assert.deepEqual(freed, [address]);
  }
});

test("registerStackTraceSymbols frees temporary storage when registration fails", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const freed = [];
  const runtime = createRuntime(memory, 32, [], freed);
  runtime.stack_trace_register_symbol = () => {
    throw new Error("registration failed");
  };

  assert.throws(
    () => registerStackTraceSymbols(
      runtime, memory, [{ id: 1, name: "A" }], { target: "wasm32" }),
    /registration failed/);
  assert.deepEqual(freed, [32]);
});

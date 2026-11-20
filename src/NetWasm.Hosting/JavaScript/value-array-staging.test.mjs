// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { createValueArrayStaging } from "./value-array-staging.mjs";

// The runtime's allocator over a memory: blocks from a bump pointer, at the target's address width.
function runtimeOver(memory, target, { start = 64, grow = false } = {}) {
  const wide = target === "wasm64";
  let next = start;
  const calls = [];
  return {
    calls,
    exports: {
      native_alloc(size) {
        assert.equal(typeof size, wide ? "bigint" : "number");
        calls.push(["alloc", size]);
        // An allocation that grows the memory detaches the buffer a caller held before it.
        if (grow) memory.grow(1);
        const address = next;
        next += Number(size);
        return wide ? BigInt(address) : address;
      },
      native_free(address) { calls.push(["free", address]); },
    },
  };
}

function staging(target, options = {}) {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const runtime = runtimeOver(memory, target, options);
  const stager = createValueArrayStaging({
    getInstance: () => ({ exports: runtime.exports }),
    getMemory: () => memory,
    runtimeModules: options.separateRuntime ? { "netwasm.runtime.v1": runtime.exports } : {},
    target,
  });
  return { memory, runtime, stager };
}

test("stages a number array in a block of linear memory at both widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const { memory, runtime, stager } = staging(target, { grow: true });
    const [address, length] = stager.stage("f64[]", Float64Array.of(1.5, -2));
    assert.equal(length, 2);
    assert.deepEqual([...new Float64Array(memory.buffer.slice(Number(address), Number(address) + 16))], [1.5, -2]);
    assert.deepEqual(runtime.calls, [["alloc", target === "wasm64" ? 16n : 16]]);
    // A plain array becomes the parameter type's typed array first.
    const [bytesAddress, bytesLength] = stager.stage("bytes", [7, 8, 255]);
    assert.deepEqual([...new Uint8Array(memory.buffer, Number(bytesAddress), bytesLength)], [7, 8, 255]);
  }
});

test("stages null as length -1 and an empty array as length 0, neither with a block", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const { runtime, stager } = staging(target);
    const none = target === "wasm64" ? 0n : 0;
    assert.deepEqual(stager.stage("bytes", null), [none, -1]);
    assert.deepEqual(stager.stage("i32[]", undefined), [none, -1]);
    assert.deepEqual(stager.stage("i32[]", Int32Array.of()), [none, 0]);
    assert.deepEqual(runtime.calls, []);
  }
});

test("discards a block that never reached managed code, and no block at all", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const { runtime, stager } = staging(target, { separateRuntime: true });
    const [address] = stager.stage("u16[]", Uint16Array.of(1));
    stager.discard(address);
    stager.discard(target === "wasm64" ? 0n : 0);
    assert.deepEqual(runtime.calls.filter(call => call[0] === "free"), [["free", address]]);
  }
});

test("refuses values of the wrong type and a runtime that cannot allocate", () => {
  const { stager } = staging("wasm32");
  assert.throws(() => stager.stage("bytes", "text"), error =>
    error instanceof NetWasmHostError && /Uint8Array, an array, or null/.test(error.message));
  assert.throws(() => stager.stage("i32[]", Uint32Array.of(1)), /Int32Array, an array, or null/);

  const memory = new WebAssembly.Memory({ initial: 1 });
  const failing = createValueArrayStaging({
    getInstance: () => ({ exports: { native_alloc: () => 0 } }), getMemory: () => memory,
    runtimeModules: {}, target: "wasm32",
  });
  assert.throws(() => failing.stage("bytes", [1]), /could not allocate/);
  const missing = createValueArrayStaging({
    getInstance: () => ({ exports: {} }), getMemory: () => memory, runtimeModules: {}, target: "wasm32",
  });
  assert.throws(() => missing.stage("bytes", [1]), /does not export native_alloc/);
});

test("rejects malformed staging options", () => {
  const valid = { getInstance: () => ({}), getMemory: () => null, runtimeModules: {}, target: "wasm32" };
  for (const value of [null, [], "options", {}, { ...valid, extra: true }]) {
    assert.throws(() => createValueArrayStaging(value), TypeError);
  }
  for (const value of [{ getInstance: null }, { getMemory: 1 }, { target: "component" }]) {
    assert.throws(() => createValueArrayStaging({ ...valid, ...value }), TypeError);
  }
});

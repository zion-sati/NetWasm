import assert from "node:assert/strict";
import test from "node:test";

import {
  projectManagedInteropMemoryOffset,
  readManagedInteropBytes,
  readManagedInteropString,
  writeManagedInteropBytes,
  writeManagedInteropString,
} from "./managed-interop-memory-codec.mjs";

const layouts = Object.freeze({
  wasm32: Object.freeze({
    stringLengthOffset: 4, stringDataOffset: 8,
    arrayLengthOffset: 4, arrayDataPointerOffset: 8,
  }),
  wasm64: Object.freeze({
    stringLengthOffset: 8, stringDataOffset: 12,
    arrayLengthOffset: 8, arrayDataPointerOffset: 16,
  }),
});

test("reads and writes managed UTF-16 strings at both target widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const reference = target === "wasm32" ? 16 : 16n;
    const layout = layouts[target];
    const value = `${"x".repeat(8192)}Z`;
    new DataView(memory.buffer).setInt32(
      16 + layout.stringLengthOffset, value.length, true);
    writeManagedInteropString({ memory, reference, target, targetLayout: layout, value });
    assert.equal(readManagedInteropString({ memory, reference, target, targetLayout: layout }), value);
    const nullReference = target === "wasm32" ? 0 : 0n;
    assert.equal(readManagedInteropString({
      memory: null, reference: nullReference, target, targetLayout: layout,
    }), null);
  }
});

test("reads and writes managed byte arrays at both target widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const reference = target === "wasm32" ? 16 : 16n;
    const layout = layouts[target];
    const view = new DataView(memory.buffer);
    view.setInt32(16 + layout.arrayLengthOffset, 4, true);
    if (target === "wasm32") view.setUint32(16 + layout.arrayDataPointerOffset, 128, true);
    else view.setBigUint64(16 + layout.arrayDataPointerOffset, 128n, true);
    writeManagedInteropBytes({
      memory, reference, target, targetLayout: layout, value: new Uint8Array([1, 2, 3, 4]),
    });
    const result = readManagedInteropBytes({ memory, reference, target, targetLayout: layout });
    assert.deepEqual([...result], [1, 2, 3, 4]);
    result[0] = 9;
    assert.equal(new Uint8Array(memory.buffer)[128], 1);
    const nullReference = target === "wasm32" ? 0 : 0n;
    assert.equal(readManagedInteropBytes({
      memory: null, reference: nullReference, target, targetLayout: layout,
    }), null);
  }
});

test("rejects invalid values and memory ranges without partial writes", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const layout = layouts.wasm32;
  const view = new DataView(memory.buffer);
  view.setInt32(20, 2, true);
  assert.throws(() => writeManagedInteropString({
    memory, reference: 16, target: "wasm32", targetLayout: layout, value: "x",
  }), /destination/);
  assert.throws(() => writeManagedInteropString({
    memory, reference: 16, target: "wasm32", targetLayout: layout, value: null,
  }), TypeError);
  view.setInt32(20, -1, true);
  assert.throws(() => readManagedInteropString({
    memory, reference: 16, target: "wasm32", targetLayout: layout,
  }), /outside/);

  view.setInt32(20, 3, true);
  view.setUint32(24, 65_535, true);
  assert.throws(() => readManagedInteropBytes({
    memory, reference: 16, target: "wasm32", targetLayout: layout,
  }), /outside/);
  view.setInt32(20, -1, true);
  view.setUint32(24, 128, true);
  assert.throws(() => readManagedInteropBytes({
    memory, reference: 16, target: "wasm32", targetLayout: layout,
  }), /outside/);
  view.setInt32(20, 2, true);
  assert.throws(() => writeManagedInteropBytes({
    memory, reference: 16, target: "wasm32", targetLayout: layout,
    value: new Uint8Array([1]),
  }), /destination/);
  assert.throws(() => writeManagedInteropBytes({
    memory, reference: 16, target: "wasm32", targetLayout: layout, value: [],
  }), TypeError);
  assert.throws(() => readManagedInteropString({
    memory: {}, reference: 16, target: "wasm32", targetLayout: layout,
  }), /memory/);
  assert.throws(() => readManagedInteropString({
    memory, reference: 16, target: "component", targetLayout: layout,
  }), /target/);
  assert.throws(() => readManagedInteropString({
    memory, reference: Number.MAX_SAFE_INTEGER, target: "wasm32", targetLayout: layout,
  }), /descriptor/);
  assert.throws(() => readManagedInteropString({
    memory, reference: 65_535, target: "wasm32", targetLayout: layout,
  }), /outside/);
  assert.equal(projectManagedInteropMemoryOffset({
    address: 32n, memory, target: "wasm64",
  }), 32);
});

test("rejects malformed managed-memory requests", () => {
  const read = {
    memory: new WebAssembly.Memory({ initial: 1 }),
    reference: 0,
    target: "wasm32",
    targetLayout: layouts.wasm32,
  };
  for (const action of [readManagedInteropString, readManagedInteropBytes]) {
    for (const value of [
      null, [], {}, { ...read, extra: true }, { ...read, [Symbol("invalid")]: true },
      Object.defineProperty({ ...read }, "reference", { enumerable: true, get: () => 0 }),
    ]) assert.throws(() => action(value), TypeError);
  }
  const write = { ...read, value: "" };
  for (const action of [writeManagedInteropString, writeManagedInteropBytes]) {
    for (const value of [
      null, [], {}, { ...write, extra: true }, { ...write, [Symbol("invalid")]: true },
      Object.defineProperty({ ...write }, "value", { enumerable: true, get: () => "" }),
    ]) assert.throws(() => action(value), TypeError);
  }
  for (const value of [
    null, [], {}, { address: 0, memory: read.memory, target: "wasm32", extra: true },
  ]) assert.throws(() => projectManagedInteropMemoryOffset(value), TypeError);
});

// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import {
  projectManagedInteropMemoryOffset,
  readManagedInteropString,
  readMemoryView,
  readValueArray,
  writeManagedInteropString,
} from "./managed-interop-memory-codec.mjs";

const layouts = Object.freeze({
  wasm32: Object.freeze({
    stringLengthOffset: 4, stringDataOffset: 8,
    arrayLengthOffset: 4, arrayDataPointerOffset: 8,
    spanReferenceOffset: 0, spanLengthOffset: 4,
  }),
  wasm64: Object.freeze({
    stringLengthOffset: 8, stringDataOffset: 12,
    arrayLengthOffset: 8, arrayDataPointerOffset: 16,
    spanReferenceOffset: 0, spanLengthOffset: 8,
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

// A managed array going out to JavaScript is copied, so the guest's array and the typed array stay independent.
// (A number array coming into managed code is staged; see value-array-staging.test.mjs.)
test("copies every managed number array out as its typed array at both widths", () => {
  const cases = [
    [Uint8Array, [1, 255]], [Int8Array, [-1, 2]], [Int16Array, [-300, 4]], [Uint16Array, [65_535, 1]],
    [Int32Array, [-70_000, 9]], [Uint32Array, [4_000_000_000, 3]],
    [BigInt64Array, [-(2n ** 40n), 5n]], [BigUint64Array, [2n ** 63n, 1n]],
    [Float32Array, [1.5, -2.25]], [Float64Array, [Math.PI, -0.5]],
  ];
  for (const target of ["wasm32", "wasm64"]) {
    for (const [constructor, values] of cases) {
      const memory = new WebAssembly.Memory({ initial: 1 });
      const reference = target === "wasm32" ? 16 : 16n;
      const layout = layouts[target];
      const view = new DataView(memory.buffer);
      view.setInt32(16 + layout.arrayLengthOffset, values.length, true);
      // An odd data address proves the copy does not depend on element alignment.
      if (target === "wasm32") view.setUint32(16 + layout.arrayDataPointerOffset, 129, true);
      else view.setBigUint64(16 + layout.arrayDataPointerOffset, 129n, true);
      const elements = new constructor(values);
      new Uint8Array(memory.buffer, 129, elements.byteLength).set(new Uint8Array(elements.buffer));
      const result = readValueArray(reference, target, layout, memory, constructor);
      assert.ok(result instanceof constructor);
      assert.deepEqual([...result], values, `${target} ${constructor.name}`);
      result[0] = values[1];
      assert.deepEqual([...readValueArray(reference, target, layout, memory, constructor)], values);
      assert.equal(readValueArray(target === "wasm32" ? 0 : 0n, target, layout, null, constructor), null);
    }
  }
});

// Writes a span at `spanAddress` whose elements start at `dataAddress`.
function writeSpan(memory, target, spanAddress, dataAddress, length) {
  const layout = layouts[target];
  const view = new DataView(memory.buffer);
  if (target === "wasm32") view.setUint32(spanAddress + layout.spanReferenceOffset, dataAddress, true);
  else view.setBigUint64(spanAddress + layout.spanReferenceOffset, BigInt(dataAddress), true);
  view.setInt32(spanAddress + layout.spanLengthOffset, length, true);
  return target === "wasm32" ? spanAddress : BigInt(spanAddress);
}

test("views every span element type over linear memory at both widths", () => {
  const cases = [
    [Uint8Array, [255, 1]], [Int8Array, [-1, 2]], [Int16Array, [-300, 4]],
    [Uint16Array, [65_535, 1]], [Int32Array, [-70_000, 9]], [Uint32Array, [4_000_000_000, 3]],
    [BigInt64Array, [-(2n ** 40n), 5n]], [BigUint64Array, [2n ** 63n, 1n]],
    [Float32Array, [1.5, -2.25]], [Float64Array, [Math.PI, -0.5]],
  ];
  for (const target of ["wasm32", "wasm64"]) {
    for (const [constructor, values] of cases) {
      const memory = new WebAssembly.Memory({ initial: 1 });
      const reference = writeSpan(memory, target, 16, 128, values.length);
      new constructor(memory.buffer, 128, values.length).set(values);
      const result = readMemoryView(reference, target, layouts[target], memory, constructor);
      assert.ok(result instanceof constructor);
      assert.equal(result.buffer, memory.buffer, "a view, not a copy");
      assert.equal(result.byteOffset, 128);
      assert.deepEqual([...result], values, `${target} ${constructor.name}`);
      result[0] = values[1];
      assert.equal(new constructor(memory.buffer, 128, 1)[0], values[1], "writes reach the guest");
    }
  }
});

test("views an empty span at any address", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  for (const dataAddress of [0, 129]) {
    const result = readMemoryView(
      writeSpan(memory, "wasm32", 16, dataAddress, 0), "wasm32", layouts.wasm32, memory, Int32Array);
    assert.equal(result.length, 0);
    assert.equal(result.buffer, memory.buffer);
  }
});

test("rejects invalid span types, ranges and alignment", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const read = (reference, constructor = Int32Array, target = "wasm32") =>
    readMemoryView(reference, target, layouts[target], memory, constructor);
  assert.throws(() => read(writeSpan(memory, "wasm32", 16, 128, 1), Array), /memory-view type/);
  assert.throws(() => read(writeSpan(memory, "wasm32", 16, 128, -1)), /outside linear memory/);
  assert.throws(() => read(writeSpan(memory, "wasm32", 16, 65_532, 2)), /outside linear memory/);
  assert.throws(() => read(writeSpan(memory, "wasm64", 16, 2 ** 53, 1), Int32Array, "wasm64"),
    /outside linear memory/);
  assert.throws(() => read(writeSpan(memory, "wasm32", 16, 130, 1)), /not aligned/);
  assert.deepEqual([...read(writeSpan(memory, "wasm32", 16, 131, 1), Uint8Array)], [0]);
});

test("rejects invalid value-array types and ranges", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const layout = layouts.wasm32;
  const view = new DataView(memory.buffer);
  view.setInt32(20, 2, true);
  view.setUint32(24, 128, true);
  assert.throws(() => readValueArray(16, "wasm32", layout, memory, Array), /value-array type/);
  view.setInt32(20, 16_384, true);
  assert.throws(() => readValueArray(16, "wasm32", layout, memory, Float64Array), /outside linear memory/);
  view.setInt32(20, -1, true);
  assert.throws(() => readValueArray(16, "wasm32", layout, memory, Uint8Array), /outside/);
  view.setInt32(20, 3, true);
  view.setUint32(24, 65_535, true);
  assert.throws(() => readValueArray(16, "wasm32", layout, memory, Uint8Array), /outside/);
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
  for (const action of [readManagedInteropString]) {
    for (const value of [
      null, [], {}, { ...read, extra: true }, { ...read, [Symbol("invalid")]: true },
      Object.defineProperty({ ...read }, "reference", { enumerable: true, get: () => 0 }),
    ]) assert.throws(() => action(value), TypeError);
  }
  const write = { ...read, value: "" };
  for (const action of [writeManagedInteropString]) {
    for (const value of [
      null, [], {}, { ...write, extra: true }, { ...write, [Symbol("invalid")]: true },
      Object.defineProperty({ ...write }, "value", { enumerable: true, get: () => "" }),
    ]) assert.throws(() => action(value), TypeError);
  }
  for (const value of [
    null, [], {}, { address: 0, memory: read.memory, target: "wasm32", extra: true },
  ]) assert.throws(() => projectManagedInteropMemoryOffset(value), TypeError);
});

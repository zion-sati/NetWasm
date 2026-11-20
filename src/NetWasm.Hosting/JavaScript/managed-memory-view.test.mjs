// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { ManagedMemoryView } from "./managed-memory-view.mjs";

// As .NET's IMemoryView: it reads and writes the segment's elements in place, through memory that may have grown.
test("reads and writes a segment through linear memory as it is at each access", () => {
  const memory = new WebAssembly.Memory({ initial: 1, maximum: 2 });
  new Int32Array(memory.buffer, 64, 4).set([1, 2, 3, 4]);
  let released = 0;
  const view = new ManagedMemoryView(() => memory, 68n, 2, 4, () => { released++; });

  assert.equal(view.length, 2);
  assert.equal(view.byteLength, 8);
  assert.deepEqual([...view.slice()], [2, 3]);
  memory.grow(1);
  view.set(Int32Array.of(9), 1);
  assert.deepEqual([...new Int32Array(memory.buffer, 64, 4)], [1, 2, 9, 4]);
  const target = new Int32Array(2);
  view.copyTo(target, 1);
  assert.deepEqual([...target], [9, 0]);
  assert.throws(() => view.set(Uint8Array.of(1)), /takes a Int32Array/);
  assert.throws(() => view.copyTo(new Float64Array(2)), /takes a Int32Array/);

  assert.equal(view.isDisposed, false);
  view.dispose();
  view.dispose();
  assert.equal(released, 1);
  assert.equal(view.isDisposed, true);
  assert.throws(() => view.length, /disposed/);
  assert.throws(() => view.slice(), /disposed/);
});

test("views every element type CoreLib numbers and rejects what it does not", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const constructors = [Uint8Array, Int8Array, Int16Array, Uint16Array, Int32Array, Uint32Array, BigInt64Array,
    BigUint64Array, Float32Array, Float64Array];
  constructors.forEach((constructor, kind) => {
    assert.ok(new ManagedMemoryView(() => memory, 64, 1, kind, () => {}).slice() instanceof constructor);
  });
  for (const [readMemory, length, kind, release] of [
    [null, 1, 0, () => {}], [() => memory, -1, 0, () => {}], [() => memory, 1.5, 0, () => {}],
    [() => memory, 1, 10, () => {}], [() => memory, 1, "length", () => {}], [() => memory, 1, 0, null],
  ]) {
    assert.throws(() => new ManagedMemoryView(readMemory, 0, length, kind, release), /invalid/);
  }
});

import assert from "node:assert/strict";
import test from "node:test";
import {
  projectRawCanonicalMemoryRange,
} from "./raw-canonical-memory-range-projector.mjs";

const memory = () => new WebAssembly.Memory({ initial: 1, maximum: 2 });
const request = overrides => ({
  address: 16,
  alignment: 8,
  byteLength: 24,
  memory: memory(),
  target: "wasm32",
  ...overrides,
});

test("projects immutable aligned ranges at both canonical address widths", () => {
  const wasm32Memory = memory();
  const wasm32 = projectRawCanonicalMemoryRange(request({ memory: wasm32Memory }));
  assert.equal(wasm32.buffer, wasm32Memory.buffer);
  assert.equal(wasm32.index, 16);
  assert.equal(wasm32.byteLength, 24);
  assert.equal(Object.isFrozen(wasm32), true);

  const wasm64Memory = memory();
  const wasm64 = projectRawCanonicalMemoryRange(request({
    address: 24n,
    memory: wasm64Memory,
    target: "wasm64",
  }));
  assert.equal(wasm64.buffer, wasm64Memory.buffer);
  assert.equal(wasm64.index, 24);
  assert.equal(wasm64.byteLength, 24);

  assert.equal(projectRawCanonicalMemoryRange(request({ address: -0 })).index, 0);
  assert.equal(projectRawCanonicalMemoryRange(request({
    address: 0n,
    target: "wasm64",
  })).index, 0);
});

test("reacquires the current memory buffer after growth", () => {
  const subjectMemory = memory();
  const first = projectRawCanonicalMemoryRange(request({
    address: 65_528,
    byteLength: 8,
    memory: subjectMemory,
  }));
  assert.throws(() => projectRawCanonicalMemoryRange(request({
    address: 65_536,
    byteLength: 8,
    memory: subjectMemory,
  })), /out of bounds/);

  subjectMemory.grow(1);
  const second = projectRawCanonicalMemoryRange(request({
    address: 65_536,
    byteLength: 8,
    memory: subjectMemory,
  }));
  assert.notEqual(second.buffer, first.buffer);
  assert.equal(second.buffer, subjectMemory.buffer);
  assert.equal(second.index, 65_536);
});

test("rejects malformed projection requests without observing accessors", () => {
  for (const value of [null, [], {}, { ...request({}), extra: true },
    { address: 0, alignment: 1, byteLength: 0, memory: memory(), wrong: "wasm32" },
    { ...request({}), [Symbol("invalid")]: true },
    Object.defineProperty(request({}), "target", { get: () => "wasm32", enumerable: true })]) {
    assert.throws(() => projectRawCanonicalMemoryRange(value), /projection/);
  }
});

test("rejects invalid targets, memory, lengths and alignments", () => {
  assert.throws(() => projectRawCanonicalMemoryRange(request({ target: "wasm128" })), /target/);
  for (const invalidMemory of [null, {}, new ArrayBuffer(8)]) {
    assert.throws(() => projectRawCanonicalMemoryRange(request({ memory: invalidMemory })), /memory is invalid/);
  }
  for (const byteLength of [-1, 0.5, Number.NaN, Number.MAX_SAFE_INTEGER + 1]) {
    assert.throws(() => projectRawCanonicalMemoryRange(request({ byteLength })), /byte length/);
  }
  for (const alignment of [0, -1, 1.5, 3, Number.NaN, Number.MAX_SAFE_INTEGER + 1]) {
    assert.throws(() => projectRawCanonicalMemoryRange(request({ alignment })), /alignment/);
  }
});

test("rejects invalid target-width addresses", () => {
  for (const address of [0n, 0.5, Number.NaN, -0x8000_0001, 0x1_0000_0000]) {
    assert.throws(() => projectRawCanonicalMemoryRange(request({ address })), /wasm32 address/);
  }
  for (const address of [0, -0x8000_0000_0000_0001n, 0x1_0000_0000_0000_0000n]) {
    assert.throws(() => projectRawCanonicalMemoryRange(request({
      address,
      target: "wasm64",
    })), /wasm64 address/);
  }
});

test("rejects unaligned and out-of-bounds canonical ranges", () => {
  assert.throws(() => projectRawCanonicalMemoryRange(request({ address: 17 })), /unaligned/);
  assert.throws(() => projectRawCanonicalMemoryRange(request({
    address: 17n,
    target: "wasm64",
  })), /unaligned/);
  assert.throws(() => projectRawCanonicalMemoryRange(request({ address: 65_536 })), /out of bounds/);
  assert.throws(() => projectRawCanonicalMemoryRange(request({
    address: 65_536,
    byteLength: 8,
  })), /out of bounds/);
  assert.throws(() => projectRawCanonicalMemoryRange(request({
    address: -1,
    alignment: 1,
  })), /out of bounds/);
  assert.throws(() => projectRawCanonicalMemoryRange(request({
    address: -1n,
    alignment: 1,
    target: "wasm64",
  })), /out of bounds/);
});

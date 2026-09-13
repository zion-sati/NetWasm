import assert from "node:assert/strict";
import test from "node:test";
import {
  createRawCanonicalMemoryAllocator,
  createRawCanonicalMemoryDeallocator,
} from "./raw-canonical-memory-allocator.mjs";

const memory = () => new WebAssembly.Memory({ initial: 1 });

test("creates immutable one-action allocation Adapters", () => {
  const reallocate = () => 16;
  const allocator = createRawCanonicalMemoryAllocator({ reallocate });
  const deallocator = createRawCanonicalMemoryDeallocator({ reallocate });

  assert.equal(typeof allocator, "function");
  assert.equal(typeof deallocator, "function");
  assert.equal(Object.isFrozen(allocator), true);
  assert.equal(Object.isFrozen(deallocator), true);
});

test("allocates with the exact target-width canonical reallocator contract", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const calls = [];
    const address = target === "wasm64" ? 32n : 32;
    const allocator = createRawCanonicalMemoryAllocator({
      reallocate(...values) {
        calls.push(values);
        return address;
      },
    });
    const actual = allocator({ alignment: 8, byteLength: 24, memory: memory(), target });
    const expected = target === "wasm64" ? [0n, 0n, 8n, 24n] : [0, 0, 8, 24];

    assert.equal(actual, address);
    assert.deepEqual(calls, [expected]);
  }
});

test("validates allocation against memory reacquired after growth", () => {
  const wasmMemory = memory();
  const allocator = createRawCanonicalMemoryAllocator({
    reallocate() {
      wasmMemory.grow(1);
      return 65_536;
    },
  });

  assert.equal(allocator({
    alignment: 16,
    byteLength: 32,
    memory: wasmMemory,
    target: "wasm32",
  }), 65_536);
  assert.equal(wasmMemory.buffer.byteLength, 131_072);
});

test("returns the target zero without calling reallocation for empty storage", () => {
  let calls = 0;
  const allocator = createRawCanonicalMemoryAllocator({
    reallocate() {
      calls++;
      throw new Error("must not run");
    },
  });

  assert.equal(allocator({ alignment: 1, byteLength: 0, memory: memory(), target: "wasm32" }), 0);
  assert.equal(allocator({ alignment: 1, byteLength: 0, memory: memory(), target: "wasm64" }), 0n);
  assert.equal(calls, 0);
});

test("deallocates with the original target-width allocation contract", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const calls = [];
    const deallocator = createRawCanonicalMemoryDeallocator({
      reallocate(...values) {
        calls.push(values);
        return target === "wasm64" ? 0n : 0;
      },
    });
    const address = target === "wasm64" ? 64n : 64;
    deallocator({ address, alignment: 8, byteLength: 24, memory: memory(), target });
    const expected = target === "wasm64" ? [64n, 24n, 8n, 0n] : [64, 24, 8, 0];

    assert.deepEqual(calls, [expected]);
  }
});

test("does not invoke reallocation for empty deallocation", () => {
  let calls = 0;
  const deallocator = createRawCanonicalMemoryDeallocator({
    reallocate() { calls++; },
  });
  deallocator({ address: 0, alignment: 1, byteLength: 0, memory: memory(), target: "wasm32" });
  assert.equal(calls, 0);
});

test("rejects invalid factories and malformed requests before reallocation", () => {
  for (const factory of [null, [], {}, { reallocate: 1 },
    { reallocate() {}, extra: true },
    { reallocate() {}, [Symbol("bad")]: true },
    Object.defineProperty({}, "reallocate", { enumerable: true, get() { throw new Error("no"); } })]) {
    assert.throws(() => createRawCanonicalMemoryAllocator(factory), TypeError);
    assert.throws(() => createRawCanonicalMemoryDeallocator(factory), TypeError);
  }

  let calls = 0;
  const reallocate = () => { calls++; return 0; };
  const allocator = createRawCanonicalMemoryAllocator({ reallocate });
  const deallocator = createRawCanonicalMemoryDeallocator({ reallocate });
  const valid = { alignment: 1, byteLength: 1, memory: memory(), target: "wasm32" };
  for (const request of [null, [], {}, { ...valid, extra: true },
    { ...valid, [Symbol("bad")]: true },
    Object.defineProperty({ ...valid }, "target", { enumerable: true, get() { throw new Error("no"); } })]) {
    assert.throws(() => allocator(request), TypeError);
  }
  for (const request of [null, [], {}, { address: 0, ...valid, extra: true }]) {
    assert.throws(() => deallocator(request), TypeError);
  }
  assert.equal(calls, 0);
});

test("rejects invalid sizes, alignments, targets and memory before reallocation", () => {
  let calls = 0;
  const allocator = createRawCanonicalMemoryAllocator({
    reallocate() { calls++; return 0; },
  });
  const valid = { alignment: 1, byteLength: 1, memory: memory(), target: "wasm32" };
  for (const byteLength of [-1, 0.5, Number.MAX_SAFE_INTEGER + 1, 0x1_0000_0000]) {
    assert.throws(() => allocator({ ...valid, byteLength }), /byte length/);
  }
  for (const alignment of [-1, 0, 3, 0x1_0000_0000]) {
    assert.throws(() => allocator({ ...valid, alignment }), /alignment/);
  }
  assert.throws(() => allocator({ ...valid, target: "wasm128" }), /target/);
  assert.throws(() => allocator({ ...valid, memory: {} }), /memory/);
  assert.equal(calls, 0);
});

test("rejects invalid allocator products and deallocation ranges", () => {
  const invalidAllocator = createRawCanonicalMemoryAllocator({ reallocate: () => 65_536 });
  assert.throws(() => invalidAllocator({
    alignment: 1,
    byteLength: 1,
    memory: memory(),
    target: "wasm32",
  }), /out of bounds/);

  let calls = 0;
  const deallocator = createRawCanonicalMemoryDeallocator({
    reallocate() { calls++; },
  });
  assert.throws(() => deallocator({
    address: 65_536,
    alignment: 1,
    byteLength: 1,
    memory: memory(),
    target: "wasm32",
  }), /out of bounds/);
  assert.equal(calls, 0);
});

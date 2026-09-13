import assert from "node:assert/strict";
import test from "node:test";

import { createOutputSinkStream } from "./output-sink-stream.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";

class OutputStream {}
class Pollable {}

function fixture(write = () => {}) {
  const sink = Object.freeze({ write });
  return {
    request: { outputStreamType: OutputStream, pollableType: Pollable, sink },
    sink,
    stream: createOutputSinkStream({ outputStreamType: OutputStream, pollableType: Pollable, sink }),
  };
}

test("adapts an immutable byte sink to the complete synchronous output-stream contract", () => {
  const writes = [];
  const { stream } = fixture(bytes => writes.push(bytes));
  assert.equal(stream instanceof OutputStream, true);
  assert.equal(Object.isExtensible(stream), true);
  assert.equal(stream.checkWrite(), 1_000_000n);
  const first = Uint8Array.of(1, 2);
  stream.write(first);
  first[0] = 9;
  assert.deepEqual([...writes[0]], [1, 2]);

  stream.blockingWriteAndFlush(Uint8Array.of(3));
  stream.checkWrite();
  stream.writeZeroes(2n);
  stream.blockingWriteZeroesAndFlush(3n);
  assert.deepEqual(writes.map(bytes => [...bytes]), [[1, 2], [3], [0, 0], [0, 0, 0]]);

  assert.equal(stream.splice({ read(length) {
    assert.equal(length, 4n);
    return Uint8Array.of(4, 5);
  } }, 4n), 2n);
  assert.equal(stream.blockingSplice({ blockingRead(length) {
    assert.equal(length, 5n);
    return Uint8Array.of(6);
  } }, 5n), 1n);

  const pollable = stream.subscribe();
  assert.equal(pollable instanceof Pollable, true);
  assert.equal(Object.isExtensible(pollable), true);
  assert.equal(pollable.ready(), true);
  assert.equal(pollable.block(), undefined);
  pollable[resourceDisposeSymbol]();
  assert.throws(() => pollable.ready(), /pollable is disposed/);

  stream.checkWrite();
  stream.flush();
  assert.throws(() => stream.write(Uint8Array.of(7)), /exceeds the permit/);
  assert.equal(stream.blockingFlush(), undefined);
  stream[resourceDisposeSymbol]();
  stream[resourceDisposeSymbol]();
  assert.throws(() => stream.checkWrite(), value => value.tag === "closed");
  assert.deepEqual(writes.slice(-2).map(bytes => [...bytes]), [[4, 5], [6]]);
});

test("enforces output-stream write, length, splice and lifecycle invariants", () => {
  const { stream } = fixture();
  assert.throws(() => stream.write(Uint8Array.of(1)), /exceeds the permit/);
  stream.checkWrite();
  assert.throws(() => stream.write(new ArrayBuffer(1)), /Uint8Array/);
  assert.throws(() => stream.blockingWriteAndFlush([]), /Uint8Array/);
  assert.throws(
    () => stream.blockingWriteAndFlush(new Uint8Array(4097)),
    /at most 4096/);

  for (const length of [1, -1n, BigInt(Number.MAX_SAFE_INTEGER) + 1n]) {
    assert.throws(() => stream.writeZeroes(length), /safely representable u64/);
  }
  assert.throws(() => stream.writeZeroes(1_000_001n), /exceeds the permit/);
  assert.throws(
    () => stream.blockingWriteZeroesAndFlush(4097n),
    /at most 4096/);

  for (const source of [null, {}, { read: null }]) {
    assert.throws(() => stream.splice(source, 1n), /splice source is invalid/);
  }
  assert.throws(
    () => stream.splice({ read: () => [] }, 1n),
    /Uint8Array/);
  assert.throws(
    () => stream.blockingSplice({ blockingRead: () => [] }, 1n),
    /Uint8Array/);

  const prototype = Object.getPrototypeOf(stream);
  assert.throws(
    () => prototype.checkWrite.call({}),
    value => value.tag === "closed");
  assert.equal(prototype[resourceDisposeSymbol].call({}), undefined);
});

test("validates exact resource factories and the narrow immutable sink", () => {
  const valid = fixture().request;
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createOutputSinkStream(value.value),
      value.shape ? /request shape/ : /request is invalid/);
  }
  for (const [name, value] of [["outputStreamType", null], ["outputStreamType", () => {}],
    ["pollableType", null], ["pollableType", () => {}]]) {
    assert.throws(
      () => createOutputSinkStream({ ...valid, [name]: value }),
      /resource type is invalid/);
  }
  for (const sink of [null, [], {}, { write() {} }, Object.freeze({ write: null }),
    Object.freeze({ write() {}, extra: true })]) {
    assert.throws(() => createOutputSinkStream({ ...valid, sink }), /output sink/);
  }
});

function invalidObjects(base) {
  const withSymbol = { ...base, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), base);
  const nonEnumerable = { ...base };
  Object.defineProperty(nonEnumerable, "sink", { value: base.sink, enumerable: false });
  const accessor = { ...base };
  Object.defineProperty(accessor, "sink", { get: () => base.sink, enumerable: true });
  return [
    { value: null, shape: false },
    { value: 1, shape: false },
    { value: [], shape: false },
    { value: withSymbol, shape: false },
    { value: inherited, shape: true },
    { value: { ...base, extra: true }, shape: true },
    { value: { outputStreamType: base.outputStreamType, pollableType: base.pollableType }, shape: true },
    { value: nonEnumerable, shape: true },
    { value: accessor, shape: true },
  ];
}

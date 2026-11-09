import assert from "node:assert/strict";
import test from "node:test";

import { validateOutputSink, writeStreamToSink } from "./output-sink-stream.mjs";

function recordingSink() {
  const writes = [];
  return { writes, sink: Object.freeze({ write: bytes => writes.push([...bytes]) }) };
}

// A jco stream end: read({ count }) settles { value, done } per copy.
function readableStream(chunks) {
  const counts = [];
  return {
    counts,
    stream: {
      async read(options) {
        counts.push(options.count);
        const value = chunks.shift();
        return value === undefined ? { value: undefined, done: true } : { value, done: false };
      },
    },
  };
}

test("copies every chunk a guest stream yields into the sink until the guest drops it", async () => {
  const { writes, sink } = recordingSink();
  const first = Uint8Array.of(1, 2);
  const { counts, stream } = readableStream([first, [3], 4]);
  const result = await writeStreamToSink(stream, sink);
  first[0] = 9;
  assert.deepEqual(result, { tag: "ok", val: undefined });
  assert.equal(Object.isFrozen(result), true);
  assert.deepEqual(writes, [[1, 2], [3], [4]]);
  assert.deepEqual(counts, [65536, 65536, 65536, 65536]);
});

test("reads chunks from an async iterator or a reader that settles bare values", async () => {
  const iterated = recordingSink();
  async function* chunks() {
    yield Uint8Array.of(5);
    yield [6, 7];
  }
  assert.deepEqual(await writeStreamToSink(chunks(), iterated.sink), { tag: "ok", val: undefined });
  assert.deepEqual(iterated.writes, [[5], [6, 7]]);

  const bare = recordingSink();
  const values = [Uint8Array.of(8), undefined];
  assert.deepEqual(
    await writeStreamToSink({ read: async () => values.shift() }, bare.sink),
    { tag: "ok", val: undefined });
  assert.deepEqual(bare.writes, [[8]]);
  const ended = recordingSink();
  assert.deepEqual(
    await writeStreamToSink({ read: async () => null }, ended.sink),
    { tag: "ok", val: undefined });
  assert.deepEqual(ended.writes, []);
});

test("settles the write's future with io when the stream or the sink fails", async () => {
  const { sink } = recordingSink();
  assert.deepEqual(
    await writeStreamToSink({ read: async () => { throw new Error("dropped"); } }, sink),
    { tag: "err", val: "io" });
  assert.deepEqual(
    await writeStreamToSink(readableStream([{}]).stream, sink),
    { tag: "err", val: "io" });
  const failing = Object.freeze({ write() { throw new Error("closed"); } });
  assert.deepEqual(
    await writeStreamToSink(readableStream([[1]]).stream, failing),
    { tag: "err", val: "io" });
});

test("rejects streams it cannot read and sinks that are not immutable writers", async () => {
  const { sink } = recordingSink();
  await assert.rejects(writeStreamToSink(null, sink), TypeError);
  await assert.rejects(writeStreamToSink({}, sink), TypeError);
  await assert.rejects(writeStreamToSink(readableStream([]).stream, { write() {} }), TypeError);

  assert.equal(validateOutputSink(sink), sink);
  for (const value of [
    null, [], {}, { write() {} }, Object.freeze({ write: 1 }),
    Object.freeze({ write() {}, extra: true }),
    Object.freeze(Object.assign(Object.create({}), { write() {} })),
    Object.freeze({ [Symbol("write")]: true, write() {} }),
  ]) {
    assert.throws(() => validateOutputSink(value, "test sink"), TypeError);
  }
  const accessor = {};
  Object.defineProperty(accessor, "write", { enumerable: true, get: () => () => {} });
  assert.throws(() => validateOutputSink(Object.freeze(accessor)), TypeError);
});

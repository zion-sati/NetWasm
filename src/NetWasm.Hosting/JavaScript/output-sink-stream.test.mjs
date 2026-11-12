import assert from "node:assert/strict";
import test from "node:test";

import { dropGuestStream, validateOutputSink, writeStreamToSink } from "./output-sink-stream.mjs";

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

// A guest whose output the host stops taking (a sink that refuses more, as a page capping its
// console does) must see a closed pipe: left open, its next write waits for a reader forever.
test("drops the guest's stream when its copy fails, and only then", async () => {
  const refusing = Object.freeze({ write() { throw new Error("console limit"); } });
  const disposable = chunks => {
    const { stream } = readableStream(chunks);
    stream.drops = 0;
    stream[Symbol.dispose] = () => { stream.drops++; };
    return stream;
  };
  const refused = disposable([[1], [2]]);
  assert.deepEqual(await writeStreamToSink(refused, refusing), { tag: "err", val: "io" });
  assert.equal(refused.drops, 1);

  const broken = { drops: 0, read: async () => { throw new Error("broken"); } };
  broken[Symbol.dispose] = () => { broken.drops++; };
  assert.deepEqual(await writeStreamToSink(broken, recordingSink().sink), { tag: "err", val: "io" });
  assert.equal(broken.drops, 1);

  async function* lines() {
    yield Uint8Array.of(1);
    yield Uint8Array.of(2);
  }
  const iterated = lines();
  assert.deepEqual(await writeStreamToSink(iterated, refusing), { tag: "err", val: "io" });
  assert.deepEqual(await iterated.next(), { value: undefined, done: true });

  const completed = disposable([[3]]);
  const { writes, sink } = recordingSink();
  assert.deepEqual(await writeStreamToSink(completed, sink), { tag: "ok", val: undefined });
  assert.deepEqual(writes, [[3]]);
  assert.equal(completed.drops, 0);
});

test("drops a jco stream end by disposing it and an iterator by returning it", () => {
  const calls = [];
  dropGuestStream({ [Symbol.dispose]() { calls.push(["dispose", this.name]); }, name: "jco" });
  dropGuestStream({ [Symbol.for("dispose")]() { calls.push(["dispose", this.name]); }, name: "polyfill" });
  dropGuestStream({ return() { calls.push(["return", this.name]); }, name: "iterator" });
  assert.deepEqual(calls, [["dispose", "jco"], ["dispose", "polyfill"], ["return", "iterator"]]);
  // A stream its guest already let go of, or nothing at all, is left as it is.
  dropGuestStream({ [Symbol.dispose]() { throw new Error("already dropped"); } });
  dropGuestStream({});
  dropGuestStream(null);
  dropGuestStream(undefined);
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

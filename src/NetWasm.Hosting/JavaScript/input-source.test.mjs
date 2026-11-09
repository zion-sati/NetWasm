import assert from "node:assert/strict";
import test from "node:test";

import { readInputSource, validateInputSource } from "./input-source.mjs";

test("accepts no standard input or an immutable reader", () => {
  assert.equal(validateInputSource(null), null);
  const source = Object.freeze({ read() {} });
  assert.strictEqual(validateInputSource(source, "stdin"), source);
});

test("rejects anything else, naming the boundary", () => {
  for (const value of [undefined, 1, "stdin", [], {}, { read() {} }, Object.freeze({ read: 1 }),
    Object.freeze({ read() {}, extra: 1 }), Object.freeze(Object.create({ read() {} })),
    Object.freeze(Object.assign(Object.create(null), { read() {} })),
    Object.freeze({ read() {}, [Symbol("hidden")]: 1 })]) {
    assert.throws(() => validateInputSource(value, "test stdin"), /test stdin must be null or an immutable reader/);
  }
  assert.throws(() => validateInputSource(1), /input source must be null/);
});

function iterate(chunks) {
  return chunks[Symbol.iterator]();
}

async function drain(stream) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(chunk);
  return chunks;
}

test("copies byte and text chunks until the end of input, then settles ok", async () => {
  const bytes = Uint8Array.of(1, 2);
  let settled;
  const chunks = await drain(readInputSource(iterate([bytes, "é"]), value => { settled = value; }, () => false));
  assert.deepEqual(chunks, [Uint8Array.of(1, 2), Uint8Array.of(0xc3, 0xa9)]);
  assert.notStrictEqual(chunks[0], bytes);
  assert.deepEqual(settled, { tag: "ok", val: undefined });
  assert.equal(Object.isFrozen(settled), true);
});

test("stops reading once the execution has ended", async () => {
  let reads = 0;
  let settled;
  const iterator = { next() { reads++; return { done: false, value: Uint8Array.of(1) }; } };
  assert.deepEqual(await drain(readInputSource(iterator, value => { settled = value; }, () => true)), []);
  assert.equal(reads, 0);
  assert.deepEqual(settled, { tag: "ok", val: undefined });
});

test("settles io when the source fails or yields something other than bytes", async () => {
  for (const iterator of [
    { next() { throw new Error("closed"); } },
    { async next() { throw new Error("closed"); } },
    iterate([42]),
  ]) {
    let settled;
    assert.deepEqual(await drain(readInputSource(iterator, value => { settled = value; }, () => false)), []);
    assert.deepEqual(settled, { tag: "err", val: "io" });
  }
});

import assert from "node:assert/strict";
import test from "node:test";

import { createRawComponentModel } from "./raw-component-model.mjs";
import { createSyncFuture, createSyncStream, readSyncDrain } from "./sync-values.mjs";

const bytes = Object.freeze({ align: 1, bytes: true, lift() {}, lower() {}, size: 1 });
const u32 = Object.freeze({
  align: 4,
  bytes: false,
  lift: (memory, address) => new DataView(memory.buffer).getUint32(address, true),
  lower: (memory, address, value) => new DataView(memory.buffer).setUint32(address, value, true),
  size: 4,
});

function instance() {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const model = createRawComponentModel({ memory: () => memory });
  const view = () => new DataView(memory.buffer);
  const write = (address, values) => new Uint8Array(memory.buffer, address, values.length).set(values);
  const read = (address, length) => [...new Uint8Array(memory.buffer, address, length)];
  const event = address => [view().getUint32(address, true), view().getUint32(address + 4, true)];
  const split = packed => [Number(packed & 0xffffffffn), Number(packed >> 32n)];
  return { memory, model, view, write, read, event, split };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

test("standard output: the guest writes a stream the host reads", async () => {
  const { model, write, split } = instance();
  const [readable, writable] = split(model.stream.new(bytes));
  const reader = model.liftStream(readable);
  write(100, [1, 2, 3]);
  assert.equal(model.stream.write(bytes, writable, 100, 3), (3 << 4) | 0);
  assert.deepEqual([...(await reader.read({ count: 64 })).value], [1, 2, 3]);
  const pending = reader.next();
  write(100, [4]);
  model.stream.write(bytes, writable, 100, 1);
  assert.deepEqual([...(await pending).value], [4]);
  model.stream.dropWritable(writable);
  assert.deepEqual(await reader.read(), { done: true, value: undefined });
  assert.equal(reader[Symbol.asyncIterator](), reader);

  const [other, otherWritable] = split(model.stream.new(bytes));
  const gone = model.liftStream(other);
  await gone.return();
  assert.equal(model.stream.write(bytes, otherWritable, 100, 1), 1);
  gone[Symbol.dispose]();
});

test("a file's contents: the guest reads a stream the host supplies", async () => {
  const { model, read, event } = instance();
  const set = model.root["[waitable-set-new]"]();

  const sync = model.lowerStream([Uint8Array.of(1, 2, 3), [4]], bytes);
  assert.equal(model.stream.read(bytes, sync, 200, 2), (2 << 4) | 0);
  assert.deepEqual(read(200, 2), [1, 2]);
  assert.equal(model.stream.read(bytes, sync, 200, 8), (2 << 4) | 0);
  assert.deepEqual(read(200, 2), [3, 4]);
  assert.equal(model.stream.read(bytes, sync, 200, 8), 1);
  model.stream.dropReadable(sync);

  async function* chunks() { yield Uint8Array.of(7, 8); yield []; yield Uint8Array.of(9); }
  const asynchronous = model.lowerStream(chunks(), bytes);
  model.root["[waitable-join]"](asynchronous, set);
  assert.equal(model.stream.read(bytes, asynchronous, 300, 4), 0xffffffff);
  assert.throws(() => model.stream.read(bytes, asynchronous, 300, 4), /already pending/);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 2);
  assert.deepEqual(event(0), [asynchronous, (2 << 4) | 0]);
  assert.deepEqual(read(300, 2), [7, 8]);
  assert.equal(model.stream.read(bytes, asynchronous, 300, 4), 0xffffffff);
  await settle();
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 2);
  assert.deepEqual(event(0), [asynchronous, (1 << 4) | 0]);
  assert.equal(model.stream.read(bytes, asynchronous, 300, 4), 0xffffffff);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 2);
  assert.deepEqual(event(0), [asynchronous, 1]);

  let cancelled = false;
  const reader = new ReadableStream({ pull() {}, cancel() { cancelled = true; } });
  const web = model.lowerStream(reader, bytes);
  assert.equal(model.stream.read(bytes, web, 300, 4), 0xffffffff);
  assert.equal(model.stream.cancelRead(web), 2);
  assert.throws(() => model.stream.cancelRead(web), /no stream read/);
  model.stream.dropReadable(web);
  await settle();
  assert.equal(cancelled, true);
  const refusing = model.lowerStream(new ReadableStream({ cancel() { throw new Error("busy"); } }), bytes);
  model.stream.dropReadable(refusing);
  await settle();

  const failing = model.lowerStream({ async *[Symbol.asyncIterator]() { throw new Error("disk"); } }, bytes);
  model.root["[waitable-join]"](failing, set);
  assert.equal(model.stream.read(bytes, failing, 300, 4), 0xffffffff);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 2);
  assert.deepEqual(event(0), [failing, 1]);
  assert.throws(() => model.lowerStream(1, bytes), /not iterable/);
});

test("pending host reads end when the guest drops its end or the host disposes its reader", async () => {
  const { model, read, split } = instance();
  const [readable, writable] = split(model.stream.new(bytes));
  const reader = model.liftStream(readable);
  const pending = reader.read();
  model.stream.dropWritable(writable);
  assert.deepEqual(await pending, { done: true, value: undefined });

  const [other] = split(model.stream.new(bytes));
  const disposed = model.liftStream(other);
  const waiting = disposed.read();
  disposed[Symbol.dispose]();
  assert.deepEqual(await waiting, { done: true, value: undefined });

  const [ownReadable] = split(model.stream.new(bytes));
  assert.equal(model.stream.read(bytes, ownReadable, 0, 1), 0xffffffff);

  let returned = false;
  function* chunks() {
    try { yield Uint8Array.of(1); yield Uint8Array.of(2); } finally { returned = true; }
  }
  const sync = model.lowerStream(chunks(), bytes);
  assert.equal(model.stream.read(bytes, sync, 0, 1), (1 << 4) | 0);
  model.stream.dropReadable(sync);
  assert.equal(returned, true);

  const web = model.lowerStream(new ReadableStream({
    start(controller) { controller.enqueue(Uint8Array.of(5, 6)); controller.close(); },
  }), bytes);
  const set = model.root["[waitable-set-new]"]();
  model.root["[waitable-join]"](web, set);
  assert.equal(model.stream.read(bytes, web, 50, 4), 0xffffffff);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 2);
  assert.deepEqual(read(50, 2), [5, 6]);
});

test("streams of records lift and lower each element through the codec", async () => {
  const { model, view, split } = instance();
  const [readable, writable] = split(model.stream.new(u32));
  const reader = model.liftStream(readable);
  view().setUint32(400, 11, true);
  view().setUint32(404, 22, true);
  model.stream.write(u32, writable, 400, 2);
  assert.deepEqual((await reader.read()).value, [11, 22]);

  const lowered = model.lowerStream([33, 44, 55], u32);
  assert.equal(model.stream.read(u32, lowered, 500, 2), (2 << 4) | 0);
  assert.deepEqual([view().getUint32(500, true), view().getUint32(504, true)], [33, 44]);
  assert.equal(model.stream.read(u32, lowered, 500, 3), (1 << 4) | 0);
  assert.equal(view().getUint32(500, true), 55);
  assert.equal(model.stream.read(u32, lowered, 500, 3), 1);
});

test("futures settle in both directions", async () => {
  const { model, view, event, split } = instance();
  const set = model.root["[waitable-set-new]"]();
  let resolve;
  const later = model.lowerFuture(new Promise(done => { resolve = done; }), u32);
  model.root["[waitable-join]"](later, set);
  assert.equal(model.future.read(u32, later, 600), 0xffffffff);
  resolve(77);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 4);
  assert.deepEqual(event(0), [later, 0]);
  assert.equal(view().getUint32(600, true), 77);
  const ready = model.lowerFuture(Promise.resolve(5), u32);
  await settle();
  assert.equal(model.future.read(u32, ready, 600), 0);
  assert.equal(view().getUint32(600, true), 5);
  model.future.dropReadable(ready);

  const cancelled = model.lowerFuture(new Promise(() => {}), u32);
  model.future.read(u32, cancelled, 600);
  assert.equal(model.future.cancelRead(cancelled), 2);

  const [readable, writable] = split(model.future.new(u32));
  const value = model.liftFuture(readable);
  view().setUint32(700, 9, true);
  assert.equal(model.future.write(u32, writable, 700), 0);
  model.future.dropWritable(writable);
  assert.equal(await value, 9);

  const [early, earlyWritable] = split(model.future.new(u32));
  view().setUint32(700, 10, true);
  model.future.write(u32, earlyWritable, 700);
  assert.equal(await model.liftFuture(early), 10);

  const [gone, goneWritable] = split(model.future.new(u32));
  model.future.dropReadable(gone);
  assert.equal(model.future.write(u32, goneWritable, 700), 1);
  const [, unwritten] = split(model.future.new(u32));
  assert.throws(() => model.future.dropWritable(unwritten), /before it was written/);
  assert.throws(() => model.future.cancelWrite(goneWritable), /no future write/);
});

// A synchronous provider's results reach the guest without a wait: its future is computed when
// the guest reads it, its stream pulled at once, and a stream the guest writes drained at once.
test("a synchronous provider's futures and streams complete at once", async () => {
  const { model, view, event, write, read, split } = instance();
  const set = model.root["[waitable-set-new]"]();
  let computed = 0;
  const known = model.lowerFuture(createSyncFuture(() => ++computed + 40), u32);
  assert.equal(computed, 0);
  assert.equal(model.future.read(u32, known, 600), 0);
  assert.equal(view().getUint32(600, true), 41);

  // A computation that cannot finish yet waits like any other future.
  let resolve;
  const deferred = model.lowerFuture(createSyncFuture(() => new Promise(done => { resolve = done; })), u32);
  model.root["[waitable-join]"](deferred, set);
  assert.equal(model.future.read(u32, deferred, 600), 0xffffffff);
  resolve(9);
  await settle();
  assert.equal(model.root["[waitable-set-wait]"](set, 0), 4);
  assert.deepEqual(event(0), [deferred, 0]);
  assert.equal(view().getUint32(600, true), 9);

  // A source with both iterators is pulled synchronously.
  const both = model.lowerStream(createSyncStream([Uint8Array.of(5, 6)][Symbol.iterator]()), bytes);
  assert.equal(model.stream.read(bytes, both, 200, 8), (2 << 4) | 0);
  assert.deepEqual(read(200, 2), [5, 6]);

  const [readable, writable] = split(model.stream.new(bytes));
  const drain = readSyncDrain(model.liftStream(readable));
  assert.equal(drain(), undefined);
  write(100, [7, 8]);
  model.stream.write(bytes, writable, 100, 2);
  assert.deepEqual([...drain()], [7, 8]);
  model.stream.dropWritable(writable);
  assert.equal(drain(), null);
  assert.equal(readSyncDrain({}), null);
  assert.throws(() => createSyncFuture(null), /needs a computation/);
});

test("async-lowered host calls return at once or through their subtask", async () => {
  const { model, event } = instance();
  const delivered = [];
  assert.equal(model.callAsync(() => 3, value => delivered.push(value)), 2);
  const set = model.root["[waitable-set-new]"]();
  let resolve;
  const status = model.callAsync(() => new Promise(done => { resolve = done; }), value => delivered.push(value));
  const subtask = status >> 4;
  assert.equal(status & 0xf, 1);
  assert.throws(() => model.root["[subtask-drop]"](subtask), /before it returns/);
  model.root["[waitable-join]"](subtask, set);
  assert.equal(model.root["[waitable-set-poll]"](set, 0), 0);
  resolve(4);
  await settle();
  assert.equal(model.root["[waitable-set-poll]"](set, 0), 1);
  assert.deepEqual(event(0), [subtask, 2]);
  assert.deepEqual(delivered, [3, 4]);
  assert.equal(model.root["[subtask-cancel]"](subtask), 2);
  model.root["[subtask-drop]"](subtask);

  let late;
  const cancelled = model.callAsync(() => new Promise(done => { late = done; }), () => assert.fail("cancelled")) >> 4;
  assert.equal(model.root["[subtask-cancel]"](cancelled), 4);
  model.root["[subtask-drop]"](cancelled);
  late(1);
  await settle();
  assert.throws(() => model.root["[subtask-drop]"](cancelled), /invalid Component Model handle/);
});

test("waitable sets join, leave and report events in the order they occur", async () => {
  const { model, event } = instance();
  const first = model.root["[waitable-set-new]"]();
  const second = model.root["[waitable-set-new]"]();
  const subtask = model.callAsync(() => Promise.resolve(1), () => {}) >> 4;
  await settle();
  model.root["[waitable-join]"](subtask, first);
  model.root["[waitable-join]"](subtask, second);
  assert.equal(model.root["[waitable-set-poll]"](first, 0), 0);
  model.root["[waitable-join]"](subtask, 0);
  assert.equal(model.root["[waitable-set-poll]"](second, 0), 0);
  assert.equal(model.root["[waitable-set-wait]"](second, 0), 0);
  assert.deepEqual(event(0), [0, 0]);
  model.root["[waitable-join]"](subtask, first);
  assert.throws(() => model.root["[waitable-set-drop]"](first), /still has members/);
  assert.equal(model.root["[waitable-set-wait]"](first, 0), 1);
  assert.deepEqual(event(0), [subtask, 2]);
  model.root["[subtask-drop]"](subtask);
  model.root["[waitable-set-drop]"](first);
  assert.throws(() => model.root["[waitable-join]"](99, second), /invalid Component Model handle 99/);
  assert.throws(() => model.root["[waitable-join]"](second, second), /invalid/);
  model.root["[backpressure-inc]"]();
  model.root["[backpressure-dec]"]();
  model.root["[context-set-0]"](42);
  assert.equal(model.root["[context-get-0]"](), 42);
});

test("drives a callback-lifted export through its events until it exits", async () => {
  const { model } = instance();
  const set = model.root["[waitable-set-new]"]();
  let resolve;
  const subtask = model.callAsync(() => new Promise(done => { resolve = done; }), () => {}) >> 4;
  model.root["[waitable-join]"](subtask, set);
  const seen = [];
  const codes = [1, 3 | (set << 4), 0];
  const driving = model.drive(2 | (set << 4), (...value) => {
    seen.push(value);
    return codes.shift();
  });
  setTimeout(() => resolve(0), 1);
  await driving;
  assert.deepEqual(seen, [[1, subtask, 2], [0, 0, 0], [0, 0, 0]]);
  await assert.rejects(model.drive(7, () => 0), /invalid callback code 7/);
});

test("a wake gives a waiting callback a turn without an event", async () => {
  const { model } = instance();
  const set = model.root["[waitable-set-new]"]();
  const seen = [];
  const codes = [2 | (set << 4), 0];
  const driving = model.drive(2 | (set << 4), (...value) => {
    seen.push(value);
    return codes.shift();
  });
  setTimeout(() => model.wake(), 1);
  await new Promise(resolve => setTimeout(resolve, 5));
  assert.deepEqual(seen, [[0, 0, 0]]);
  model.wake();
  await driving;
  assert.deepEqual(seen, [[0, 0, 0], [0, 0, 0]]);
});

test("a host call that rejects or a lowering that throws fails the driven export", async () => {
  for (const [start, deliver] of [
    [() => Promise.reject(new Error("host failed")), () => {}],
    [() => Promise.resolve(1), () => { throw new Error("host failed"); }],
  ]) {
    const { model } = instance();
    const set = model.root["[waitable-set-new]"]();
    model.callAsync(start, deliver);
    await assert.rejects(model.drive(2 | (set << 4), () => 0), /host failed/);
    assert.throws(() => model.callAsync(() => 1, () => {}), /host failed/);
  }
  const { model } = instance();
  const set = model.root["[waitable-set-new]"]();
  const failing = model.lowerFuture(Promise.reject("refused"), u32);
  model.future.read(u32, failing, 0);
  await assert.rejects(model.drive(2 | (set << 4), () => 0), /refused/);
  const lowering = instance();
  const throwing = { ...u32, lower() { throw new Error("lowering"); } };
  const future = lowering.model.lowerFuture(Promise.resolve(1), throwing);
  lowering.model.future.read(throwing, future, 0);
  await settle();
  await assert.rejects(lowering.model.drive(2, () => 0), /lowering/);
});

test("closing ends the guest's open streams and rejects waits", async () => {
  const { model, split } = instance();
  const set = model.root["[waitable-set-new]"]();
  const [readable] = split(model.stream.new(bytes));
  const reader = model.liftStream(readable);
  let returned = false;
  model.lowerStream({ [Symbol.asyncIterator]: () => ({ next: () => new Promise(() => {}), return() { returned = true; } }) }, bytes);
  const pendingRead = reader.read();
  const waiting = model.drive(2 | (set << 4), () => 0);
  model.close();
  model.close();
  assert.deepEqual(await pendingRead, { done: true, value: undefined });
  assert.equal(returned, true);
  await assert.rejects(waiting, /closed/);
  assert.throws(() => model.callAsync(() => 1, () => {}), /closed/);
});

test("rejects invalid options, codecs and handles", () => {
  for (const options of [null, {}, { memory: 1 }]) {
    assert.throws(() => createRawComponentModel(options), TypeError);
  }
  const { model } = instance();
  for (const codec of [null, {}, { ...bytes, size: -1 }, { ...bytes, align: 0 }, { ...bytes, bytes: 1 },
    { ...bytes, lift: null }, { ...bytes, extra: 1 }]) {
    assert.throws(() => model.stream.new(codec), TypeError);
  }
  assert.throws(() => model.stream.write(bytes, 5, 0, 1), /invalid Component Model handle/);
  const [, writable] = [0, Number(model.stream.new(bytes) >> 32n)];
  assert.throws(() => model.stream.cancelWrite(writable), /no stream write/);
});

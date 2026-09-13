import assert from "node:assert/strict";
import test from "node:test";
import {
  borrowRawResource,
  closeRawResourceStore,
  createRawResourceStore,
  dropRawResource,
  registerRawResource,
  transferRawResource,
} from "./raw-resource-store.mjs";

const register = (store, type, value, release = null) => registerRawResource({
  store,
  type,
  value,
  release,
});
const handleRequest = (store, type, handle) => ({ store, type, handle });

test("isolates resource types and execution stores", () => {
  const first = createRawResourceStore();
  const second = createRawResourceStore();
  const descriptor = {};
  const stream = {};
  const other = {};

  assert.equal(Object.isFrozen(first), true);
  assert.equal(Object.getPrototypeOf(first), null);
  assert.equal(register(first, 4, descriptor), 1);
  assert.equal(register(first, 7, stream), 1);
  assert.equal(register(second, 4, other), 1);
  assert.equal(borrowRawResource(handleRequest(first, 4, 1)), descriptor);
  assert.equal(borrowRawResource(handleRequest(first, 7, 1)), stream);
  assert.equal(borrowRawResource(handleRequest(second, 4, 1)), other);
});

test("borrows without mutation and transfers ownership without release", () => {
  const store = createRawResourceStore();
  const calls = [];
  const value = {};
  const handle = register(store, 2, value, resource => calls.push(resource));

  assert.equal(borrowRawResource(handleRequest(store, 2, handle)), value);
  assert.equal(borrowRawResource(handleRequest(store, 2, handle)), value);
  assert.equal(transferRawResource(handleRequest(store, 2, handle)), value);
  assert.deepEqual(calls, []);
  assert.throws(() => borrowRawResource(handleRequest(store, 2, handle)), /unavailable/);
});

test("drops exactly once before invoking the destructor", () => {
  const store = createRawResourceStore();
  const value = {};
  let availableDuringRelease = true;
  const handle = register(store, 3, value, () => {
    availableDuringRelease = false;
    assert.throws(() => borrowRawResource(handleRequest(store, 3, handle)), /unavailable/);
    throw new Error("private destructor failure");
  });

  assert.throws(() => dropRawResource(handleRequest(store, 3, handle)), /private destructor/);
  assert.equal(availableDuringRelease, false);
  assert.throws(() => dropRawResource(handleRequest(store, 3, handle)), /unavailable/);
});

test("reuses released slots without a lifetime-operation limit", () => {
  const store = createRawResourceStore();
  const first = register(store, 1, "first");
  const second = register(store, 1, "second");
  dropRawResource(handleRequest(store, 1, first));
  dropRawResource(handleRequest(store, 1, second));

  assert.equal(register(store, 1, "third"), second);
  assert.equal(register(store, 1, "fourth"), first);
  assert.equal(register(store, 1, "fifth"), 3);
});

test("normalizes signed i32 handles at the resource boundary", () => {
  const store = createRawResourceStore();
  const value = {};
  const handle = register(store, 5, value);

  assert.equal(borrowRawResource(handleRequest(store, 5, handle | 0)), value);
  assert.throws(
    () => borrowRawResource(handleRequest(store, 5, -1)),
    /unavailable/);
});

test("closes remaining ownership in reverse order and attempts every release", () => {
  const store = createRawResourceStore();
  const calls = [];
  register(store, 1, "first", value => calls.push(value));
  const transferred = register(store, 1, "transferred", value => calls.push(value));
  register(store, 2, "second", value => {
    calls.push(value);
    throw new Error("private second");
  });
  register(store, 1, "third", value => {
    calls.push(value);
    throw new Error("private third");
  });
  transferRawResource(handleRequest(store, 1, transferred));

  assert.throws(
    () => closeRawResourceStore({ store }),
    error => error instanceof AggregateError
      && error.errors.map(item => item.message).join(",") === "private third,private second");
  assert.deepEqual(calls, ["third", "second", "first"]);
  assert.throws(() => closeRawResourceStore({ store }), /already closed/);
});

test("rejects malformed requests before mutating state", () => {
  const store = createRawResourceStore();
  const release = () => { throw new Error("must not release"); };
  for (const request of [null, [], 1, {},
    { store, type: 1, value: {}, release, extra: true },
    Object.defineProperty({ store, type: 1, value: {} }, "release", {
      get() { throw new Error("must not read accessor"); }, enumerable: true,
    }),
    { store, type: 1, value: {}, release, [Symbol("bad")]: true }]) {
    assert.throws(() => registerRawResource(request), TypeError);
  }
  for (const type of [-1, 1.5, Number.MAX_SAFE_INTEGER + 1]) {
    assert.throws(() => register(store, type, {}, release), /type/);
  }
  assert.throws(() => register(store, 1, {}, 1), /release/);
  assert.equal(register(store, 1, "retained"), 1);
  assert.equal(borrowRawResource(handleRequest(store, 1, 1)), "retained");
});

test("rejects invalid stores, types and handles consistently", () => {
  const store = createRawResourceStore();
  register(store, 1, {});
  for (const action of [borrowRawResource, transferRawResource, dropRawResource]) {
    for (const request of [
      handleRequest({}, 1, 1),
      handleRequest(store, -1, 1),
      handleRequest(store, 1, 0),
      handleRequest(store, 1, -0x8000_0001),
      handleRequest(store, 1, 0x1_0000_0000),
      handleRequest(store, 1, 1.5),
      handleRequest(store, 2, 1),
      { ...handleRequest(store, 1, 1), extra: true },
    ]) {
      assert.throws(() => action(request), TypeError);
    }
  }
  assert.throws(() => closeRawResourceStore({ store: {} }), /store/);
});

test("rejects every action after close without invoking a release", () => {
  const store = createRawResourceStore();
  closeRawResourceStore({ store });
  for (const action of [
    () => register(store, 1, {}),
    () => borrowRawResource(handleRequest(store, 1, 1)),
    () => transferRawResource(handleRequest(store, 1, 1)),
    () => dropRawResource(handleRequest(store, 1, 1)),
  ]) {
    assert.throws(action, /closed/);
  }
});

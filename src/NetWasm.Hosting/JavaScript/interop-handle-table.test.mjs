import assert from "node:assert/strict";
import test from "node:test";

import { createInteropHandleTable } from "./interop-handle-table.mjs";

test("owns, reuses, and deterministically closes ordinary handles", () => {
  const table = createInteropHandleTable();
  const first = table.acquire("first");
  const second = table.acquire({ value: 2 });
  assert.deepEqual([first, second, table.count], [1, 2, 2]);
  assert.equal(table.get(first), "first");
  table.release(first);
  assert.equal(table.count, 1);
  assert.throws(() => table.get(first), /stale/);
  assert.equal(table.acquire("reused"), first);
  assert.equal(table.get(first), "reused");
  table.dispose();
  assert.equal(table.count, 0);
  assert.throws(() => table.acquire("late"), /unavailable/);
  assert.throws(() => table.get(second), /stale/);
  assert.throws(() => table.release(second), /stale/);
  table.releaseSubscription(second);
});

test("releases subscriptions and managed callbacks exactly once", () => {
  const calls = [];
  const table = createInteropHandleTable({ maximumHandle: 3 });
  const first = table.acquireSubscription(
    { dispose() { calls.push("dispose"); } },
    [() => calls.push("first"), () => calls.push("second")]);
  table.releaseSubscription(first);
  table.releaseSubscription(first);
  assert.deepEqual(calls, ["dispose", "first", "second"]);
  assert.equal(table.acquire("replacement"), first);

  table.acquireSubscription({}, undefined);
  table.dispose();
  assert.deepEqual(calls, ["dispose", "first", "second"]);
});

test("continues closing after a subscription release fails", () => {
  const calls = [];
  const table = createInteropHandleTable({ maximumHandle: 2 });
  table.acquireSubscription({ dispose() { throw new Error("failed"); } }, [
    () => calls.push("callback"),
  ]);
  table.acquireSubscription({ dispose() { calls.push("remaining"); } }, []);
  table.dispose();
  assert.deepEqual(calls, ["callback", "remaining"]);
});

test("enforces the configured nonzero i32 allocation space", () => {
  const table = createInteropHandleTable({ maximumHandle: 1 });
  assert.equal(table.acquire("only"), 1);
  assert.throws(() => table.acquire("overflow"), /exhausted/);
  table.release(1);
  assert.equal(table.acquire("reused"), 1);
  for (const handle of [1.5, Number.NaN]) {
    assert.throws(() => table.get(handle), /must be an i32/);
  }
  assert.throws(() => table.release(-1), /stale.*4294967295/);
});

test("rejects malformed handle-table options", () => {
  for (const value of [
    null,
    [],
    {},
    { maximumHandle: 0 },
    { maximumHandle: 0x1_0000_0000 },
    { maximumHandle: 1.5 },
    { maximumHandle: 1, extra: true },
    { maximumHandle: 1, [Symbol("invalid")]: true },
    Object.create({ maximumHandle: 1 }),
    Object.defineProperty({}, "maximumHandle", { enumerable: true, get: () => 1 }),
  ]) {
    assert.throws(() => createInteropHandleTable(value), TypeError);
  }
});

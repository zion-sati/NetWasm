import assert from "node:assert/strict";
import test from "node:test";
import { createPollableReactor } from "./pollable-reactor.mjs";

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((accept, decline) => {
    resolve = accept;
    reject = decline;
  });
  return { promise, resolve, reject };
}

function fixture(overrides = {}) {
  const calls = [];
  const jobs = [];
  const reactor = createPollableReactor({
    onReady(token) { calls.push(`ready:${token}`); },
    onFailure() { calls.push("failure"); },
    schedule(callback) { jobs.push(callback); },
    ...overrides,
  });
  return { calls, jobs, reactor };
}

test("waits for actual readiness, disposes first, and notifies once", async () => {
  const wait = deferred();
  const { calls, jobs, reactor } = fixture();
  reactor.watch({
    block() { calls.push("block"); return wait.promise; },
    [Symbol.dispose]() { calls.push("dispose"); },
  }, 17);
  assert.deepEqual(calls, []);
  jobs.shift()();
  assert.deepEqual(calls, ["block"]);
  wait.resolve();
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(calls, ["block", "dispose", "ready:17"]);
  assert.equal(reactor.cancel(17), false);
  reactor.close();
});

test("cancels before or during block and suppresses stale readiness", async () => {
  const before = fixture();
  let beforeDisposed = 0;
  before.reactor.watch({ block() {}, dispose() { beforeDisposed++; } }, 1);
  assert.equal(before.reactor.cancel(1), true);
  before.jobs.shift()();
  await Promise.resolve();
  assert.equal(beforeDisposed, 1);
  assert.deepEqual(before.calls, []);

  const wait = deferred();
  const during = fixture();
  let duringDisposed = 0;
  during.reactor.watch({
    block() { return wait.promise; },
    dispose() { duringDisposed++; },
  }, 2);
  during.jobs.shift()();
  assert.equal(during.reactor.cancel(2), true);
  wait.resolve();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(duringDisposed, 1);
  assert.deepEqual(during.calls, []);
});

test("contains schedule, block, readiness, and disposal failures", async () => {
  for (const createCase of [
    () => fixture({ schedule() { throw new Error("private schedule detail"); } }),
    () => fixture({ onReady() { throw new Error("private readiness detail"); } }),
  ]) {
    const state = createCase();
    state.reactor.watch({ block() {} }, 3);
    state.jobs.shift?.()?.();
    await Promise.resolve();
    await Promise.resolve();
    assert.deepEqual(state.calls, ["failure"]);
  }

  const syncBlock = fixture({ schedule: callback => callback() });
  syncBlock.reactor.watch({ block() { throw new Error("private block detail"); } }, 4);
  assert.deepEqual(syncBlock.calls, ["failure"]);

  const asyncBlock = fixture({ schedule: callback => callback() });
  asyncBlock.reactor.watch({ block() { return Promise.reject(new Error("private block detail")); } }, 5);
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(asyncBlock.calls, ["failure"]);

  const disposal = fixture({ schedule: callback => callback() });
  disposal.reactor.watch({
    block() {},
    [Symbol.dispose]() { throw new Error("private disposal detail"); },
  }, 6);
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(disposal.calls, ["failure"]);

  const cancelDisposal = fixture();
  cancelDisposal.reactor.watch({
    block() {},
    dispose() { throw new Error("private disposal detail"); },
  }, 7);
  assert.equal(cancelDisposal.reactor.cancel(7), true);
  assert.deepEqual(cancelDisposal.calls, ["failure"]);

  const sink = fixture({
    schedule: callback => callback(),
    onFailure() { throw new Error("private sink detail"); },
  });
  assert.doesNotThrow(() => sink.reactor.watch({ block() { throw new Error(); } }, 8));
});

test("supports standard, legacy, method, and absent pollable disposal", async () => {
  const calls = [];
  const reactor = createPollableReactor({
    onReady: token => calls.push(`ready:${token}`),
    onFailure: () => calls.push("failure"),
    schedule: callback => callback(),
  });
  const pollables = [
    { block() {}, [Symbol.dispose]() { calls.push("standard"); } },
    { block() {}, [Symbol.for("dispose")]() { calls.push("legacy"); } },
    { block() {}, dispose() { calls.push("method"); } },
    { block() {} },
  ];
  for (let index = 0; index < pollables.length; index++) {
    reactor.watch(pollables[index], index + 1);
  }
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(calls, [
    "standard", "ready:1",
    "legacy", "ready:2",
    "method", "ready:3",
    "ready:4",
  ]);
});

test("closes all pending pollables once and suppresses late work", async () => {
  const wait = deferred();
  const { calls, jobs, reactor } = fixture();
  let disposed = 0;
  reactor.watch({ block() { return wait.promise; }, dispose() { disposed++; } }, 11);
  reactor.watch({ block() {}, dispose() { disposed++; } }, 12);
  jobs.shift()();
  reactor.close();
  reactor.close();
  jobs.shift()();
  wait.resolve();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(disposed, 2);
  assert.deepEqual(calls, []);
  assert.throws(() => reactor.watch({ block() {} }, 13), /closed/);

  const failure = fixture();
  failure.reactor.watch({ block() {}, dispose() { throw new Error("private"); } }, 14);
  failure.reactor.watch({ block() {}, dispose() { throw new Error("private"); } }, 15);
  assert.throws(() => failure.reactor.close(), /could not release/);
  assert.deepEqual(failure.calls, []);

  const reentrant = fixture({ schedule: callback => callback() });
  reentrant.reactor.watch({
    block() {},
    dispose() { reentrant.reactor.close(); },
  }, 16);
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(reentrant.calls, []);
  assert.throws(() => reentrant.reactor.watch({ block() {} }, 17), /closed/);
});

test("validates dependencies, pollables, and token ownership", () => {
  assert.throws(() => createPollableReactor(), TypeError);
  assert.throws(() => createPollableReactor({ onReady() {} }), TypeError);
  assert.throws(() => createPollableReactor({ onReady() {}, onFailure() {}, schedule: null }), TypeError);
  const { reactor } = fixture();
  assert.throws(() => reactor.watch(null, 1), TypeError);
  assert.throws(() => reactor.watch({}, 1), TypeError);
  for (const token of [0, -1, 1.5, 0x100000000]) {
    assert.throws(() => reactor.watch({ block() {} }, token), TypeError);
    assert.throws(() => reactor.cancel(token), TypeError);
  }
  reactor.watch({ block() {} }, 1);
  assert.throws(() => reactor.watch({ block() {} }, 1), /already watched/);
});

test("independent reactors own identical tokens and shutdown separately", async () => {
  const firstWait = deferred();
  const secondWait = deferred();
  const first = fixture();
  const second = fixture();
  let firstDisposed = 0;
  let secondDisposed = 0;
  first.reactor.watch({
    block() { return firstWait.promise; },
    dispose() { firstDisposed++; },
  }, 1);
  second.reactor.watch({
    block() { return secondWait.promise; },
    dispose() { secondDisposed++; },
  }, 1);
  first.jobs.shift()();
  second.jobs.shift()();
  first.reactor.close();
  assert.equal(firstDisposed, 1);
  assert.equal(secondDisposed, 0);
  firstWait.resolve();
  secondWait.resolve();
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(first.calls, []);
  assert.deepEqual(second.calls, ["ready:1"]);
  assert.equal(firstDisposed, 1);
  assert.equal(secondDisposed, 1);
  second.reactor.close();
  assert.equal(secondDisposed, 1);
});

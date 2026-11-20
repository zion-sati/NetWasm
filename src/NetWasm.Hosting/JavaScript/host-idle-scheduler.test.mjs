// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { selectIdleScheduler, selectQuietTimer } from "./host-idle-scheduler.mjs";

test("uses the browser's idle callback where it has one", () => {
  const requests = [];
  const schedule = selectIdleScheduler({
    document: {},
    requestIdleCallback(callback, ...rest) { requests.push([callback, rest]); },
    setTimeout() { assert.fail("no timer"); },
  });
  let ran = 0;
  schedule((...parameters) => { ran += parameters.length === 0 ? 1 : 100; });
  assert.equal(requests.length, 1);
  assert.deepEqual(requests[0][1], []);
  // The browser passes its deadline, which the collection does not take.
  requests[0][0]({ didTimeout: false, timeRemaining: () => 10 });
  assert.equal(ran, 1);
});

test("falls back to a short timer in a page without an idle callback", () => {
  const timers = [];
  const schedule = selectIdleScheduler({ document: {}, setTimeout(callback, delay) { timers.push([callback, delay]); return 3; } });
  let ran = 0;
  schedule((...parameters) => { ran += parameters.length === 0 ? 1 : 100; });
  assert.equal(timers.length, 1);
  assert.equal(timers[0][1], 200);
  timers[0][0]("timer argument");
  assert.equal(ran, 1);
});

test("uses a short timer in a worker and in Node.js, where it does not keep the process alive", () => {
  const timers = [];
  let released = 0;
  // A worker's timer is a number; Node.js's can be released.
  const worker = selectIdleScheduler({ setTimeout(callback, delay) { timers.push([callback, delay]); return 7; } });
  const node = selectIdleScheduler({
    document: null,
    setTimeout(callback, delay) {
      timers.push([callback, delay]);
      return { unref() { released++; } };
    },
  });
  let ran = 0;
  worker(() => { ran++; });
  node(() => { ran++; });
  assert.deepEqual(timers.map(timer => timer[1]), [200, 200]);
  assert.equal(released, 1);
  for (const [callback] of timers) callback("timer argument");
  assert.equal(ran, 2);
});

test("has no scheduler for a host without an idle callback or a timer", () => {
  assert.equal(selectIdleScheduler({ document: {} }), null);
  assert.equal(selectIdleScheduler({}), null);
  for (const scope of [null, undefined, "window"]) {
    assert.throws(() => selectIdleScheduler(scope), /scope is invalid/u);
  }
});

test("offers a host's timer and clock for the quiet collection", () => {
  const timers = [];
  let released = 0;
  const timer = selectQuietTimer({
    performance: { now: () => 42.5 },
    setTimeout(callback, delay) {
      timers.push([callback, delay]);
      return timers.length === 1 ? 7 : { unref() { released++; } };
    },
  });
  assert.equal(Object.isFrozen(timer), true);
  assert.equal(timer.now(), 42.5);
  let ran = 0;
  timer.setTimer((...parameters) => { ran += parameters.length === 0 ? 1 : 100; }, 1000);
  timer.setTimer(() => { ran++; }, 2500);
  assert.deepEqual(timers.map(entry => entry[1]), [1000, 2500]);
  // Only a timer that can keep a process alive is released, and a timer of null or without unref is left alone.
  assert.equal(released, 1);
  timers[0][0]("timer argument");
  timers[1][0]();
  assert.equal(ran, 2);
  const plain = selectQuietTimer({ performance: { now: () => 1 }, setTimeout: () => null });
  plain.setTimer(() => {}, 1);
  const unreleasable = selectQuietTimer({ performance: { now: () => 1 }, setTimeout: () => ({}) });
  unreleasable.setTimer(() => {}, 1);
});

test("has no quiet timer for a host without a timer or a clock", () => {
  assert.equal(selectQuietTimer({ performance: { now() {} } }), null);
  assert.equal(selectQuietTimer({ setTimeout() {} }), null);
  assert.equal(selectQuietTimer({ performance: null, setTimeout() {} }), null);
  assert.equal(selectQuietTimer({ performance: "clock", setTimeout() {} }), null);
  assert.equal(selectQuietTimer({ performance: {}, setTimeout() {} }), null);
  for (const scope of [null, undefined, "window"]) {
    assert.throws(() => selectQuietTimer(scope), /scope is invalid/u);
  }
});

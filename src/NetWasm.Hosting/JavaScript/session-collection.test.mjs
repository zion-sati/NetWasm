// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createSessionCollection } from "./session-collection.mjs";

function host() {
  const state = { idle: [], idleAnswer: false, quiet: 0, timers: [] };
  state.options = {
    collectWhenIdle: () => state.idleAnswer,
    collectWhenQuiet: () => { state.quiet++; return 256; },
    quietTimer: { now: () => 0, setTimer: (run, milliseconds) => state.timers.push({ milliseconds, run }) },
    requestIdle: run => state.idle.push(run),
  };
  return state;
}

test("asks for the next idle period and times the quiet after each run of the module", () => {
  const state = host();
  const moduleRan = createSessionCollection(state.options);
  moduleRan();
  moduleRan();
  assert.equal(state.idle.length, 1);
  assert.deepEqual(state.timers.map(timer => timer.milliseconds), [1000]);

  // An idle collection that declined leaves the quiet one due.
  state.idle.shift()();
  state.timers.shift().run();
  state.timers.shift().run();
  assert.equal(state.quiet, 1);

  // One that collected leaves it nothing to do until the module runs again.
  moduleRan();
  state.idleAnswer = true;
  state.idle.shift()();
  state.timers.shift().run();
  assert.equal(state.quiet, 1);
  moduleRan();
  state.timers.shift().run();
  assert.equal(state.quiet, 2);
});

test("uses only what the runtime and the host both offer", () => {
  // No idle scheduler: the quiet collection alone.
  const timed = host();
  const quietOnly = createSessionCollection({ ...timed.options, requestIdle: null });
  quietOnly();
  assert.equal(timed.idle.length, 0);
  timed.timers.shift().run();
  assert.equal(timed.quiet, 1);

  // No timer, or no quiet entry: the idle collection alone.
  for (const without of [{ quietTimer: null }, { collectWhenQuiet: null }]) {
    const state = host();
    const idleOnly = createSessionCollection({ ...state.options, ...without });
    idleOnly();
    assert.equal(state.timers.length, 0);
    state.idleAnswer = true;
    state.idle.shift()();
    assert.equal(state.idle.length, 0);
  }

  // Neither: a run of the module does nothing.
  const none = host();
  createSessionCollection({ ...none.options, collectWhenIdle: undefined, quietTimer: null })();
  createSessionCollection({ collectWhenIdle: null, collectWhenQuiet: null, quietTimer: null, requestIdle: null })();
  assert.equal(none.idle.length + none.timers.length, 0);
  for (const options of [null, "session"]) {
    assert.throws(() => createSessionCollection(options), /options are invalid/u);
  }
});

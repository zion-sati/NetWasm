// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createQuietCollection } from "./quiet-collection.mjs";

// A host whose timers the test runs out by hand and whose clock it moves.
function host(answers) {
  const state = { collections: 0, time: 0, timers: [], took: 0 };
  state.collection = createQuietCollection({
    collect() {
      state.collections++;
      state.time += state.took;
      return answers.shift();
    },
    now: () => state.time,
    setTimer: (run, milliseconds) => state.timers.push({ milliseconds, run }),
  });
  state.expire = () => state.timers.shift().run();
  return state;
}

test("collects once the module has stayed quiet for a whole period", () => {
  const state = host([256, 256]);
  assert.equal(Object.isFrozen(state.collection), true);

  // The first run sets the timer; later runs set none.
  state.collection.moduleRan();
  assert.deepEqual(state.timers.map(timer => timer.milliseconds), [1000]);
  state.collection.moduleRan();
  state.collection.moduleRan();
  assert.equal(state.timers.length, 1);

  // The module ran during the period, so the timer runs out another.
  state.expire();
  assert.equal(state.collections, 0);
  assert.deepEqual(state.timers.map(timer => timer.milliseconds), [1000]);

  // Nothing ran during that one: the collection follows, and no timer is left behind.
  state.expire();
  assert.equal(state.collections, 1);
  assert.equal(state.timers.length, 0);

  // The next stretch of activity gets its own.
  state.collection.moduleRan();
  assert.equal(state.timers.length, 1);
  state.expire();
  assert.equal(state.collections, 2);
});

test("leaves a stretch an idle collection already cleaned up after alone", () => {
  const state = host([256]);
  state.collection.moduleRan();
  state.collection.collected();
  state.expire();
  assert.equal(state.collections, 0);
  assert.equal(state.timers.length, 0);

  // The module ran again after that collection: the quiet one is due.
  state.collection.moduleRan();
  state.collection.collected();
  state.collection.moduleRan();
  state.expire();
  state.expire();
  assert.equal(state.collections, 1);
});

test("waits twice as long after a collection that freed little, and starts over after one that paid", () => {
  // 1 is a collection that freed nothing, 32 one short of an eighth, 33 an eighth, 256 everything.
  const state = host([1, 32, 33, 1, 1, 1, 1, 1, 1, 1, 256]);
  const periods = [];
  for (let round = 0; round < 11; round++) {
    state.collection.moduleRan();
    periods.push(state.timers[0].milliseconds);
    state.expire();
  }
  state.collection.moduleRan();
  periods.push(state.timers[0].milliseconds);
  assert.deepEqual(periods, [1000, 2000, 4000, 1000, 2000, 4000, 8000, 16000, 32000, 60000, 60000, 1000]);
  assert.equal(state.collections, 11);
});

test("rests many times as long as the last collection took", () => {
  const state = host([256, 256, 1, 256]);
  const next = took => {
    state.took = took;
    state.collection.moduleRan();
    state.expire();
    state.collection.moduleRan();
    const period = state.timers[0].milliseconds;
    state.expire();
    return period;
  };
  // A paid collection of 10 ms keeps the shortest period; one of 150 ms rests 3 s.
  assert.equal(next(10), 1000);
  state.took = 150;
  state.collection.moduleRan();
  state.expire();
  state.took = 0;
  state.collection.moduleRan();
  assert.equal(state.timers[0].milliseconds, 3000);
  state.expire();
  // One that freed nothing and took 10 s rests the longest period, not 200 s.
  state.took = 10000;
  state.collection.moduleRan();
  state.expire();
  state.collection.moduleRan();
  assert.equal(state.timers[0].milliseconds, 60000);
});

test("keeps its period when the runtime did not collect", () => {
  const state = host([1, 0, 256]);
  state.collection.moduleRan();
  state.expire();
  state.collection.moduleRan();
  assert.equal(state.timers[0].milliseconds, 2000);
  state.expire();
  state.collection.moduleRan();
  assert.equal(state.timers[0].milliseconds, 2000);
  assert.equal(state.collections, 2);
});

test("requires a collect action, a timer and a clock", () => {
  const complete = { collect() {}, now() {}, setTimer() {} };
  for (const options of [null, "quiet", {}, { ...complete, collect: 1 }, { ...complete, now: 1 },
    { ...complete, setTimer: undefined }]) {
    assert.throws(() => createQuietCollection(options), /requires a collect action, a timer and a clock/u);
  }
});

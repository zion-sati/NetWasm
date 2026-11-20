// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createIdleCollection } from "./idle-collection.mjs";

test("collects once in the next idle period, however often the module ran before it", () => {
  const idle = [];
  let collections = 0;
  const collection = createIdleCollection({ collect: () => { collections++; }, requestIdle: run => idle.push(run) });
  assert.equal(Object.isFrozen(collection), true);

  collection.request();
  collection.request();
  assert.equal(idle.length, 1);
  assert.equal(collections, 0);
  idle.shift()();
  assert.equal(collections, 1);

  // The module ran again: the next idle period is asked for anew.
  collection.request();
  assert.equal(idle.length, 1);
  idle.shift()();
  assert.equal(collections, 2);
});

test("requires a collect action and an idle scheduler", () => {
  for (const options of [null, "idle", {}, { collect() {} }, { requestIdle() {} },
    { collect: 1, requestIdle() {} }, { collect() {}, requestIdle: 1 }]) {
    assert.throws(() => createIdleCollection(options), /requires a collect action and an idle scheduler/u);
  }
});

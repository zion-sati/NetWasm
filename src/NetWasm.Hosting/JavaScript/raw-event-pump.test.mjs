import assert from "node:assert/strict";
import test from "node:test";

import { createRawComponentModel } from "./raw-component-model.mjs";
import { createRawEventPump } from "./raw-event-pump.mjs";

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

function instance() {
  const memory = new WebAssembly.Memory({ initial: 1 });
  return createRawComponentModel({ memory: () => memory });
}

test("runs the loop and drives a wait until the loop exits", async () => {
  const componentModel = instance();
  const set = componentModel.root["[waitable-set-new]"]();
  let finish;
  const subtask = componentModel.callAsync(() => new Promise(resolve => { finish = resolve; }), () => {}) >> 4;
  componentModel.root["[waitable-join]"](subtask, set);
  const calls = [];
  const pump = createRawEventPump({
    componentModel,
    run() { calls.push("run"); return 2 | (set << 4); },
    callback(...event) { calls.push(["callback", ...event]); return 0; },
    onFailure: assert.fail,
  });
  assert.equal(Object.isFrozen(pump), true);
  pump.turn();
  assert.deepEqual(calls, ["run"]);
  finish();
  await settle();
  assert.deepEqual(calls, ["run", ["callback", 1, subtask, 2]]);
});

test("a run with nothing pending exits without driving", () => {
  let runs = 0;
  const pump = createRawEventPump({
    componentModel: { drive: assert.fail, wake: assert.fail },
    run() { runs++; return 0; },
    callback: assert.fail,
    onFailure: assert.fail,
  });
  pump.turn();
  pump.turn();
  assert.equal(runs, 2);
});

test("a call while the loop waits gives it a turn, then runs the loop again", async () => {
  const componentModel = instance();
  const set = componentModel.root["[waitable-set-new]"]();
  const calls = [];
  const codes = [0];
  const pump = createRawEventPump({
    componentModel,
    run() { calls.push("run"); return calls.length === 1 ? 2 | (set << 4) : 0; },
    callback(...event) { calls.push(["callback", ...event]); return codes.shift(); },
    onFailure: assert.fail,
  });
  pump.turn();
  pump.turn();
  await settle();
  assert.deepEqual(calls, ["run", ["callback", 0, 0, 0], "run"]);
});

test("a failing run or loop fails once, and a stopped pump runs nothing", async () => {
  const failures = [];
  const broken = createRawEventPump({
    componentModel: { drive() {}, wake() {} },
    run() { throw new Error("trap"); },
    callback() { return 0; },
    onFailure: error => failures.push(error.message),
  });
  broken.turn();
  broken.turn();
  assert.deepEqual(failures, ["trap"]);

  const componentModel = instance();
  const set = componentModel.root["[waitable-set-new]"]();
  const failing = createRawEventPump({
    componentModel,
    run: () => 2 | (set << 4),
    callback() { throw new Error("callback trap"); },
    onFailure: error => failures.push(error.message),
  });
  failing.turn();
  componentModel.wake();
  await settle();
  assert.deepEqual(failures, ["trap", "callback trap"]);

  let runs = 0;
  const stopped = createRawEventPump({
    componentModel: { drive() {}, wake() {} },
    run() { runs++; return 0; },
    callback() { return 0; },
    onFailure: assert.fail,
  });
  stopped.stop();
  stopped.turn();
  assert.equal(runs, 0);

  // A loop whose instance closes after the pump stopped ends quietly.
  const closing = instance();
  const closingSet = closing.root["[waitable-set-new]"]();
  const quiet = createRawEventPump({
    componentModel: closing,
    run: () => 2 | (closingSet << 4),
    callback() { return 0; },
    onFailure: assert.fail,
  });
  quiet.turn();
  quiet.stop();
  closing.close();
  await settle();
});

test("rejects an incomplete request", () => {
  const valid = {
    componentModel: { drive() {}, wake() {} },
    run() { return 0; },
    callback() { return 0; },
    onFailure() {},
  };
  for (const request of [null, 1, { ...valid, componentModel: null }, { ...valid, componentModel: 1 },
    { ...valid, componentModel: { wake() {} } }, { ...valid, componentModel: { drive() {} } },
    { ...valid, run: null }, { ...valid, callback: null }, { ...valid, onFailure: null }]) {
    assert.throws(() => createRawEventPump(request), /raw event pump request is invalid/);
  }
});

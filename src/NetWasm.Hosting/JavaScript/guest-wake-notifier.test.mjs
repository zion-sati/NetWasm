import assert from "node:assert/strict";
import test from "node:test";
import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";

test("wakes the guest before requesting process observation", () => {
  const calls = [];
  const notifier = createGuestWakeNotifier({
    guestWake(token) { calls.push(`guest:${token}`); },
    observeWake(error) { calls.push(error === undefined ? "observe" : "failure"); },
  });
  notifier.notify(1);
  notifier.notify(0xffffffff);
  assert.deepEqual(calls, ["guest:1", "observe", "guest:4294967295", "observe"]);
  assert.equal(Object.isFrozen(notifier), true);
});

test("turns guest wake failure into an opaque observation failure", () => {
  const privateFailure = new Error("private guest detail");
  const observed = [];
  const notifier = createGuestWakeNotifier({
    guestWake() { throw privateFailure; },
    observeWake(error) { observed.push(error); },
  });
  assert.doesNotThrow(() => notifier.notify(7));
  assert.equal(observed.length, 1);
  assert.notEqual(observed[0], privateFailure);
  assert.equal(Object.isFrozen(observed[0]), true);
});

test("validates dependencies and token contract", () => {
  assert.throws(() => createGuestWakeNotifier(), TypeError);
  assert.throws(() => createGuestWakeNotifier({ guestWake() {} }), TypeError);
  const notifier = createGuestWakeNotifier({ guestWake() {}, observeWake() {} });
  for (const token of [0, -1, 1.5, 0x100000000]) {
    assert.throws(() => notifier.notify(token), TypeError);
  }
});

import assert from "node:assert/strict";
import test from "node:test";
import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { observeManagedProcess } from "./managed-process-observer.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";

function composeLifecycle({ block }) {
  const calls = [];
  const jobs = [];
  let observeWake;
  let processStatus = 0;
  const notifier = createGuestWakeNotifier({
    guestWake(token) {
      calls.push(`guestWake:${token}`);
      processStatus = 1;
    },
    observeWake(error) { observeWake(error); },
  });
  const reactor = createPollableReactor({
    onReady: token => notifier.notify(token),
    onFailure: () => observeWake(Object.freeze({})),
    schedule: callback => jobs.push(callback),
  });
  const pollable = {
    block() {
      calls.push("block");
      return block();
    },
    dispose() { calls.push("dispose"); },
  };
  const process = {
    start() {
      calls.push("start");
      reactor.watch(pollable, 13);
      return 7;
    },
    status() {
      calls.push("status");
      return processStatus;
    },
    exitCode() {
      calls.push("exitCode");
      return 41;
    },
    complete() { calls.push("complete"); },
  };
  const result = observeManagedProcess({
    process,
    subscribeWake(observer) {
      observeWake = observer;
      return () => {
        observeWake = () => {};
        calls.push("unsubscribe");
      };
    },
  });
  return { calls, jobs, reactor, result };
}

test("readiness disposes before guest wake and process re-observation", async () => {
  const lifecycle = composeLifecycle({ block: () => Promise.resolve() });
  assert.deepEqual(lifecycle.calls, ["start", "status"]);
  lifecycle.jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(await lifecycle.result, {
    schemaVersion: 1,
    completionKind: "normal",
    exitCode: 41,
    primaryFailure: null,
    cleanupFailures: [],
  });
  assert.deepEqual(lifecycle.calls, [
    "start",
    "status",
    "block",
    "dispose",
    "guestWake:13",
    "status",
    "exitCode",
    "unsubscribe",
    "complete",
  ]);
  lifecycle.reactor.close();
});

test("pollable failure terminates observation without guest wake", async () => {
  const lifecycle = composeLifecycle({
    block: () => Promise.reject(new Error("private pollable detail")),
  });
  lifecycle.jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  const result = await lifecycle.result;
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.process-wake");
  assert.doesNotMatch(JSON.stringify(result), /private/);
  assert.equal(lifecycle.calls.some(call => call.startsWith("guestWake:")), false);
  assert.deepEqual(lifecycle.calls.slice(-3), ["dispose", "unsubscribe", "complete"]);
  lifecycle.reactor.close();
});

import assert from "node:assert/strict";
import test from "node:test";
import { observeManagedProcess } from "./managed-process-observer.mjs";

function fixture({ status = 0, exitCode = 0 } = {}) {
  const calls = [];
  let wake;
  const process = {
    start() {
      calls.push("start");
      return 7;
    },
    status(handle) {
      assert.equal(handle, 7);
      calls.push("status");
      return status;
    },
    exitCode(handle) {
      assert.equal(handle, 7);
      calls.push("exitCode");
      return exitCode;
    },
    complete(handle) {
      assert.equal(handle, 7);
      calls.push("complete");
    },
  };
  return {
    calls,
    process,
    subscribeWake(observer) {
      wake = observer;
      return () => calls.push("unsubscribe");
    },
    wake(error) {
      wake(error);
    },
    setStatus(value) {
      status = value;
    },
  };
}

function assertFailure(result, completionKind, phase, code) {
  assert.equal(result.schemaVersion, 1);
  assert.equal(result.completionKind, completionKind);
  assert.equal(result.exitCode, null);
  assert.equal(result.primaryFailure.phase, phase);
  assert.equal(result.primaryFailure.code, code);
  assert.equal(typeof result.primaryFailure.message, "string");
  assert.ok(result.primaryFailure.message.length > 0);
}

test("observes immediate and deferred signed exit codes and releases once", async () => {
  for (const exitCode of [0, 19, -7]) {
    const immediate = fixture({ status: 1, exitCode });
    const immediateResult = await observeManagedProcess(immediate);
    assert.deepEqual(immediateResult, {
      schemaVersion: 1,
      completionKind: "normal",
      exitCode,
      primaryFailure: null,
      cleanupFailures: [],
    });
    assert.deepEqual(immediate.calls,
      ["start", "status", "exitCode", "unsubscribe", "complete"]);

    const deferred = fixture({ exitCode });
    const result = observeManagedProcess(deferred);
    assert.deepEqual(deferred.calls, ["start", "status"]);
    await Promise.resolve();
    assert.deepEqual(deferred.calls, ["start", "status"]);
    deferred.wake();
    deferred.setStatus(1);
    deferred.wake();
    assert.equal((await result).exitCode, exitCode);
    deferred.wake();
    assert.deepEqual(deferred.calls,
      ["start", "status", "status", "status", "exitCode", "unsubscribe", "complete"]);
  }
});

test("maps managed fault, managed cancellation, invalid status, and wake failure", async () => {
  const cases = [
    [2, "managedFailure", "execution", "managed.failure"],
    [3, "managedCancellation", "execution", "managed.cancelled"],
    [99, "contractFailure", "observation", "contract.process-status"],
  ];
  for (const [status, kind, phase, code] of cases) {
    const guest = fixture({ status });
    assertFailure(await observeManagedProcess(guest), kind, phase, code);
    assert.deepEqual(guest.calls, ["start", "status", "unsubscribe", "complete"]);
  }

  const guest = fixture();
  const result = observeManagedProcess(guest);
  guest.wake(new Error("private wake detail"));
  assertFailure(await result, "hostFailure", "observation", "host.process-wake");
  assert.deepEqual(guest.calls, ["start", "status", "unsubscribe", "complete"]);
});

test("caller cancellation prevents start or closes active observation", async () => {
  const already = new AbortController();
  already.abort();
  const untouched = fixture();
  assertFailure(
    await observeManagedProcess({ ...untouched, signal: already.signal }),
    "callerCancellation",
    "observation",
    "caller.cancelled");
  assert.deepEqual(untouched.calls, []);

  const controller = new AbortController();
  const guest = fixture();
  const result = observeManagedProcess({ ...guest, signal: controller.signal });
  controller.abort();
  assertFailure(await result, "callerCancellation", "observation", "caller.cancelled");
  guest.setStatus(1);
  guest.wake();
  assert.deepEqual(guest.calls, ["start", "status", "unsubscribe", "complete"]);

  const duringStart = new AbortController();
  const started = fixture();
  started.process.start = () => {
    duringStart.abort();
    return 7;
  };
  assertFailure(
    await observeManagedProcess({ ...started, signal: duringStart.signal }),
    "callerCancellation",
    "observation",
    "caller.cancelled");
  assert.deepEqual(started.calls, ["unsubscribe", "complete"]);
});

test("rejects invalid dependencies before subscribing or starting", () => {
  assert.throws(() => observeManagedProcess(), TypeError);
  assert.throws(() => observeManagedProcess(null), TypeError);
  assert.throws(() => observeManagedProcess(1), TypeError);
  for (const operation of ["start", "status", "exitCode", "complete"]) {
    const guest = fixture();
    guest.process[operation] = null;
    assert.throws(() => observeManagedProcess(guest), TypeError);
    assert.deepEqual(guest.calls, []);
  }
  assert.throws(() => observeManagedProcess({ process: null }), TypeError);
  assert.throws(() => observeManagedProcess({ ...fixture(), subscribeWake: null }), TypeError);
  for (const signal of [{}, { aborted: false }, { aborted: false, addEventListener() {} }]) {
    const guest = fixture();
    assert.throws(() => observeManagedProcess({ ...guest, signal }), TypeError);
    assert.deepEqual(guest.calls, []);
  }
});

test("maps invalid handles and exit codes without releasing invalid ownership", async () => {
  for (const handle of [0, -1, 0x100000000, 1.5]) {
    const guest = fixture();
    guest.process.start = () => handle;
    assertFailure(
      await observeManagedProcess(guest),
      "contractFailure",
      "execution",
      "contract.process-handle");
    assert.deepEqual(guest.calls, ["unsubscribe"]);
  }
  for (const exitCode of [NaN, 1.5, -0x80000001, 0x80000000]) {
    const guest = fixture({ status: 1, exitCode });
    assertFailure(
      await observeManagedProcess(guest),
      "contractFailure",
      "observation",
      "contract.process-exit-code");
    assert.deepEqual(guest.calls,
      ["start", "status", "exitCode", "unsubscribe", "complete"]);
  }
});

test("maps host operation failures without exposing exception text", async () => {
  const operations = [
    ["start", "hostFailure", "execution", "host.process-start"],
    ["status", "hostFailure", "observation", "host.process-status"],
    ["exitCode", "hostFailure", "observation", "host.process-exit-code"],
  ];
  for (const [operation, kind, phase, code] of operations) {
    const guest = fixture({ status: 1 });
    guest.process[operation] = () => {
      throw new Error(`private ${operation} detail`);
    };
    const result = await observeManagedProcess(guest);
    assertFailure(result, kind, phase, code);
    assert.doesNotMatch(result.primaryFailure.message, /private/);
    assert.equal(guest.calls.includes("unsubscribe"), true);
    assert.equal(guest.calls.includes("complete"), operation !== "start");
  }

  const subscription = fixture();
  const subscriptionResult = await observeManagedProcess({
    ...subscription,
    subscribeWake() {
      throw new Error("private subscription detail");
    },
  });
  assertFailure(
    subscriptionResult,
    "hostFailure",
    "observation",
    "host.process-subscription");
  assert.deepEqual(subscription.calls, []);

  const invalidSubscription = fixture();
  const invalidResult = await observeManagedProcess({
    ...invalidSubscription,
    subscribeWake() {
      return null;
    },
  });
  assertFailure(
    invalidResult,
    "contractFailure",
    "observation",
    "contract.process-subscription");
  assert.deepEqual(invalidSubscription.calls, []);
});

test("promotes cleanup-only failure and preserves later cleanup in order", async () => {
  const first = fixture({ status: 1 });
  first.process.complete = () => {
    throw new Error("private complete detail");
  };
  const firstResult = await observeManagedProcess({
    ...first,
    subscribeWake(observer) {
      first.wake = observer;
      return () => {
        throw new Error("private unsubscribe detail");
      };
    },
  });
  assertFailure(firstResult, "hostFailure", "cleanup", "host.process-unsubscribe");
  assert.deepEqual(firstResult.cleanupFailures.map(failure => failure.code),
    ["host.process-complete"]);

  const controller = new AbortController();
  const second = fixture({ status: 2 });
  second.process.complete = () => {
    throw new Error("private complete detail");
  };
  const originalRemove = controller.signal.removeEventListener.bind(controller.signal);
  controller.signal.removeEventListener = () => {
    originalRemove("abort", () => {});
    throw new Error("private listener detail");
  };
  const secondResult = await observeManagedProcess({
    ...second,
    signal: controller.signal,
    subscribeWake() {
      return () => {
        throw new Error("private unsubscribe detail");
      };
    },
  });
  assertFailure(secondResult, "managedFailure", "execution", "managed.failure");
  assert.deepEqual(secondResult.cleanupFailures.map(failure => failure.code), [
    "host.process-unsubscribe",
    "host.process-abort-listener",
    "host.process-complete",
  ]);

  const cleanupAbort = new AbortController();
  const reentrant = fixture({ status: 1, exitCode: 31 });
  const reentrantResult = await observeManagedProcess({
    ...reentrant,
    signal: cleanupAbort.signal,
    subscribeWake() {
      return () => cleanupAbort.abort();
    },
  });
  assert.equal(reentrantResult.completionKind, "normal");
  assert.equal(reentrantResult.exitCode, 31);
  assert.equal(reentrant.calls.filter(call => call === "complete").length, 1);
});

test("maps abort-listener registration failure and still releases ownership", async () => {
  const guest = fixture();
  const signal = {
    aborted: false,
    addEventListener() {
      throw new Error("private listener detail");
    },
    removeEventListener() {},
  };
  const result = await observeManagedProcess({ ...guest, signal });
  assertFailure(result, "hostFailure", "observation", "host.process-abort-listener");
  assert.deepEqual(guest.calls, ["start", "unsubscribe", "complete"]);
});

test("retains early and reentrant notifications without duplicate observation", async () => {
  const early = fixture({ status: 1, exitCode: 23 });
  const earlyResult = await observeManagedProcess({
    ...early,
    subscribeWake(observer) {
      observer();
      return () => early.calls.push("unsubscribe");
    },
  });
  assert.equal(earlyResult.exitCode, 23);
  assert.deepEqual(early.calls,
    ["start", "status", "exitCode", "unsubscribe", "complete"]);

  const reentrant = fixture({ exitCode: 29 });
  const originalStatus = reentrant.process.status;
  let first = true;
  reentrant.process.status = handle => {
    const status = originalStatus(handle);
    if (first) {
      first = false;
      reentrant.setStatus(1);
      reentrant.wake();
    }
    return status;
  };
  assert.equal((await observeManagedProcess(reentrant)).exitCode, 29);
  assert.deepEqual(reentrant.calls,
    ["start", "status", "status", "exitCode", "unsubscribe", "complete"]);
});

test("retains early and reentrant wake failures", async () => {
  const early = fixture();
  const earlyResult = await observeManagedProcess({
    ...early,
    subscribeWake(observer) {
      observer(new Error("private early detail"));
      return () => early.calls.push("unsubscribe");
    },
  });
  assertFailure(earlyResult, "hostFailure", "observation", "host.process-wake");
  assert.deepEqual(early.calls, ["unsubscribe"]);

  const duringStatus = fixture({ status: 1 });
  const originalStatus = duringStatus.process.status;
  duringStatus.process.status = handle => {
    const status = originalStatus(handle);
    duringStatus.wake(new Error("private status detail"));
    return status;
  };
  const statusResult = await observeManagedProcess(duringStatus);
  assertFailure(statusResult, "hostFailure", "observation", "host.process-wake");
  assert.deepEqual(duringStatus.calls,
    ["start", "status", "unsubscribe", "complete"]);

  const duringExit = fixture({ status: 1 });
  const originalExitCode = duringExit.process.exitCode;
  duringExit.process.exitCode = handle => {
    const exitCode = originalExitCode(handle);
    duringExit.wake(new Error("private exit detail"));
    return exitCode;
  };
  const exitResult = await observeManagedProcess(duringExit);
  assertFailure(exitResult, "hostFailure", "observation", "host.process-wake");
  assert.deepEqual(duringExit.calls,
    ["start", "status", "exitCode", "unsubscribe", "complete"]);
});

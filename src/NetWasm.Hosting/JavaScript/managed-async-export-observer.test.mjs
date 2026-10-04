import assert from "node:assert/strict";
import test from "node:test";
import { observeManagedAsyncExport } from "./managed-async-export-observer.mjs";
import { NetWasmHostError, NetWasmManagedError } from "./managed-errors.mjs";

function fixture(overrides = {}) {
  const pending = new Map();
  const events = [];
  let token = 0;
  let abort;
  let status = 0;
  const request = {
    name: "work",
    readStatus() { events.push("status"); return status; },
    readResult() { events.push("result"); return 42; },
    complete() { events.push("complete"); },
    schedule(callback, delay) {
      assert.equal(delay, 0);
      pending.set(++token, callback);
      events.push("schedule");
      return token;
    },
    cancelScheduled(id) { pending.delete(id); events.push("cancel"); },
    registerAbort(callback) {
      abort = callback;
      return () => { events.push("unregister"); };
    },
    ...overrides,
  };
  return {
    request, events, pending,
    abort(reason) { abort(reason); },
    setStatus(value) { status = value; },
    tick() {
      const [id, callback] = pending.entries().next().value;
      pending.delete(id);
      callback();
    },
  };
}

test("retains polling until actual completion and releases before promise settlement", async () => {
  const f = fixture();
  const promise = observeManagedAsyncExport(f.request);
  f.tick();
  assert.equal(f.pending.size, 1);
  assert.equal(f.events.includes("complete"), false);
  f.setStatus(1);
  f.tick();
  assert.equal(await promise, 42);
  assert.deepEqual(f.events.slice(-4), ["status", "result", "unregister", "complete"]);
  assert.equal(f.pending.size, 0);
  f.abort(new Error("late abort"));
  assert.equal(f.events.filter(x => x === "complete").length, 1);
});

test("terminal close cancels polling and stale timer callbacks cannot call the guest", async () => {
  const f = fixture();
  const promise = observeManagedAsyncExport(f.request);
  const staleCallback = [...f.pending.values()][0];
  const reason = new NetWasmHostError("closed");
  const rejected = assert.rejects(promise, error => error === reason);
  f.abort(reason);
  await rejected;
  assert.equal(f.pending.size, 0);
  assert.deepEqual(f.events.slice(-3), ["cancel", "unregister", "complete"]);
  const before = [...f.events];
  staleCallback();
  f.abort(reason);
  assert.deepEqual(f.events, before);
});

test("maps guest failure, cancellation and invalid statuses without reading results", async () => {
  for (const [status, type] of [[2, NetWasmManagedError], [3, DOMException], [9, NetWasmHostError]]) {
    const f = fixture({ readStatus: () => status });
    await assert.rejects(observeManagedAsyncExport(f.request), type);
    assert.deepEqual(f.events.slice(-2), ["unregister", "complete"]);
  }
});

test("fault completion returns the original call payload after releasing ownership", async () => {
  const managed = { typeId: 7, typeName: "System.FormatException", message: "original 🌏", stackTrace: "managed trace" };
  let resolveDetails;
  const f = fixture({ readStatus: () => 2, complete() {
    f.events.push("complete");
    return new Promise(resolve => { resolveDetails = resolve; });
  } });
  const promise = observeManagedAsyncExport(f.request);
  assert.deepEqual(f.events, ["unregister", "complete"]);
  f.abort(new NetWasmHostError("late abort"));
  resolveDetails(managed);
  await assert.rejects(promise, error => error instanceof NetWasmManagedError
    && error.message === managed.message && error.managedType === 7
    && error.managed.stackTrace === managed.stackTrace);
  assert.deepEqual(f.events, ["unregister", "complete"]);
});

test("caller cancellation keeps precedence over a concurrently captured task fault", async () => {
  const managed = { typeId: 7, typeName: null, message: "fault", stackTrace: null };
  const f = fixture({ complete: () => Promise.resolve(managed) });
  const promise = observeManagedAsyncExport(f.request);
  const reason = new NetWasmHostError("closed");
  f.abort(reason);
  await assert.rejects(promise, error => error === reason);
});

test("failed payload resolution preserves the original failure and reports cleanup failure", async () => {
  const cause = new Error("payload lookup failed");
  const f = fixture({ readStatus: () => 2, complete: () => Promise.reject(cause) });
  await assert.rejects(observeManagedAsyncExport(f.request), error =>
    error instanceof NetWasmManagedError && error.cause instanceof AggregateError
    && error.cause.errors[0] instanceof NetWasmManagedError && error.cause.errors[1] === cause);
});

test("preserves traps and wraps status, result and scheduler failures", async () => {
  const trap = new WebAssembly.RuntimeError("trap");
  for (const [overrides, cause, preserve] of [
    [{ readStatus() { throw trap; } }, trap, true],
    [{ readStatus() { throw new Error("status"); } }, null, false],
    [{ readStatus: () => 1, readResult() { throw new Error("result"); } }, null, false],
    [{ schedule() { throw new Error("scheduler"); } }, null, false],
  ]) {
    const f = fixture(overrides);
    await assert.rejects(observeManagedAsyncExport(f.request), error => preserve
      ? error === cause : error instanceof NetWasmManagedError);
    assert.deepEqual(f.events.slice(-2), ["unregister", "complete"]);
  }
});

test("contains every cleanup failure and still attempts all owned releases", async () => {
  for (const failureStage of ["cancel", "unregister", "complete"]) {
    const cause = new Error(failureStage);
    const f = fixture();
    if (failureStage === "cancel") f.request.cancelScheduled = () => { throw cause; };
    if (failureStage === "unregister") f.request.registerAbort = callback => {
      f.cancel = callback;
      return () => { throw cause; };
    };
    if (failureStage === "complete") f.request.complete = () => { throw cause; };
    const promise = observeManagedAsyncExport(f.request);
    const reason = new NetWasmHostError("closed");
    const rejected = assert.rejects(promise, error => error instanceof NetWasmManagedError
      && error.cause instanceof AggregateError
      && error.cause.errors[0] === reason && error.cause.errors[1] === cause);
    if (failureStage === "unregister") f.cancel(reason);
    else f.abort(reason);
    await rejected;
    if (failureStage !== "complete") assert.equal(f.events.includes("complete"), true);
  }

  const cause = new Error("completion");
  await assert.rejects(observeManagedAsyncExport({
    name: "work", readStatus: () => 1, readResult: () => 42,
    complete() { throw cause; },
  }), error => error.cause.errors.length === 1 && error.cause.errors[0] === cause);
});

test("handles synchronous abort registration without settling before its cleanup is owned", async () => {
  let statusReads = 0;
  const events = [];
  const reason = new NetWasmHostError("closed");
  await assert.rejects(observeManagedAsyncExport({
    name: "work",
    readStatus() { statusReads++; return 1; },
    readResult: () => 42,
    complete() { events.push("complete"); },
    registerAbort(callback) { callback(reason); return () => { events.push("unregister"); }; },
  }), error => error === reason);
  assert.equal(statusReads, 0);
  assert.deepEqual(events, ["unregister", "complete"]);
});

test("rejects invalid or failed registration and releases the invoked guest handle", async () => {
  for (const registerAbort of [() => null, () => { throw new Error("registration"); }]) {
    let completed = 0;
    await assert.rejects(observeManagedAsyncExport({
      name: "work", readStatus: () => 1, readResult: () => 42,
      complete() { completed++; }, registerAbort,
    }), NetWasmManagedError);
    assert.equal(completed, 1);
  }
});

test("rejects malformed actions before acquiring operation ownership", () => {
  const valid = { name: "work", readStatus: () => 1, readResult: () => 42, complete() {} };
  for (const name of [null, ""]) {
    assert.throws(() => observeManagedAsyncExport({ ...valid, name }), TypeError);
  }
  for (const key of ["readStatus", "readResult", "complete", "schedule", "cancelScheduled", "registerAbort"]) {
    assert.throws(() => observeManagedAsyncExport({ ...valid, [key]: null }), TypeError);
  }
});

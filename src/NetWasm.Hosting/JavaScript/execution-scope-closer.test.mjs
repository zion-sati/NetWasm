import assert from "node:assert/strict";
import test from "node:test";
import {
  closeExecutionScope,
  createExecutionScopeCloser,
} from "./execution-scope-closer.mjs";
import {
  failedExecutionResult,
  normalExecutionResult,
} from "./execution-result.mjs";

const release = (code, action) => ({
  code,
  message: `The execution host could not complete ${code}.`,
  release: action,
});

test("returns an unchanged result for an empty scope", async () => {
  const outcome = normalExecutionResult(0);
  assert.equal(await closeExecutionScope(outcome), outcome);
  assert.equal(await closeExecutionScope(outcome, []), outcome);
});

test("validates the complete scope before releasing anything", async () => {
  const calls = [];
  const outcome = normalExecutionResult(0);
  await assert.rejects(() => closeExecutionScope(null), TypeError);
  await assert.rejects(() => closeExecutionScope(outcome, null), TypeError);
  for (const invalid of [null, 1, {}, { release: null }]) {
    await assert.rejects(() => closeExecutionScope(outcome, [
      release("host.first", () => calls.push("first")),
      invalid,
    ]), TypeError);
    assert.deepEqual(calls, []);
  }
  await assert.rejects(() => closeExecutionScope(outcome, [
    release("Host.invalid", () => calls.push("invalid")),
  ]), TypeError);
  assert.deepEqual(calls, []);
});

test("prepares a fail-first exactly-once scope closer", async () => {
  const calls = [];
  assert.throws(() => createExecutionScopeCloser(null), TypeError);
  assert.throws(() => createExecutionScopeCloser([
    release("host.first", () => calls.push("first")),
    null,
  ]), TypeError);
  assert.deepEqual(calls, []);

  const close = createExecutionScopeCloser([
    release("host.first", () => calls.push("first")),
  ]);
  assert.equal(Object.isFrozen(close), true);
  await assert.rejects(() => close(null), TypeError);
  assert.deepEqual(calls, []);
  const outcome = normalExecutionResult(3);
  assert.equal(await close(outcome), outcome);
  assert.deepEqual(calls, ["first"]);
  await assert.rejects(() => close(normalExecutionResult(4)), /already closed/);
  assert.deepEqual(calls, ["first"]);
});

test("attempts every synchronous and asynchronous release in order", async () => {
  const calls = [];
  const outcome = await closeExecutionScope(normalExecutionResult(7), [
    release("host.first", () => calls.push("first")),
    release("host.second", async () => {
      await Promise.resolve();
      calls.push("second");
      throw new Error("private second detail");
    }),
    release("host.third", () => {
      calls.push("third");
      throw new Error("private third detail");
    }),
    release("host.fourth", () => calls.push("fourth")),
  ]);
  assert.deepEqual(calls, ["first", "second", "third", "fourth"]);
  assert.equal(outcome.completionKind, "hostFailure");
  assert.equal(outcome.exitCode, null);
  assert.equal(outcome.primaryFailure.code, "host.second");
  assert.deepEqual(outcome.cleanupFailures.map(failure => failure.code), ["host.third"]);
  assert.doesNotMatch(JSON.stringify(outcome), /private/);
});

test("appends cleanup failures after an existing terminal result", async () => {
  const outcome = failedExecutionResult(
    "managedFailure",
    "execution",
    "managed.failure",
    "Managed execution failed.");
  const closed = await closeExecutionScope(outcome, [
    release("host.first", () => { throw new Error("private"); }),
    release("host.second", () => { throw new Error("private"); }),
  ]);
  assert.equal(closed.completionKind, "managedFailure");
  assert.equal(closed.primaryFailure.code, "managed.failure");
  assert.deepEqual(closed.cleanupFailures.map(failure => failure.code), [
    "host.first",
    "host.second",
  ]);
});

test("preserves an explicit output phase and rejects other release phases", async () => {
  const outcome = await closeExecutionScope(normalExecutionResult(0), [{
    phase: "output",
    code: "host.output",
    message: "The execution host could not finalize output.",
    release() { throw new Error("private"); },
  }]);
  assert.equal(outcome.primaryFailure.phase, "output");
  await assert.rejects(() => closeExecutionScope(normalExecutionResult(0), [{
    phase: "execution",
    code: "host.invalid",
    message: "The execution host failed.",
    release() {},
  }]), /phase/);
});

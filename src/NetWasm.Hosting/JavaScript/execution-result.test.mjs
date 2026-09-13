import assert from "node:assert/strict";
import test from "node:test";
import {
  appendCleanupFailures,
  assertExecutionResult,
  executionFailure,
  failedExecutionResult,
  normalExecutionResult,
} from "./execution-result.mjs";

const cleanup = () => executionFailure(
  "cleanup",
  "host.release",
  "The execution host could not release a resource.");

test("creates frozen transport-stable normal and failed results", () => {
  for (const exitCode of [-0x80000000, -1, 0, 0x7fffffff]) {
    const result = normalExecutionResult(exitCode);
    assert.doesNotThrow(() => assertExecutionResult(result));
    assert.equal(Object.isFrozen(result), true);
    assert.equal(Object.isFrozen(result.cleanupFailures), true);
  }

  for (const completionKind of [
    "managedFailure",
    "managedCancellation",
    "callerCancellation",
    "contractFailure",
    "hostFailure",
  ]) {
    const result = failedExecutionResult(
      completionKind,
      "execution",
      "managed.failure",
      "Managed execution failed.");
    assert.doesNotThrow(() => assertExecutionResult(result));
    assert.equal(Object.isFrozen(result.primaryFailure), true);
  }
});

test("promotes cleanup after normal and appends cleanup after failure", () => {
  const first = cleanup();
  const second = executionFailure(
    "output",
    "host.output-flush",
    "The execution host could not flush output.");
  const normal = normalExecutionResult(0);
  assert.equal(appendCleanupFailures(normal, []), normal);
  assert.deepEqual(appendCleanupFailures(normal, [first, second]), {
    schemaVersion: 1,
    completionKind: "hostFailure",
    exitCode: null,
    primaryFailure: first,
    cleanupFailures: [second],
  });

  const failed = appendCleanupFailures(
    failedExecutionResult(
      "managedFailure",
      "execution",
      "managed.failure",
      "Managed execution failed."),
    [first]);
  const appended = appendCleanupFailures(failed, [second]);
  assert.equal(appended.primaryFailure.code, "managed.failure");
  assert.deepEqual(appended.cleanupFailures, [first, second]);
});

test("rejects invalid result and failure shapes", () => {
  for (const value of [null, 1]) {
    assert.throws(() => assertExecutionResult(value), TypeError);
  }
  const valid = normalExecutionResult(0);
  for (const change of [
    { schemaVersion: 2 },
    { completionKind: "Normal" },
    { cleanupFailures: null },
  ]) {
    assert.throws(() => assertExecutionResult({ ...valid, ...change }), TypeError);
  }

  const badFailure = (phase, code, message) => ({ phase, code, message });
  for (const failure of [
    null,
    1,
    badFailure("unknown", "host.failure", "Host failure."),
    badFailure("execution", null, "Host failure."),
    badFailure("execution", "Host.failure", "Host failure."),
    badFailure("execution", "host.failure", null),
    badFailure("execution", "host.failure", ""),
    badFailure("execution", "host.failure", " Host failure."),
    badFailure("execution", "host.failure", "Host\nfailure."),
  ]) {
    assert.throws(() => assertExecutionResult({
      schemaVersion: 1,
      completionKind: "hostFailure",
      exitCode: null,
      primaryFailure: failure,
      cleanupFailures: [],
    }), TypeError);
  }
  assert.throws(() => executionFailure("invalid", "host.failure", "Host failure."), TypeError);
  assert.throws(() => failedExecutionResult(
    "invalid",
    "execution",
    "host.failure",
    "Host failure."), TypeError);
});

test("rejects invalid normal, failed, and cleanup combinations", () => {
  const valid = normalExecutionResult(0);
  for (const change of [
    { exitCode: 1.5 },
    { exitCode: -0x80000001 },
    { exitCode: 0x80000000 },
    { primaryFailure: cleanup() },
    { cleanupFailures: [cleanup()] },
  ]) {
    assert.throws(() => assertExecutionResult({ ...valid, ...change }), TypeError);
  }

  const failed = failedExecutionResult(
    "hostFailure",
    "execution",
    "host.failure",
    "The execution host failed.");
  assert.throws(() => assertExecutionResult({ ...failed, exitCode: 0 }), TypeError);
  assert.throws(() => assertExecutionResult({ ...failed, primaryFailure: null }), TypeError);
  assert.throws(() => assertExecutionResult({
    ...failed,
    cleanupFailures: [executionFailure(
      "execution",
      "host.failure",
      "The execution host failed.")],
  }), TypeError);

  assert.throws(() => appendCleanupFailures(valid, null), TypeError);
  assert.throws(() => appendCleanupFailures(valid, [executionFailure(
    "execution",
    "host.failure",
    "The execution host failed.")]), TypeError);
});

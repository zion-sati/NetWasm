// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";
import {
  appendCleanupFailures,
  assertExecutionResult,
  describeFailureCause,
  executionFailure,
  failedExecutionResult,
  mapFailureDetails,
  normalExecutionResult,
} from "./execution-result.mjs";
import { createContractError } from "./contract-error.mjs";

test("failures carry the caught cause as a redacted single-line detail", () => {
  const result = failedExecutionResult(
    "contractFailure",
    "validation",
    "contract.invalid",
    "The execution request is invalid.",
    new TypeError("platform capability 'clocks' was not granted"));

  assert.deepEqual(result.primaryFailure, {
    phase: "validation",
    code: "contract.invalid",
    message: "The execution request is invalid.",
    detail: "TypeError: platform capability 'clocks' was not granted",
  });
  assert.equal(Object.isFrozen(result.primaryFailure), true);
  assert.equal("detail" in executionFailure("execution", "host.failure", "Failed."), false);
  assert.equal("detail" in executionFailure("execution", "host.failure", "Failed.", "   "), false);
});

test("describes error and non-error causes", () => {
  assert.equal(describeFailureCause(new RangeError("too far")), "RangeError: too far");
  assert.equal(describeFailureCause(new Error("")), "Error");
  assert.equal(describeFailureCause({ name: "", message: "plain object" }), "Error: plain object");
  assert.equal(describeFailureCause({ name: 7, message: "odd name" }), "Error: odd name");
  assert.equal(describeFailureCause("a string"), "a string");
  assert.equal(describeFailureCause(42), "42");
  assert.equal(describeFailureCause(null), "null");
  assert.equal(describeFailureCause({ toString() { throw new Error("hostile"); } }), undefined);
  assert.equal(describeFailureCause(new Error("line one\nline\u0000two\t  three ")),
    "Error: line one line two three");
});

test("describes the value a contract error wraps", () => {
  const wrapped = createContractError(undefined, new TypeError("platform capability 'clocks' was not granted"));

  assert.equal(describeFailureCause(createContractError(undefined, wrapped)),
    "TypeError: platform capability 'clocks' was not granted");
  assert.equal(describeFailureCause(createContractError("manifest digest mismatch")),
    "Error: manifest digest mismatch");
});

test("maps failure details and drops details a transform empties", () => {
  const outcome = appendCleanupFailures(
    failedExecutionResult("hostFailure", "execution", "host.failure", "Failed.", new Error("primary")),
    [cleanup(), executionFailure("cleanup", "host.release", "Release failed.", new Error("cleanup"))]);

  const mapped = mapFailureDetails(outcome, detail => detail === "Error: primary" ? "  " : `${detail}\n!`);

  assert.equal("detail" in mapped.primaryFailure, false);
  assert.equal("detail" in mapped.cleanupFailures[0], false);
  assert.equal(mapped.cleanupFailures[1].detail, "Error: cleanup !");
  assert.equal(Object.isFrozen(mapped.cleanupFailures), true);
  const normal = normalExecutionResult(3);
  assert.equal(mapFailureDetails(normal, () => assert.fail("normal results have no failures")), normal);
  assert.throws(() => mapFailureDetails(outcome, null), /failure detail transform is required/);
  assert.throws(() => mapFailureDetails(outcome, () => 7), /failure detail transform must return text/);
});

test("truncates long causes to the detail limit", () => {
  const detail = describeFailureCause(new Error("x".repeat(2000)));

  assert.equal(detail.length, 1024);
  assert.equal(detail.endsWith("\u2026"), true);
  assert.equal(describeFailureCause(new Error("y".repeat(1017))).length, 1024);
});

test("rejects invalid failure details", () => {
  for (const detail of ["", " padded", "a".repeat(1025), "bad\ncontrol", 7]) {
    const result = {
      schemaVersion: 1,
      completionKind: "hostFailure",
      exitCode: null,
      primaryFailure: { phase: "execution", code: "host.failure", message: "Failed.", detail },
      cleanupFailures: [],
    };
    assert.throws(() => assertExecutionResult(result), /execution failure detail is invalid/);
  }
});

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

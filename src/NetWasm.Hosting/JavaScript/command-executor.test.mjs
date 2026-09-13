import assert from "node:assert/strict";
import test from "node:test";
import { executeCommand } from "./command-executor.mjs";

test("preserves every signed 32-bit command exit code", () => {
  for (const exitCode of [-0x80000000, -1, 0, 1, 0x7fffffff]) {
    assert.deepEqual(executeCommand({ run: () => exitCode }), {
      schemaVersion: 1,
      completionKind: "normal",
      exitCode,
      primaryFailure: null,
      cleanupFailures: [],
    });
  }
});

test("pre-cancellation prevents guest entry", () => {
  const controller = new AbortController();
  controller.abort();
  let calls = 0;
  const result = executeCommand({
    run() { calls++; return 0; },
    signal: controller.signal,
  });
  assert.equal(calls, 0);
  assert.equal(result.completionKind, "callerCancellation");
  assert.equal(result.primaryFailure.code, "caller.cancelled");
});

test("a synchronous command owns the terminal result after entry", () => {
  const controller = new AbortController();
  const result = executeCommand({
    run() {
      controller.abort();
      return 23;
    },
    signal: controller.signal,
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 23);
});

test("maps invocation failure without exposing exception details", () => {
  const result = executeCommand({
    run() { throw new Error("private command detail"); },
  });
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.command-invoke");
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("maps every invalid exit representation to contract failure", () => {
  for (const exitCode of [undefined, null, 1.5, -0x80000001, 0x80000000, 0n, Promise.resolve(0)]) {
    const result = executeCommand({ run: () => exitCode });
    assert.equal(result.completionKind, "contractFailure");
    assert.equal(result.primaryFailure.code, "contract.command-exit-code");
  }
});

test("validates dependencies before guest entry", () => {
  assert.throws(() => executeCommand(null), TypeError);
  assert.throws(() => executeCommand(1), TypeError);
  assert.throws(() => executeCommand(), TypeError);
  assert.throws(() => executeCommand({ run: null }), TypeError);
  for (const signal of [{}, { aborted: false }, { aborted: false, addEventListener() {} }]) {
    let calls = 0;
    assert.throws(() => executeCommand({
      run() { calls++; return 0; },
      signal,
    }), TypeError);
    assert.equal(calls, 0);
  }
});

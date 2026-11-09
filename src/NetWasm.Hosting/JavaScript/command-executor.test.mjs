import assert from "node:assert/strict";
import test from "node:test";
import { executeCommand } from "./command-executor.mjs";
import { NetWasmManagedError } from "./managed-errors.mjs";
import { createWasiPlatform } from "./wasi-platform.mjs";

test("preserves every signed 32-bit command exit code", async () => {
  for (const exitCode of [-0x80000000, -1, 0, 1, 0x7fffffff]) {
    assert.deepEqual(await executeCommand({ run: () => exitCode }), {
      schemaVersion: 1,
      completionKind: "normal",
      exitCode,
      primaryFailure: null,
      cleanupFailures: [],
    });
  }
});

test("pre-cancellation prevents guest entry", async () => {
  const controller = new AbortController();
  controller.abort();
  let calls = 0;
  const result = await executeCommand({
    run() { calls++; return 0; },
    signal: controller.signal,
  });
  assert.equal(calls, 0);
  assert.equal(result.completionKind, "callerCancellation");
  assert.equal(result.primaryFailure.code, "caller.cancelled");
});

test("a command owns the terminal result after entry", async () => {
  const controller = new AbortController();
  const result = await executeCommand({
    run() {
      controller.abort();
      return 23;
    },
    signal: controller.signal,
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 23);
});

test("maps invocation failure without exposing exception details", async () => {
  const result = await executeCommand({
    run() { throw new Error("private command detail"); },
  });
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.command-invoke");
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("classifies only branded terminal evidence as a managed failure", async () => {
  const result = await executeCommand({
    run() {
      throw new NetWasmManagedError("run", {
        cause: new WebAssembly.RuntimeError("private trap"),
        managedType: 7,
      });
    },
  });

  assert.equal(result.completionKind, "managedFailure");
  assert.equal(result.primaryFailure.code, "managed.failure");
  assert.doesNotMatch(JSON.stringify(result), /private|managedType/u);
});

test("maps every invalid exit representation to contract failure", async () => {
  for (const exitCode of [undefined, null, 1.5, -0x80000001, 0x80000000, 0n, Promise.resolve(0.5)]) {
    const result = await executeCommand({ run: () => exitCode });
    assert.equal(result.completionKind, "contractFailure");
    assert.equal(result.primaryFailure.code, "contract.command-exit-code");
  }
});

test("validates dependencies before guest entry", async () => {
  await assert.rejects(() => executeCommand(null), TypeError);
  await assert.rejects(() => executeCommand(1), TypeError);
  await assert.rejects(() => executeCommand(), TypeError);
  await assert.rejects(() => executeCommand({ run: null }), TypeError);
  for (const signal of [{}, { aborted: false }, { aborted: false, addEventListener() {} }]) {
    let calls = 0;
    await assert.rejects(() => executeCommand({
      run() { calls++; return 0; },
      signal,
    }), TypeError);
    assert.equal(calls, 0);
  }
});

test("awaits an asynchronous command and completes with its exit code", async () => {
  const result = await executeCommand({ run: async () => 9 });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 9);
});

test("completes with the status a guest passes to wasi:cli/exit", async () => {
  const sink = Object.freeze({ write() {} });
  const { imports } = createWasiPlatform(Object.freeze({ initialDirectory: null, network: null }))(Object.freeze({
    arguments: [], environment: [], filesystem: null, grants: { network: "denyAll" },
    signal: null, stderr: sink, stdin: null, stdout: sink,
  }));
  const exit = imports["wasi:cli/exit@0.3.2"];
  for (const [run, exitCode] of [
    [() => exit.exit({ tag: "ok" }), 0],
    [() => exit.exit({ tag: "err" }), 1],
    [async () => exit.exitWithCode(42), 42],
  ]) {
    const result = await executeCommand({ run });
    assert.equal(result.completionKind, "normal");
    assert.equal(result.exitCode, exitCode);
  }
});

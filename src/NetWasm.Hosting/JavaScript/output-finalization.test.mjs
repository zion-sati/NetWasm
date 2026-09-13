import assert from "node:assert/strict";
import test from "node:test";

import { closeExecutionScope } from "./execution-scope-closer.mjs";
import { normalExecutionResult } from "./execution-result.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";

const sink = write => Object.freeze({ write });

test("forwards copied output to both caller sinks", async () => {
  const writes = [];
  const output = createOutputFinalization({
    stdout: sink(bytes => writes.push(["stdout", bytes])),
    stderr: sink(bytes => writes.push(["stderr", bytes])),
  });
  assert.equal(Object.isFrozen(output), true);
  assert.equal(Object.isFrozen(output.releaseActions), true);
  const bytes = Uint8Array.of(1, 2);
  output.stdout.write(bytes);
  output.stderr.write(bytes);
  assert.deepEqual(writes.map(([name, value]) => [name, [...value]]), [
    ["stdout", [1, 2]], ["stderr", [1, 2]],
  ]);
  const outcome = normalExecutionResult(0);
  assert.equal(await closeExecutionScope(outcome, output.releaseActions), outcome);
});

test("records each first sink failure and reports ordered output failures", async () => {
  const calls = [];
  const output = createOutputFinalization({
    stdout: sink(() => { calls.push("stdout"); throw new Error("private stdout"); }),
    stderr: sink(() => { calls.push("stderr"); throw new Error("private stderr"); }),
  });
  output.stdout.write(Uint8Array.of(1));
  output.stdout.write(Uint8Array.of(2));
  output.stderr.write(Uint8Array.of(3));
  assert.deepEqual(calls, ["stdout", "stderr"]);
  const outcome = await closeExecutionScope(normalExecutionResult(0), output.releaseActions);
  assert.equal(outcome.completionKind, "hostFailure");
  assert.deepEqual([
    outcome.primaryFailure.phase,
    outcome.primaryFailure.code,
    ...outcome.cleanupFailures.map(failure => failure.code),
  ], ["output", "host.stdout-output", "host.stderr-output"]);
  assert.doesNotMatch(JSON.stringify(outcome), /private/);
});

test("rejects malformed requests and sinks before creating wrappers", () => {
  const valid = { stdout: sink(() => {}), stderr: sink(() => {}) };
  for (const value of [null, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createOutputFinalization(value), TypeError);
  }
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createOutputFinalization(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "stdout", { enumerable: true, get: () => valid.stdout });
  assert.throws(() => createOutputFinalization(accessor), TypeError);
  for (const name of ["stdout", "stderr"]) {
    for (const value of [null, {}, { write() {} }]) {
      assert.throws(() => createOutputFinalization({ ...valid, [name]: value }), /output sink/);
    }
  }
});

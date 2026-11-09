import assert from "node:assert/strict";
import test from "node:test";

import { createCommandRun } from "./command-run.mjs";

const configuration = Object.freeze({
  applicationImports: Object.freeze([]),
  arguments: Object.freeze(["App", "--from-build"]),
  environment: Object.freeze([Object.freeze({ name: "MODE", value: "build" })]),
  grants: Object.freeze({
    clocks: Object.freeze(["wall"]),
    environment: Object.freeze(["MODE"]),
    network: "denyAll",
    preopens: Object.freeze([]),
    randomness: false, temporaryDirectory: false, workingDirectory: false,
  }),
});
const startup = Object.freeze({ buildFingerprint: "1".repeat(64), manifestSha256: "2".repeat(64) });
const encode = text => new TextEncoder().encode(text);

test("runs with the build's request, overrides, and line output", async () => {
  const lines = [];
  let received;
  const run = createCommandRun(async execution => {
    received = execution;
    execution.stdout.write(encode("one\ntw"));
    execution.stdout.write(encode("o\r\nthree"));
    execution.stderr.write(encode("problem\n"));
    return { exitCode: 0 };
  });
  const controller = new AbortController();
  const result = await run({
    configuration,
    startup,
    options: {
      arguments: ["a", "b"],
      environment: { MODE: "page", EXTRA: "1" },
      signal: controller.signal,
      stdout: line => lines.push(["out", line]),
      stderr: line => lines.push(["err", line]),
    },
  });
  assert.deepEqual(result, { exitCode: 0 });
  assert.deepEqual(lines, [["out", "one"], ["out", "two"], ["err", "problem"], ["out", "three"]]);
  assert.equal(received.signal, controller.signal);
  assert.deepEqual(received.request, {
    schemaVersion: 1,
    buildFingerprint: startup.buildFingerprint,
    deploymentManifestSha256: startup.manifestSha256,
    arguments: ["App", "a", "b"],
    environment: [{ name: "MODE", value: "page" }, { name: "EXTRA", value: "1" }],
    grants: { ...configuration.grants, environment: ["MODE", "EXTRA"] },
    applicationImports: [],
  });
});

test("defaults to the build's request and the console", async t => {
  const log = t.mock.method(console, "log", () => {});
  const error = t.mock.method(console, "error", () => {});
  let received;
  const run = createCommandRun(async execution => {
    received = execution;
    execution.stdout.write(encode("hello\n"));
    execution.stderr.write(encode("partial"));
    throw new Error("host failed");
  });
  await assert.rejects(run({ configuration, startup, options: undefined }), /host failed/);
  assert.deepEqual(received.request.arguments, ["App", "--from-build"]);
  assert.equal(received.request.environment, configuration.environment);
  assert.equal(received.signal, null);
  assert.deepEqual(log.mock.calls.map(call => call.arguments), [["hello"]]);
  assert.deepEqual(error.mock.calls.map(call => call.arguments), [["partial"]]);
});

test("rejects invalid options before running", async () => {
  const run = createCommandRun(() => assert.fail("must not run"));
  for (const [options, pattern] of [
    [null, /must be an object/],
    [[], /must be an object/],
    [{ other: 1 }, /unknown run option other/],
    [{ arguments: "a" }, /array of strings/],
    [{ arguments: [1] }, /array of strings/],
    [{ stdout: "log" }, /stdout must be a line callback/],
    [{ stderr: 1 }, /stderr must be a line callback/],
    [{ signal: {} }, /AbortSignal/],
    [{ environment: { A: 1 } }, /values must be strings/],
  ]) {
    await assert.rejects(run({ configuration, startup, options }), pattern);
  }
  assert.throws(() => createCommandRun(null), /requires executeNetWasm/);
});

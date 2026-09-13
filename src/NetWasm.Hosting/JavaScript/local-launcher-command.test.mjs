import assert from "node:assert/strict";
import test from "node:test";

import { createLocalLauncherCommand } from "./local-launcher-command.mjs";
import { normalExecutionResult } from "./execution-result.mjs";

const sink = Object.freeze({ write() {} });
const paths = Object.freeze({
  descriptor: "/output/app.netwasm.execution.json",
  request: "/requests/run.json",
  result: "/results/run.json",
});

function invocation(overrides = {}) {
  return {
    arguments: [
      "--descriptor", paths.descriptor,
      "--request", paths.request,
      "--result", paths.result,
    ],
    hostExecutablePath: "/tools/node",
    launcherPath: "/packages/hosting/launcher.mjs",
    signal: null,
    stderr: sink,
    stdout: sink,
    ...overrides,
  };
}

function createFixture(overrides = {}) {
  const calls = [];
  const outcome = normalExecutionResult(17);
  const options = {
    async deleteResult(path) {
      calls.push(["delete", path]);
    },
    async launch(value) {
      calls.push(["launch", value]);
      return outcome;
    },
    async readFileText(path) {
      calls.push(["file", path]);
      return path === paths.descriptor ? "descriptor-text" : "request-text";
    },
    async readStdinText() {
      calls.push(["stdin"]);
      return "stdin-request-text";
    },
    async writeResultText(path, text) {
      calls.push(["result", path, text]);
    },
    ...overrides,
  };
  return { calls, outcome, run: createLocalLauncherCommand(options) };
}

function malformedDataObjects(value) {
  const first = Object.keys(value)[0];
  const withSymbol = { ...value, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), value);
  const extra = { ...value, extra: true };
  const missing = { ...value };
  delete missing[first];
  const nonEnumerable = { ...value };
  Object.defineProperty(nonEnumerable, first, { value: value[first], enumerable: false });
  const accessor = { ...value };
  Object.defineProperty(accessor, first, { get: () => value[first], enumerable: true });
  return { invalid: [null, 1, [], withSymbol], malformed: [inherited, extra, missing, nonEnumerable, accessor] };
}

test("local launcher command preserves application streams and writes its result channel", async () => {
  const fixture = createFixture();
  assert.equal(Object.isFrozen(fixture.run), true);
  assert.strictEqual(await fixture.run(invocation()), fixture.outcome);
  assert.deepEqual(fixture.calls.slice(0, 2), [
    ["file", paths.descriptor],
    ["file", paths.request],
  ]);
  const launch = fixture.calls.find(([kind]) => kind === "launch")[1];
  assert.equal(Object.isFrozen(launch), true);
  assert.equal(launch.descriptorText, "descriptor-text");
  assert.equal(launch.requestText, "request-text");
  assert.equal(launch.hostExecutablePath, "/tools/node");
  assert.equal(launch.launcherPath, "/packages/hosting/launcher.mjs");
  assert.strictEqual(launch.stdout, sink);
  assert.strictEqual(launch.stderr, sink);
  assert.equal(launch.signal, null);
  assert.deepEqual(fixture.calls.at(-1), [
    "result",
    paths.result,
    `${JSON.stringify(fixture.outcome)}\n`,
  ]);
});

test("local launcher command accepts stdin and option reordering", async () => {
  const fixture = createFixture();
  const outcome = await fixture.run(invocation({
    arguments: [
      "--result", paths.result,
      "--request", "-",
      "--descriptor", paths.descriptor,
    ],
  }));
  assert.strictEqual(outcome, fixture.outcome);
  assert.deepEqual(fixture.calls.filter(([kind]) => kind === "stdin"), [["stdin"]]);
  assert.equal(fixture.calls.filter(([kind]) => kind === "file").length, 1);
  assert.equal(
    fixture.calls.find(([kind]) => kind === "launch")[1].requestText,
    "stdin-request-text");
});

test("local launcher command explicitly replaces an SDK-owned result channel", async () => {
  const fixture = createFixture();
  await fixture.run(invocation({
    arguments: [...invocation().arguments, "--replace-result"],
  }));
  assert.deepEqual(fixture.calls[0], ["delete", paths.result]);
  assert.deepEqual(fixture.calls.at(-1), [
    "result",
    paths.result,
    `${JSON.stringify(fixture.outcome)}\n`,
  ]);
});

test("local launcher command separates SDK options from application arguments", async () => {
  const fixture = createFixture();
  await fixture.run(invocation({
    arguments: [...invocation().arguments, "--", "first", "--second"],
  }));
  assert.deepEqual(
    fixture.calls.find(([kind]) => kind === "launch")[1].arguments,
    ["first", "--second"]);
});

test("createLocalLauncherCommand validates exact dependencies", () => {
  const valid = {
    deleteResult() {},
    launch() {},
    readFileText() {},
    readStdinText() {},
    writeResultText() {},
  };
  const { invalid, malformed } = malformedDataObjects(valid);
  for (const value of invalid) {
    assert.throws(() => createLocalLauncherCommand(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createLocalLauncherCommand(value), /options shape/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createLocalLauncherCommand({ ...valid, [key]: null }),
      new RegExp(`'${key}' action is required`, "i"));
  }
});

test("local launcher command validates its exact invocation", async () => {
  const base = invocation();
  const { invalid, malformed } = malformedDataObjects(base);
  for (const value of invalid) {
    await assert.rejects(() => createFixture().run(value), /invocation is invalid/i);
  }
  for (const value of malformed) {
    await assert.rejects(() => createFixture().run(value), /invocation shape/i);
  }
  await assert.rejects(
    () => createFixture().run(invocation({ arguments: null })),
    /arguments must be an array/i);
  for (const key of ["hostExecutablePath", "launcherPath"]) {
    for (const value of [null, "", "relative", "/bad\0path"]) {
      await assert.rejects(
        () => createFixture().run(invocation({ [key]: value })),
        /absolute local path without NUL/i);
    }
  }
  for (const signal of [undefined, {}, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(
      () => createFixture().run(invocation({ signal })),
      /signal must be an AbortSignal/i);
  }
  await assert.rejects(
    () => createFixture().run(invocation({ stdout: { write() {} } })),
    /stdout output sink must be an immutable/i);
  await assert.rejects(
    () => createFixture().run(invocation({ stderr: null })),
    /stderr output sink is invalid/i);
});

test("local launcher command rejects incomplete, unknown, and duplicate options", async () => {
  for (const arguments_ of [
    [],
    ["--descriptor", paths.descriptor],
    ["--descriptor", paths.descriptor, "--request", paths.request, "--unknown", paths.result],
    ["--descriptor", paths.descriptor, "--request", paths.request, "--request", paths.result],
    ["--replace-result", "--replace-result", "--descriptor", paths.descriptor, "--request", paths.request],
    ["--descriptor", paths.descriptor, "--request", paths.request, "--result"],
  ]) {
    await assert.rejects(
      () => createFixture().run(invocation({ arguments: arguments_ })),
      /requires descriptor|unknown or duplicated/i);
  }
  await assert.rejects(
    () => createFixture().run(invocation({
      arguments: [
        "--descriptor", paths.descriptor,
        "--request", paths.request,
        "--unknown", paths.result,
      ],
    })),
    /unknown or duplicated/i);
});

test("local launcher command validates canonical absolute channel paths", async () => {
  for (const [name, value] of [
    ["--descriptor", null],
    ["--request", "relative"],
    ["--result", "/bad\0path"],
  ]) {
    const arguments_ = [...invocation().arguments];
    arguments_[arguments_.indexOf(name) + 1] = value;
    await assert.rejects(
      () => createFixture().run(invocation({ arguments: arguments_ })),
      /absolute local path without NUL/i);
  }
  for (const name of ["--descriptor", "--request", "--result"]) {
    const arguments_ = [...invocation().arguments];
    arguments_[arguments_.indexOf(name) + 1] = "/output/../other.json";
    await assert.rejects(
      () => createFixture().run(invocation({ arguments: arguments_ })),
      /path must be canonical/i);
  }
});

test("local launcher command keeps all channels distinct", async () => {
  for (const [requestPath, resultPath] of [
    [paths.request, paths.descriptor],
    [paths.descriptor, paths.result],
    [paths.result, paths.result],
  ]) {
    await assert.rejects(
      () => createFixture().run(invocation({
        arguments: [
          "--descriptor", paths.descriptor,
          "--request", requestPath,
          "--result", resultPath,
        ],
      })),
      /channels must use distinct paths/i);
  }
});

test("local launcher command maps input and launch failures to the result channel", async () => {
  for (const options of [
    { async readFileText() { throw new Error("secret read failure"); } },
    { async readStdinText() { throw new Error("secret stdin failure"); } },
    { async launch() { throw new Error("secret launch failure"); } },
    { async launch() { return {}; } },
  ]) {
    const fixture = createFixture(options);
    const useStdin = Object.hasOwn(options, "readStdinText");
    const arguments_ = useStdin
      ? ["--descriptor", paths.descriptor, "--request", "-", "--result", paths.result]
      : invocation().arguments;
    const result = await fixture.run(invocation({ arguments: arguments_ }));
    assert.equal(result.completionKind, "hostFailure");
    assert.equal(result.primaryFailure.code, "host.launcher-input");
    assert.doesNotMatch(result.primaryFailure.message, /secret/i);
    assert.match(fixture.calls.at(-1)[2], /"completionKind":"hostFailure"/u);
  }
});

test("local launcher command propagates result-channel write failure", async () => {
  const failure = new Error("result write failed");
  await assert.rejects(
    () => createFixture({ async writeResultText() { throw failure; } }).run(invocation()),
    error => error === failure);
});

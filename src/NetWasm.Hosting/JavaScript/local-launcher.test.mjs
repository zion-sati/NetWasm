import assert from "node:assert/strict";
import test from "node:test";

import { createLocalLauncher } from "./local-launcher.mjs";
import { normalExecutionResult } from "./execution-result.mjs";

const sink = Object.freeze({ write() {} });
const descriptor = Object.freeze({
  buildFingerprint: "build",
  deploymentManifestPath: "/output/deployment.json",
  deploymentManifestSha256: "manifest",
  hostExecutablePath: "/tools/node",
  launcherPath: "/packages/hosting/launcher.mjs",
  toolPackages: Object.freeze([
    Object.freeze({ id: "NetWasm.Toolchain", rootPath: "/packages/toolchain" }),
  ]),
});
const request = Object.freeze({
  buildFingerprint: descriptor.buildFingerprint,
  deploymentManifestSha256: descriptor.deploymentManifestSha256,
});

function invocation(overrides = {}) {
  return {
    arguments: [],
    descriptorText: "descriptor",
    hostExecutablePath: descriptor.hostExecutablePath,
    launcherPath: descriptor.launcherPath,
    requestText: "request",
    signal: null,
    stderr: sink,
    stdout: sink,
    ...overrides,
  };
}

function createFixture(overrides = {}) {
  const calls = [];
  const platform = Object.freeze({ createFilesystem() {}, createShim() {} });
  const outcome = normalExecutionResult(23);
  const options = {
    appendArguments(value, arguments_) {
      calls.push(["arguments", value, arguments_]);
      return value;
    },
    createExecution(value) {
      calls.push(["create", value]);
      return async value_ => {
        calls.push(["execute", value_]);
        return outcome;
      };
    },
    async loadPlatform(value) {
      calls.push(["platform", value]);
      return platform;
    },
    readDescriptor(value) {
      calls.push(["descriptor", value]);
      return descriptor;
    },
    readRequest(value) {
      calls.push(["request", value]);
      return request;
    },
    async realPath(value) {
      calls.push(["real", value]);
      return value;
    },
    async verifyPackage(value) {
      calls.push(["verify", value]);
      return value;
    },
    ...overrides,
  };
  return { calls, launch: createLocalLauncher(options), outcome, platform };
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

function assertFailure(outcome, completionKind, code, phase = "validation") {
  assert.equal(outcome.completionKind, completionKind);
  assert.equal(outcome.primaryFailure.code, code);
  assert.equal(outcome.primaryFailure.phase, phase);
  assert.equal(outcome.exitCode, null);
}

test("local launcher verifies identities and delegates the exact public execution call", async () => {
  const fixture = createFixture();
  assert.equal(Object.isFrozen(fixture.launch), true);
  assert.strictEqual(await fixture.launch(invocation()), fixture.outcome);
  assert.deepEqual(fixture.calls.slice(0, 2), [
    ["descriptor", "descriptor"],
    ["request", "request"],
  ]);
  assert.deepEqual(fixture.calls.filter(([kind]) => kind === "real"), [
    ["real", descriptor.hostExecutablePath],
    ["real", descriptor.hostExecutablePath],
    ["real", descriptor.launcherPath],
    ["real", descriptor.launcherPath],
  ]);
  assert.strictEqual(fixture.calls.find(([kind]) => kind === "verify")[1], descriptor.toolPackages[0]);
  assert.strictEqual(fixture.calls.find(([kind]) => kind === "platform")[1], descriptor.toolPackages[0]);
  const composition = fixture.calls.find(([kind]) => kind === "create")[1];
  assert.equal(Object.isFrozen(composition), true);
  assert.equal(composition.manifestPath, descriptor.deploymentManifestPath);
  assert.strictEqual(composition.platform, fixture.platform);
  const execution = fixture.calls.find(([kind]) => kind === "execute")[1];
  assert.equal(Object.isFrozen(execution), true);
  assert.strictEqual(execution.request, request);
  assert.strictEqual(execution.stdout, sink);
  assert.strictEqual(execution.stderr, sink);
  assert.equal(execution.signal, null);
});

test("createLocalLauncher validates its exact dependencies", () => {
  const valid = {
    appendArguments() {},
    createExecution() {},
    loadPlatform() {},
    readDescriptor() {},
    readRequest() {},
    realPath() {},
    verifyPackage() {},
  };
  const { invalid, malformed } = malformedDataObjects(valid);
  for (const value of invalid) {
    assert.throws(() => createLocalLauncher(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createLocalLauncher(value), /options shape/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createLocalLauncher({ ...valid, [key]: null }),
      new RegExp(`'${key}' action is required`, "i"));
  }
});

test("local launcher validates the exact invocation boundary", async () => {
  const base = invocation();
  const { invalid, malformed } = malformedDataObjects(base);
  for (const value of invalid) {
    await assert.rejects(() => createFixture().launch(value), /invocation is invalid/i);
  }
  for (const value of malformed) {
    await assert.rejects(() => createFixture().launch(value), /invocation shape/i);
  }
  for (const key of ["descriptorText", "requestText"]) {
    for (const value of [null, ""]) {
      await assert.rejects(
        () => createFixture().launch(invocation({ [key]: value })),
        /text is required/i);
    }
  }
  for (const key of ["hostExecutablePath", "launcherPath"]) {
    for (const value of [null, "", "relative", "/bad\0path"]) {
      await assert.rejects(
        () => createFixture().launch(invocation({ [key]: value })),
        /absolute local path without NUL/i);
    }
  }
  for (const signal of [undefined, {}, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(
      () => createFixture().launch(invocation({ signal })),
      /signal must be an AbortSignal/i);
  }
  await assert.rejects(
    () => createFixture().launch(invocation({ stdout: { write() {} } })),
    /stdout output sink must be an immutable/i);
  await assert.rejects(
    () => createFixture().launch(invocation({ stderr: null })),
    /stderr output sink is invalid/i);
});

test("local launcher returns cancellation before and between setup stages", async () => {
  const preCancelled = new AbortController();
  preCancelled.abort();
  const preFixture = createFixture();
  assertFailure(
    await preFixture.launch(invocation({ signal: preCancelled.signal })),
    "callerCancellation",
    "host.cancelled");
  assert.deepEqual(preFixture.calls, []);

  for (const stage of ["request", "realPath", "loadPlatform"]) {
    const controller = new AbortController();
    const options = stage === "request"
      ? { readRequest() { controller.abort(); return request; } }
      : stage === "realPath"
        ? { async realPath(value) { controller.abort(); return value; } }
        : { async loadPlatform() { controller.abort(); return {}; } };
    const outcome = await createFixture(options).launch(
      invocation({ signal: controller.signal }));
    assertFailure(outcome, "callerCancellation", "host.cancelled");
  }
});

test("local launcher maps descriptor and request failures without leaking errors", async () => {
  const descriptorFailure = await createFixture({
    readDescriptor() { throw new Error("secret descriptor error"); },
  }).launch(invocation());
  assertFailure(descriptorFailure, "hostFailure", "host.descriptor");
  assert.doesNotMatch(descriptorFailure.primaryFailure.message, /secret/i);

  const requestFailure = await createFixture({
    readRequest() { throw new Error("secret request error"); },
  }).launch(invocation());
  assertFailure(requestFailure, "contractFailure", "host.contract");
  assert.doesNotMatch(requestFailure.primaryFailure.message, /secret/i);

  for (const mismatched of [
    { ...request, buildFingerprint: "other" },
    { ...request, deploymentManifestSha256: "other" },
  ]) {
    assertFailure(
      await createFixture({ readRequest() { return mismatched; } }).launch(invocation()),
      "contractFailure",
      "host.contract");
  }
});

test("local launcher maps physical identity mismatch and lookup failures", async () => {
  for (const changedInvocation of [
    invocation({ hostExecutablePath: "/tools/other-node" }),
    invocation({ launcherPath: "/packages/hosting/other-launcher.mjs" }),
  ]) {
    const outcome = await createFixture().launch(changedInvocation);
    assertFailure(outcome, "hostFailure", "host.launcher-identity");
  }
  const failedLookup = await createFixture({
    async realPath() { throw new Error("secret path failure"); },
  }).launch(invocation());
  assertFailure(failedLookup, "hostFailure", "host.launcher-identity");
  assert.doesNotMatch(failedLookup.primaryFailure.message, /secret/i);
});

test("local launcher requires and verifies the exact Toolchain package", async () => {
  const missingDescriptor = Object.freeze({ ...descriptor, toolPackages: Object.freeze([]) });
  const missing = await createFixture({
    readDescriptor() { return missingDescriptor; },
  }).launch(invocation());
  assertFailure(missing, "hostFailure", "host.toolchain");
  assert.match(missing.primaryFailure.message, /does not select/i);

  for (const options of [
    { async verifyPackage() { throw new Error("secret verify failure"); } },
    { async loadPlatform() { throw new Error("secret load failure"); } },
  ]) {
    const outcome = await createFixture(options).launch(invocation());
    assertFailure(outcome, "hostFailure", "host.toolchain");
    assert.doesNotMatch(outcome.primaryFailure.message, /secret/i);
  }
});

test("local launcher maps composition and unexpected execution failures", async () => {
  for (const createExecution of [
    () => { throw new Error("secret composition failure"); },
    () => null,
  ]) {
    const outcome = await createFixture({ createExecution }).launch(invocation());
    assertFailure(outcome, "hostFailure", "host.composition");
    assert.doesNotMatch(outcome.primaryFailure.message, /secret/i);
  }
  const execution = await createFixture({
    createExecution() {
      return async () => { throw new Error("secret execution failure"); };
    },
  }).launch(invocation());
  assertFailure(execution, "hostFailure", "host.execution", "execution");
  assert.doesNotMatch(execution.primaryFailure.message, /secret/i);
});

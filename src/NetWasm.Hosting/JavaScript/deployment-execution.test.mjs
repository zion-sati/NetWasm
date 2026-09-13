import assert from "node:assert/strict";
import test from "node:test";

import { createContractError } from "./contract-error.mjs";
import { createDeploymentExecution } from "./deployment-execution.mjs";
import { normalExecutionResult } from "./execution-result.mjs";
import { createOutputFinalization } from "./output-finalization.mjs";
import { executeWithTimeZoneResources } from "./timezone-execution.mjs";

const digest = "1".repeat(64);
const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });
const componentImports = Object.freeze(Object.create(null));
const consumerModules = Object.freeze(Object.create(null));
const rawProviders = Object.freeze(Object.create(null));
const binding = Object.freeze({ binding: true });

function request() {
  return Object.freeze({
    buildFingerprint: digest,
    deploymentManifestSha256: digest,
    environment: Object.freeze([]),
    grants: Object.freeze({ preopens: Object.freeze([]) }),
  });
}

function manifest(kind = "component") {
  return Object.freeze({
    artifacts: Object.freeze([]),
    buildFingerprint: digest,
    deploymentKind: kind,
    executionContract: "wasi-command@0.2.11",
    runtimeFeatures: Object.freeze([]),
  });
}

function execution(overrides = {}) {
  return { request: request(), signal: null, stderr, stdout, ...overrides };
}

function fixture(overrides = {}) {
  const calls = [];
  const strategy = async value => {
    calls.push(["strategy", value]);
    return normalExecutionResult(17);
  };
  class Descriptor { openAt() {} read() {} }
  const options = {
    createFilesystem(preopens) {
      calls.push(["filesystem", preopens]);
      return {
        types: { Descriptor },
        preopens: { getDirectories: () => [] },
        mountReadOnlyFile() {},
        dispose() { calls.push(["filesystem-release"]); },
      };
    },
    createOutputFinalization,
    createTimeZoneMaterializer(mount) {
      calls.push(["materializer", mount]);
      return async value => {
        calls.push(["materialize", value]);
        return null;
      };
    },
    executeWithResources: executeWithTimeZoneResources,
    async loadManifest(value) {
      calls.push(["manifest", value]);
      return manifest();
    },
    async prepareProviders(value) {
      calls.push(["providers", value]);
      return Object.freeze({ binding, componentImports, consumerModules, rawProviders });
    },
    prepareRawInterop(value) {
      calls.push(["interop", value]);
      return Object.freeze({});
    },
    resolveStrategy(kind) {
      calls.push(["resolve", kind]);
      return strategy;
    },
    selectTimeZone(value) {
      calls.push(["timezone", value]);
      return null;
    },
    validateRequest(value) {
      calls.push(["request", value]);
      return value;
    },
    ...overrides,
  };
  return { calls, execute: createDeploymentExecution(options), options, strategy };
}

test("executes a component through validated providers and owned resources", async () => {
  const f = fixture();
  const input = execution();
  const outcome = await f.execute(input);
  assert.equal(outcome.exitCode, 17);
  assert.equal(Object.isFrozen(f.execute), true);
  assert.deepEqual(f.calls.map(([name]) => name), [
    "request", "manifest", "timezone", "resolve", "filesystem", "materializer",
    "materialize", "providers", "strategy", "filesystem-release",
  ]);
  const manifestRequest = f.calls.find(([name]) => name === "manifest")[1];
  assert.deepEqual(manifestRequest, { expectedSha256: digest, signal: null });
  const providerRequest = f.calls.find(([name]) => name === "providers")[1];
  assert.equal(providerRequest.request, input.request);
  assert.equal(providerRequest.manifest.deploymentKind, "component");
  assert.notEqual(providerRequest.stdout, stdout);
  assert.notEqual(providerRequest.stderr, stderr);
  const strategyRequest = f.calls.find(([name]) => name === "strategy")[1];
  assert.deepEqual(Object.keys(strategyRequest).sort(), [
    "artifacts", "contractKey", "imports", "signal",
  ]);
  assert.equal(strategyRequest.imports, componentImports);
});

test("derives raw interop from the root and never from the public request", async () => {
  const rawManifest = manifest("raw");
  const f = fixture({ loadManifest: async () => rawManifest });
  const outcome = await f.execute(execution());
  assert.equal(outcome.exitCode, 17);
  const strategyRequest = f.calls.find(([name]) => name === "strategy")[1];
  assert.deepEqual(Object.keys(strategyRequest).sort(), [
    "artifacts", "contractKey", "prepareInterop", "providers", "signal",
  ]);
  assert.equal(strategyRequest.providers, rawProviders);
  const interop = Object.freeze({ abi: {}, adapter: {}, manifest: {} });
  await strategyRequest.prepareInterop(interop);
  const rootRequest = f.calls.find(([name]) => name === "interop")[1];
  assert.equal(rootRequest.abi, interop.abi);
  assert.equal(rootRequest.adapter, interop.adapter);
  assert.equal(rootRequest.manifest, interop.manifest);
  assert.equal(rootRequest.consumerModules, consumerModules);
  assert.notEqual(rootRequest.stderr, stderr);
});

test("reports a missing host-local raw interop boundary without entering the Strategy", async () => {
  const f = fixture({
    loadManifest: async () => manifest("raw"),
    prepareRawInterop: null,
  });
  const outcome = await f.execute(execution());
  assert.deepEqual([outcome.completionKind, outcome.primaryFailure.code], [
    "hostFailure", "host.interop-prepare",
  ]);
  assert.equal(f.calls.some(([name]) => name === "strategy"), false);
});

test("returns validation cancellation before invoking dependencies", async () => {
  const controller = new AbortController();
  controller.abort();
  const f = fixture();
  const outcome = await f.execute(execution({ signal: controller.signal }));
  assert.deepEqual([outcome.completionKind, outcome.primaryFailure.phase], [
    "callerCancellation", "validation",
  ]);
  assert.deepEqual(f.calls, []);
});

test("maps contract, host, identity, and mid-validation cancellation failures", async () => {
  const contract = fixture({ validateRequest() { throw createContractError(); } });
  assert.equal((await contract.execute(execution())).completionKind, "contractFailure");

  const identity = fixture({ loadManifest: async () => Object.freeze({
    ...manifest(), buildFingerprint: "2".repeat(64),
  }) });
  assert.equal((await identity.execute(execution())).primaryFailure.code, "contract.invalid");

  for (const overrides of [
    { loadManifest() { throw new Error("private transport"); } },
    { selectTimeZone() { throw new Error("private selector"); } },
    { resolveStrategy() { return null; } },
  ]) {
    const f = fixture(overrides);
    const outcome = await f.execute(execution());
    assert.deepEqual([outcome.completionKind, outcome.primaryFailure.code], [
      "hostFailure", "host.validation",
    ]);
    assert.doesNotMatch(JSON.stringify(outcome), /private/);
  }

  const controller = new AbortController();
  const cancelled = fixture({ loadManifest() { controller.abort(); throw new Error("private"); } });
  const outcome = await cancelled.execute(execution({ signal: controller.signal }));
  assert.deepEqual([outcome.completionKind, outcome.primaryFailure.phase], [
    "callerCancellation", "validation",
  ]);
});

test("maps output preparation failures before filesystem allocation", async () => {
  for (const product of [null, Object.freeze({ stderr, stdout, releaseActions: [] })]) {
    const f = fixture({ createOutputFinalization: () => product });
    const outcome = await f.execute(execution());
    assert.equal(outcome.primaryFailure.code, "host.output-prepare");
    assert.equal(f.calls.some(([name]) => name === "filesystem"), false);
  }
});

test("maps provider contract, host, and cancellation failures", async () => {
  const cases = [
    [() => { throw createContractError(); }, "contractFailure", "contract.invalid"],
    [() => { throw new Error("private provider"); }, "hostFailure", "host.provider-prepare"],
    [({ signal }) => { signal.controller.abort(); throw new Error("private"); },
      "callerCancellation", "caller.cancelled"],
  ];
  for (const [prepare, kind, code] of cases) {
    const controller = new AbortController();
    controller.signal.controller = controller;
    const f = fixture({ prepareProviders: prepare });
    const outcome = await f.execute(execution({ signal: controller.signal }));
    assert.deepEqual([outcome.completionKind, outcome.primaryFailure.code], [kind, code]);
    assert.equal(f.calls.some(([name]) => name === "strategy"), false);
  }
});

test("maps malformed resource workflow results at the public boundary", async () => {
  const f = fixture({ executeWithResources: async () => null });
  const outcome = await f.execute(execution());
  assert.deepEqual([outcome.completionKind, outcome.primaryFailure.code], [
    "hostFailure", "host.execution",
  ]);
});

test("maps unexpected resource workflow rejection and caller cancellation", async () => {
  for (const cancelled of [false, true]) {
    const controller = new AbortController();
    const f = fixture({
      executeWithResources: async () => {
        if (cancelled) controller.abort();
        throw new Error("private workflow detail");
      },
    });
    const outcome = await f.execute(execution({ signal: controller.signal }));
    assert.deepEqual([outcome.completionKind, outcome.primaryFailure.phase], [
      cancelled ? "callerCancellation" : "hostFailure", "execution",
    ]);
    assert.doesNotMatch(JSON.stringify(outcome), /private/);
  }
});

test("validates factory and public request shapes before execution", async () => {
  const valid = fixture().options;
  for (const value of [null, 1, [], {}, { ...valid, extra: () => {} }, Object.create(valid)]) {
    assert.throws(() => createDeploymentExecution(value), TypeError);
  }
  for (const name of Object.keys(valid).filter(name => name !== "prepareRawInterop")) {
    assert.throws(() => createDeploymentExecution({ ...valid, [name]: null }), /action/);
  }
  assert.throws(() => createDeploymentExecution({ ...valid, prepareRawInterop: {} }), /action/);
  const symbolicFactory = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createDeploymentExecution(symbolicFactory), TypeError);
  const factoryAccessor = { ...valid };
  Object.defineProperty(factoryAccessor, "loadManifest", {
    enumerable: true, get: () => valid.loadManifest,
  });
  assert.throws(() => createDeploymentExecution(factoryAccessor), TypeError);

  const f = fixture();
  for (const value of [null, 1, [], {}, { ...execution(), extra: true }, Object.create(execution())]) {
    await assert.rejects(() => f.execute(value), TypeError);
  }
  const symbolic = { ...execution(), [Symbol("invalid")]: true };
  await assert.rejects(() => f.execute(symbolic), TypeError);
  const accessor = { ...execution() };
  Object.defineProperty(accessor, "request", { enumerable: true, get: request });
  await assert.rejects(() => f.execute(accessor), TypeError);
  for (const signal of [1, {}, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(() => f.execute(execution({ signal })), /AbortSignal/);
  }
  for (const name of ["stdout", "stderr"]) {
    for (const value of [null, {}, { write() {} }]) {
      await assert.rejects(() => f.execute(execution({ [name]: value })), /output sink/);
    }
  }
  assert.deepEqual(f.calls, []);
});

import assert from "node:assert/strict";
import test from "node:test";

import {
  createProviderExecutionComposition,
} from "./provider-execution-composition.mjs";
import { isContractError } from "./contract-error.mjs";

const digest = "1".repeat(64);
const environmentModule = "wasi:cli/environment@0.2.11";
const reactorModule = "netwasm:runtime/reactor-host@1.0.0";
const environmentSource = Object.freeze({
  getArguments: () => ["application"],
  getEnvironment: () => [],
  initialCwd: () => null,
});
class OutputStream {}
class Pollable {}
const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });

function manifest() {
  return {
    artifacts: [{
      mediaType: "application/wasm",
      relativePath: "program.wasm",
      role: "application",
      schemaVersion: null,
      sha256: digest,
    }],
    buildFingerprint: digest,
    deploymentKind: "raw",
    executionContract: "wasi-command@0.2.11",
    exports: [],
    featureSet: "default",
    profile: "netwasm0.1",
    requiredImportModules: [environmentModule, reactorModule],
    requiredImports: [
      { interface: environmentModule, name: "get-arguments", parameters: [], results: ["list<string>"] },
      { interface: environmentModule, name: "get-environment", parameters: [], results: ["list<tuple<string,string>>"] },
      { interface: environmentModule, name: "initial-cwd", parameters: [], results: ["option<string>"] },
      { interface: reactorModule, name: "watch", parameters: ["u32", "s64"], results: [] },
      { interface: reactorModule, name: "cancel", parameters: ["u32"], results: [] },
    ],
    runtimeFeatures: [],
    schemaVersion: 1,
    semanticBuildId: digest,
    target: "wasm32",
    versions: {
      compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1",
    },
  };
}

function executionRequest() {
  return {
    applicationImports: [],
    arguments: ["application"],
    buildFingerprint: digest,
    deploymentManifestSha256: digest,
    environment: [],
    grants: {
      clocks: [], environment: [], network: "denyAll", preopens: [], randomness: false,
    },
    schemaVersion: 1,
  };
}

function options(overrides = {}) {
  return {
    createShim(config) {
      assert.deepEqual(config.sandbox.args, ["application"]);
      return {
        getImportObject(request) {
          assert.deepEqual(request, { asVersion: "0.2.11" });
          return {
            [environmentModule]: environmentSource,
            "wasi:io/poll@0.2.11": Object.freeze({ Pollable }),
            "wasi:io/streams@0.2.11": Object.freeze({ OutputStream }),
          };
        },
      };
    },
    hashBytes() { assert.fail("no application provider should be hashed"); },
    importModule() { assert.fail("no application provider should be imported"); },
    isAbsoluteHostPath() { return false; },
    readArtifact() { assert.fail("no application provider should be read"); },
    ...overrides,
  };
}

test("wires the generated Preview 2 catalog into selected provider preparation", async () => {
  const prepare = createProviderExecutionComposition(options());
  assert.equal(Object.isFrozen(prepare), true);
  const result = await prepare({
    filesystem: null,
    manifest: manifest(),
    request: executionRequest(),
    signal: null,
    stderr,
    stdout,
  });

  assert.deepEqual(Object.keys(result.componentImports), ["wasi:cli/environment"]);
  assert.equal(result.componentImports["wasi:cli/environment"], environmentSource);
  assert.deepEqual(Object.keys(result.rawProviders), ["wasi:cli/environment@0.2.11"]);
  assert.equal(result.rawProviders["wasi:cli/environment@0.2.11"], environmentSource);
  assert.deepEqual(Object.keys(result.consumerModules), [environmentModule]);
  assert.equal(result.consumerModules[environmentModule], environmentSource);
  assert.deepEqual(result.binding.platformProviders.map(value => value.module), [environmentModule]);
  assert.deepEqual(result.binding.applicationProviders, []);
});

test("rejects malformed composition dependencies before creating the catalog", () => {
  const valid = options();
  for (const value of [null, 1, [], {}, { ...valid, extra: () => {} }, Object.create(valid)]) {
    assert.throws(() => createProviderExecutionComposition(value), TypeError);
  }
  for (const name of Object.keys(valid)) {
    assert.throws(() => createProviderExecutionComposition({ ...valid, [name]: null }), /action/);
  }
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createProviderExecutionComposition(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "createShim", { enumerable: true, get: () => valid.createShim });
  assert.throws(() => createProviderExecutionComposition(accessor), TypeError);
});

test("brands public validation failures without exposing dependency details", async () => {
  const prepare = createProviderExecutionComposition(options());
  await assert.rejects(() => prepare({
    filesystem: null,
    manifest: { ...manifest(), profile: "private-invalid-profile" },
    request: executionRequest(),
    signal: null,
    stderr,
    stdout,
  }), error => isContractError(error)
    && !error.message.includes("private-invalid-profile"));
});

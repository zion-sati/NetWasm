import assert from "node:assert/strict";
import test from "node:test";

import { platformProviderCatalog } from "./platform-provider-catalog.generated.mjs";
import {
  createProviderExecutionComposition,
} from "./provider-execution-composition.mjs";
import { isContractError } from "./contract-error.mjs";
import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { describeFailureCause } from "./execution-result.mjs";

const digest = "1".repeat(64);
const environmentModule = "wasi:cli/environment@0.3.2";
const environmentSource = Object.freeze({
  getArguments: () => ["application"],
  getEnvironment: () => [],
  getInitialCwd: () => undefined,
});
const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });
const stdin = Object.freeze({ read() { return [][Symbol.iterator](); } });

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
    executionContract: "wasi-command@0.3.2",
    exports: [],
    featureSet: "default",
    profile: "netwasm0.1",
    requiredImportModules: [environmentModule],
    requiredImports: [
      { interface: environmentModule, name: "get-arguments", parameters: [], results: ["list<string>"] },
      { interface: environmentModule, name: "get-environment", parameters: [], results: ["list<tuple<string,string>>"] },
      { interface: environmentModule, name: "get-initial-cwd", parameters: [], results: ["option<string>"] },
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
      clocks: [], environment: [], network: "denyAll", preopens: [], randomness: false, temporaryDirectory: false, workingDirectory: false,
    },
    schemaVersion: 1,
  };
}

function options(overrides = {}) {
  return {
    catalog: platformProviderCatalog,
    createImports(request) {
      assert.deepEqual(request.arguments, ["application"]);
      return Object.freeze({
        imports: Object.freeze({ [environmentModule]: environmentSource }),
        release: async () => {},
      });
    },
    hashBytes() { assert.fail("no application provider should be hashed"); },
    importModule() { assert.fail("no application provider should be imported"); },
    isAbsoluteHostPath() { return false; },
    readArtifact() { assert.fail("no application provider should be read"); },
    snapshotManifest: validateDeploymentManifest,
    ...overrides,
  };
}

test("wires the generated WASI 0.3 catalog into selected provider preparation", async () => {
  const prepare = createProviderExecutionComposition(options());
  assert.equal(Object.isFrozen(prepare), true);
  const result = await prepare({
    filesystem: null,
    manifest: manifest(),
    request: executionRequest(),
    signal: null,
    stderr,
    stdin,
    stdout,
  });

  assert.deepEqual(Object.keys(result.componentImports), ["wasi:cli/environment"]);
  assert.equal(result.componentImports["wasi:cli/environment"], environmentSource);
  assert.deepEqual(Object.keys(result.rawProviders), [environmentModule]);
  assert.equal(result.rawProviders[environmentModule], environmentSource);
  assert.equal(typeof result.release, "function");
  await result.release();
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
  for (const name of Object.keys(valid).filter(name => name !== "catalog")) {
    assert.throws(() => createProviderExecutionComposition({ ...valid, [name]: null }), /action/);
  }
  assert.throws(() => createProviderExecutionComposition({ ...valid, catalog: null }), /catalog/);
  const symbolic = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createProviderExecutionComposition(symbolic), TypeError);
  const accessor = { ...valid };
  Object.defineProperty(accessor, "createImports", { enumerable: true, get: () => valid.createImports });
  assert.throws(() => createProviderExecutionComposition(accessor), TypeError);
});

test("keeps an ungranted platform capability as the contract failure cause", async () => {
  const wallClockModule = "wasi:clocks/system-clock@0.3.2";
  const instant = `${wallClockModule}#instant`;
  const prepare = createProviderExecutionComposition(options({
    createImports: () => Object.freeze({
      imports: Object.freeze({ [wallClockModule]: Object.freeze({ now() {}, getResolution() {} }) }),
      release: async () => {},
    }),
  }));
  const required = manifest();
  await assert.rejects(() => prepare({
    filesystem: null,
    manifest: {
      ...required,
      requiredImportModules: [wallClockModule],
      requiredImports: [
        { interface: wallClockModule, name: "now", parameters: [], results: [instant] },
      ],
    },
    request: executionRequest(),
    signal: null,
    stderr,
    stdin,
    stdout,
  }), error => isContractError(error)
    && describeFailureCause(error) === "TypeError: platform capability 'wallClock' was not granted");
});

test("brands public validation failures and keeps their cause", async () => {
  const prepare = createProviderExecutionComposition(options());
  await assert.rejects(() => prepare({
    filesystem: null,
    manifest: { ...manifest(), profile: "private-invalid-profile" },
    request: executionRequest(),
    signal: null,
    stderr,
    stdin,
    stdout,
  }), error => isContractError(error)
    && !error.message.includes("private-invalid-profile")
    && error.cause instanceof Error);
});

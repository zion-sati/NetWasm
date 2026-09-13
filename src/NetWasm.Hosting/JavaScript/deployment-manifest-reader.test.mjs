import assert from "node:assert/strict";
import test from "node:test";

import { createDeploymentManifestReader } from "./deployment-manifest-reader.mjs";
import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const digest = "a".repeat(64);

test("deployment manifest reader composes the strict public data boundary", () => {
  const read = createDeploymentManifestReader({
    parseJson: parseStrictJson,
    validate: validateDeploymentManifest,
  });
  const result = read(JSON.stringify({
    schemaVersion: 1,
    semanticBuildId: digest,
    deploymentKind: "raw",
    profile: "netwasm0.1",
    target: "wasm64",
    featureSet: "mvp",
    executionContract: "wasi-command@0.2.11",
    versions: {
      sdk: "0.1.0",
      compiler: "0.1.0",
      runtime: "0.1.0",
      runtimeAbi: "1.0.0",
      hosting: "0.1.0",
      toolchain: "0.1.0",
    },
    buildFingerprint: digest,
    runtimeFeatures: [],
    artifacts: [{
      relativePath: "app.wasm",
      role: "application",
      mediaType: "application/wasm",
      sha256: digest,
      schemaVersion: null,
    }],
    requiredImportModules: [],
    requiredImports: [],
    exports: [],
  }));

  assert.equal(result.deploymentKind, "raw");
  assert.equal(result.target, "wasm64");
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.artifacts[0]), true);
});

test("createDeploymentManifestReader composes parse then validation", () => {
  const calls = [];
  const parsed = { schemaVersion: 1 };
  const validated = Object.freeze({ ...parsed });
  const read = createDeploymentManifestReader({
    parseJson(text) {
      calls.push(["parse", text]);
      return parsed;
    },
    validate(value) {
      calls.push(["validate", value]);
      return validated;
    },
  });

  assert.equal(Object.isFrozen(read), true);
  assert.strictEqual(read("manifest"), validated);
  assert.deepEqual(calls, [["parse", "manifest"], ["validate", parsed]]);
});

test("createDeploymentManifestReader validates its exact dependencies", () => {
  const valid = { parseJson() {}, validate() {} };
  const withSymbol = { ...valid, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), valid);
  const wrongKey = { parseJson() {}, other() {} };
  const nonEnumerable = { ...valid };
  Object.defineProperty(nonEnumerable, "validate", { value() {}, enumerable: false });
  const accessor = { ...valid };
  Object.defineProperty(accessor, "validate", { get: () => () => {}, enumerable: true });

  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => createDeploymentManifestReader(value), /options is invalid/i);
  }
  for (const value of [inherited, { ...valid, extra() {} }, wrongKey, nonEnumerable, accessor]) {
    assert.throws(() => createDeploymentManifestReader(value), /options shape/i);
  }
  assert.throws(
    () => createDeploymentManifestReader({ ...valid, parseJson: null }),
    /JSON reader is required/i);
  assert.throws(
    () => createDeploymentManifestReader({ ...valid, validate: null }),
    /validation action is required/i);
});

test("deployment manifest reader propagates parser and validator failures", () => {
  const parseFailure = new Error("parse failed");
  const validateFailure = new Error("validate failed");
  let validations = 0;
  const parseFails = createDeploymentManifestReader({
    parseJson() { throw parseFailure; },
    validate() { validations++; },
  });
  assert.throws(() => parseFails("manifest"), error => {
    assert.strictEqual(error, parseFailure);
    return true;
  });
  assert.equal(validations, 0);

  const validateFails = createDeploymentManifestReader({
    parseJson() { return {}; },
    validate() { throw validateFailure; },
  });
  assert.throws(() => validateFails("manifest"), error => {
    assert.strictEqual(error, validateFailure);
    return true;
  });
});

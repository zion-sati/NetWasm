import assert from "node:assert/strict";
import test from "node:test";

import { createExecutionDescriptorReader } from "./execution-descriptor-reader.mjs";
import {
  createExecutionDescriptorValidator,
} from "./execution-descriptor-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const digest = "a".repeat(64);
const valid = {
  schemaVersion: 1,
  buildFingerprint: digest,
  deploymentManifestPath: "/output/app.netwasm.deployment.json",
  deploymentManifestSha256: digest,
  hostingVersion: "0.1.0-preview.1",
  hostExecutablePath: "/tools/node",
  launcherPath: "/packages/hosting/tools/netwasm/hosting/launcher.mjs",
  toolPackages: [{
    id: "NetWasm.Toolchain",
    version: "0.1.0-preview.23",
    rootPath: "/packages/toolchain",
    sha256: digest,
  }],
};

test("execution descriptor reader composes the strict public data boundary", () => {
  const read = createExecutionDescriptorReader({
    parseJson: parseStrictJson,
    validate: createExecutionDescriptorValidator({
      isAbsoluteHostPath: path => path.startsWith("/"),
    }),
  });
  const result = read(JSON.stringify(valid));
  assert.deepEqual(result, valid);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.toolPackages[0]), true);
  assert.throws(
    () => read(`${JSON.stringify(valid).slice(0, -1)},"schemaVersion":1}`),
    /duplicate property 'schemaVersion'/i);
});

test("createExecutionDescriptorReader composes parse then validation", () => {
  const calls = [];
  const parsed = { schemaVersion: 1 };
  const validated = Object.freeze({ ...parsed });
  const read = createExecutionDescriptorReader({
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
  assert.strictEqual(read("descriptor"), validated);
  assert.deepEqual(calls, [["parse", "descriptor"], ["validate", parsed]]);
});

test("createExecutionDescriptorReader validates its exact dependencies", () => {
  const dependencies = { parseJson() {}, validate() {} };
  const withSymbol = { ...dependencies, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), dependencies);
  const nonEnumerable = { ...dependencies };
  Object.defineProperty(nonEnumerable, "validate", { value() {}, enumerable: false });
  const accessor = { ...dependencies };
  Object.defineProperty(accessor, "validate", { get: () => () => {}, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => createExecutionDescriptorReader(value), /options is invalid/i);
  }
  for (const value of [
    inherited,
    { ...dependencies, extra() {} },
    { parseJson() {}, other() {} },
    nonEnumerable,
    accessor,
  ]) {
    assert.throws(() => createExecutionDescriptorReader(value), /options shape/i);
  }
  assert.throws(
    () => createExecutionDescriptorReader({ ...dependencies, parseJson: null }),
    /JSON reader is required/i);
  assert.throws(
    () => createExecutionDescriptorReader({ ...dependencies, validate: null }),
    /validation action is required/i);
});

test("execution descriptor reader preserves parser and validator failures", () => {
  const parseFailure = new Error("parse failed");
  const validationFailure = new Error("validation failed");
  let validations = 0;
  const parseFails = createExecutionDescriptorReader({
    parseJson() { throw parseFailure; },
    validate() { validations++; },
  });
  assert.throws(() => parseFails("descriptor"), error => error === parseFailure);
  assert.equal(validations, 0);

  const validationFails = createExecutionDescriptorReader({
    parseJson() { return {}; },
    validate() { throw validationFailure; },
  });
  assert.throws(() => validationFails("descriptor"), error => error === validationFailure);
});

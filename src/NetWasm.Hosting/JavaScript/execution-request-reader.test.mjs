import assert from "node:assert/strict";
import test from "node:test";

import { createExecutionRequestReader } from "./execution-request-reader.mjs";
import { createExecutionRequestValidator } from "./execution-request-validator.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

const digest = "a".repeat(64);

test("execution request reader composes the strict public data boundary", () => {
  const validate = createExecutionRequestValidator({
    isAbsoluteHostPath: path => path.startsWith("/"),
    selectProviderKind,
  });
  const read = createExecutionRequestReader({ parseJson: parseStrictJson, validate });
  const result = read(JSON.stringify({
    schemaVersion: 1,
    buildFingerprint: digest,
    deploymentManifestSha256: digest,
    arguments: ["first"],
    environment: [],
    grants: {
      environment: [],
      preopens: [],
      network: "denyAll",
      clocks: [],
      randomness: false,
    },
    applicationImports: [],
  }));
  assert.deepEqual(result.arguments, ["first"]);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.grants), true);
});

test("createExecutionRequestReader composes parse then validation", () => {
  const calls = [];
  const parsed = { schemaVersion: 1 };
  const validated = Object.freeze({ ...parsed });
  const read = createExecutionRequestReader({
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
  assert.strictEqual(read("request"), validated);
  assert.deepEqual(calls, [["parse", "request"], ["validate", parsed]]);
});

test("createExecutionRequestReader validates its exact dependencies", () => {
  const valid = { parseJson() {}, validate() {} };
  const withSymbol = { ...valid, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), valid);
  const wrongKey = { parseJson() {}, other() {} };
  const nonEnumerable = { ...valid };
  Object.defineProperty(nonEnumerable, "validate", { value() {}, enumerable: false });
  const accessor = { ...valid };
  Object.defineProperty(accessor, "validate", { get: () => () => {}, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => createExecutionRequestReader(value), /options is invalid/i);
  }
  for (const value of [inherited, { ...valid, extra() {} }, wrongKey, nonEnumerable, accessor]) {
    assert.throws(() => createExecutionRequestReader(value), /options shape/i);
  }
  assert.throws(
    () => createExecutionRequestReader({ ...valid, parseJson: null }),
    /JSON reader is required/i);
  assert.throws(
    () => createExecutionRequestReader({ ...valid, validate: null }),
    /validation action is required/i);
});

test("execution request reader propagates parser and validator failures", () => {
  const parseFailure = new Error("parse failed");
  const validateFailure = new Error("validate failed");
  let validations = 0;
  const parseFails = createExecutionRequestReader({
    parseJson() { throw parseFailure; },
    validate() { validations++; },
  });
  assert.throws(() => parseFails("request"), error => {
    assert.strictEqual(error, parseFailure);
    return true;
  });
  assert.equal(validations, 0);

  const validateFails = createExecutionRequestReader({
    parseJson() { return {}; },
    validate() { throw validateFailure; },
  });
  assert.throws(() => validateFails("request"), error => {
    assert.strictEqual(error, validateFailure);
    return true;
  });
});

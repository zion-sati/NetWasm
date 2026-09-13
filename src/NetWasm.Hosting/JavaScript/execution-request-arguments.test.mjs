import assert from "node:assert/strict";
import test from "node:test";

import { appendExecutionArguments } from "./execution-request-arguments.mjs";

function request() {
  return Object.freeze({
    schemaVersion: 1,
    buildFingerprint: "build",
    deploymentManifestSha256: "manifest",
    arguments: Object.freeze(["first", ""]),
    environment: Object.freeze([]),
    grants: Object.freeze({}),
    applicationImports: Object.freeze([]),
  });
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
  return {
    invalid: [null, 1, [], withSymbol],
    malformed: [inherited, extra, missing, nonEnumerable, accessor],
  };
}

test("execution request argument appending preserves validated authority", () => {
  const validated = request();
  const appended = appendExecutionArguments(validated, ["third", ""]);
  assert.deepEqual(appended.arguments, ["first", "", "third", ""]);
  assert.equal(Object.isFrozen(appended), true);
  assert.equal(Object.isFrozen(appended.arguments), true);
  assert.strictEqual(appended.grants, validated.grants);
  assert.strictEqual(appended.environment, validated.environment);
  assert.throws(
    () => appendExecutionArguments(validated, ["bad\0argument"]),
    /without NUL/i);
  assert.throws(
    () => appendExecutionArguments(validated, null),
    /must be explicit/i);
});

test("execution request argument appending requires the validated immutable shape", () => {
  const { invalid, malformed } = malformedDataObjects(request());
  for (const value of invalid) {
    assert.throws(() => appendExecutionArguments(value, []), /is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => appendExecutionArguments(value, []), /shape is invalid/i);
  }
  assert.throws(() => appendExecutionArguments({ ...request() }, []), /not immutable/i);
  assert.throws(
    () => appendExecutionArguments(Object.freeze({
      ...request(),
      arguments: [],
    }), []),
    /not immutable/i);
});

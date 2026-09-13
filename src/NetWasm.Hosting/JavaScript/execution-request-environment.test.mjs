import assert from "node:assert/strict";
import test from "node:test";

import { overlayExecutionEnvironment } from "./execution-request-environment.mjs";

function request(overrides = {}) {
  return Object.freeze({
    schemaVersion: 1,
    buildFingerprint: "build",
    deploymentManifestSha256: "manifest",
    arguments: Object.freeze([]),
    environment: Object.freeze([
      Object.freeze({ name: "EXPLICIT", value: "request" }),
    ]),
    grants: Object.freeze({
      environment: Object.freeze(["EXPLICIT", "GRANT_ONLY"]),
      preopens: Object.freeze([]),
      network: "allowAll",
      clocks: Object.freeze(["wall", "monotonic"]),
      randomness: true,
    }),
    applicationImports: Object.freeze([]),
    ...overrides,
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

test("local environment overlay inherits values and lets explicit values win", () => {
  const original = request();
  const inherited = [
    { name: "ZETA", value: "last" },
    { name: "EXPLICIT", value: "process" },
    { name: "ALPHA", value: "first" },
  ];
  const result = overlayExecutionEnvironment(original, inherited);

  assert.deepEqual(result.environment, [
    { name: "ALPHA", value: "first" },
    { name: "EXPLICIT", value: "request" },
    { name: "ZETA", value: "last" },
  ]);
  assert.deepEqual(result.grants.environment, [
    "ALPHA", "EXPLICIT", "GRANT_ONLY", "ZETA",
  ]);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.environment), true);
  assert.equal(Object.isFrozen(result.environment[0]), true);
  assert.equal(Object.isFrozen(result.grants), true);
  assert.equal(Object.isFrozen(result.grants.environment), true);
  assert.strictEqual(result.arguments, original.arguments);
  assert.strictEqual(result.grants.preopens, original.grants.preopens);
  assert.deepEqual(inherited[0], { name: "ZETA", value: "last" });
});

test("local environment overlay accepts an empty process environment", () => {
  const original = request();
  const result = overlayExecutionEnvironment(original, []);
  assert.deepEqual(result.environment, original.environment);
  assert.deepEqual(result.grants.environment, original.grants.environment);
  assert.notStrictEqual(result.environment, original.environment);
  assert.notStrictEqual(result.grants, original.grants);
});

test("local environment overlay rejects malformed and duplicate inherited values", () => {
  for (const environment of [
    null,
    [{ name: "", value: "value" }],
    [{ name: "BAD=NAME", value: "value" }],
    [{ name: "BAD\0NAME", value: "value" }],
    [{ name: "NAME", value: null }],
    [{ name: "NAME", value: "bad\0value" }],
    [{ name: "NAME", value: "one" }, { name: "NAME", value: "two" }],
    [{ name: "NAME", value: "value", extra: true }],
  ]) {
    assert.throws(
      () => overlayExecutionEnvironment(request(), environment),
      /environment|duplicated|shape/i);
  }
});

test("local environment overlay requires the validated immutable request shape", () => {
  const { invalid, malformed } = malformedDataObjects(request());
  for (const value of invalid) {
    assert.throws(() => overlayExecutionEnvironment(value, []), /is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => overlayExecutionEnvironment(value, []), /shape is invalid/i);
  }
  assert.throws(() => overlayExecutionEnvironment({ ...request() }, []), /not immutable/i);
  assert.throws(
    () => overlayExecutionEnvironment(Object.freeze({
      ...request(),
      environment: [],
    }), []),
    /not immutable/i);
});

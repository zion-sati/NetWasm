import assert from "node:assert/strict";
import test from "node:test";

import {
  appendExecutionArguments,
  createExecutionRequestValidator,
} from "./execution-request-validator.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";

const digest = character => character.repeat(64);

function request(overrides = {}) {
  return {
    schemaVersion: 1,
    buildFingerprint: digest("a"),
    deploymentManifestSha256: digest("b"),
    arguments: ["first", ""],
    environment: [{ name: "ALPHA", value: "one" }],
    grants: {
      environment: ["ALPHA", "UNUSED"],
      preopens: [{ hostPath: "/host/data", guestPath: "/data", access: "readOnly" }],
      network: "denyAll",
      clocks: ["wall", "monotonic"],
      randomness: false,
    },
    applicationImports: [{
      module: "example:logging/logger@1.0.0",
      artifactPath: "imports/logger.mjs",
      sha256: digest("c"),
    }],
    ...overrides,
  };
}

function createValidator(predicate = value => value.startsWith("/")) {
  return createExecutionRequestValidator({
    isAbsoluteHostPath: predicate,
    selectProviderKind,
  });
}

function expectInvalid(value, pattern, validator = createValidator()) {
  assert.throws(() => validator(value), pattern);
}

test("execution request validator snapshots every supported authority value", () => {
  for (const network of ["denyAll", "allowAll"]) {
    for (const access of ["readOnly", "readWrite"]) {
      const source = request({
        grants: {
          ...request().grants,
          network,
          preopens: [{ hostPath: "/host/data", guestPath: "/", access }],
          randomness: true,
        },
      });
      const result = createValidator()(source);
      assert.equal(result.grants.network, network);
      assert.equal(result.grants.preopens[0].access, access);
      assert.equal(result.grants.randomness, true);
      assert.equal(Object.isFrozen(result), true);
      assert.equal(Object.isFrozen(result.arguments), true);
      assert.equal(Object.isFrozen(result.environment[0]), true);
      assert.equal(Object.isFrozen(result.grants), true);
      assert.equal(Object.isFrozen(result.grants.preopens[0]), true);
      assert.equal(Object.isFrozen(result.applicationImports[0]), true);
      assert.notStrictEqual(result.grants, source.grants);
      assert.notStrictEqual(result.applicationImports, source.applicationImports);
    }
  }

  const empty = request({
    arguments: [],
    environment: [],
    grants: {
      environment: [],
      preopens: [],
      network: "denyAll",
      clocks: [],
      randomness: false,
    },
    applicationImports: [],
  });
  assert.deepEqual(createValidator()(empty).applicationImports, []);

  const nullPrototype = Object.assign(Object.create(null), request());
  assert.equal(createValidator()(nullPrototype).schemaVersion, 1);
});

test("execution request argument appending preserves validated authority", () => {
  const validated = createValidator()(request());
  const appended = appendExecutionArguments(validated, ["third", ""]);
  assert.deepEqual(appended.arguments, ["first", "", "third", ""]);
  assert.equal(Object.isFrozen(appended), true);
  assert.equal(Object.isFrozen(appended.arguments), true);
  assert.strictEqual(appended.grants, validated.grants);
  assert.throws(
    () => appendExecutionArguments(validated, ["bad\0argument"]),
    /without NUL/i);
  assert.throws(
    () => appendExecutionArguments(request(), []),
    /not immutable/i);
});

test("createExecutionRequestValidator validates its exact dependencies", () => {
  const valid = { isAbsoluteHostPath() {}, selectProviderKind() {} };
  const withSymbol = { ...valid, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), valid);
  const wrongKey = { other() {} };
  const nonEnumerable = { ...valid };
  Object.defineProperty(nonEnumerable, "isAbsoluteHostPath", { value() {}, enumerable: false });
  const accessor = {};
  Object.defineProperty(accessor, "isAbsoluteHostPath", { get: () => () => {}, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => createExecutionRequestValidator(value), /options is invalid/i);
  }
  for (const value of [inherited, { ...valid, extra() {} }, wrongKey, nonEnumerable, accessor]) {
    assert.throws(() => createExecutionRequestValidator(value), /options shape/i);
  }
  assert.throws(
    () => createExecutionRequestValidator({ ...valid, isAbsoluteHostPath: null }),
    /predicate.*selector are required/i);
  assert.throws(
    () => createExecutionRequestValidator({ ...valid, selectProviderKind: null }),
    /predicate.*selector are required/i);
});

test("execution request validator rejects invalid root identity and arguments", () => {
  const withSymbol = request();
  withSymbol[Symbol("hidden")] = true;
  const inherited = Object.assign(Object.create({ inherited: true }), request());
  const extra = { ...request(), extra: true };
  const wrongKey = request();
  delete wrongKey.arguments;
  wrongKey.argv = [];
  const nonEnumerable = request();
  Object.defineProperty(nonEnumerable, "arguments", { value: [], enumerable: false });
  const accessor = request();
  Object.defineProperty(accessor, "arguments", { get: () => [], enumerable: true });
  for (const value of [null, 1, [], withSymbol]) expectInvalid(value, /request is invalid/i);
  for (const value of [inherited, extra, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(value, /request shape/i);
  }
  for (const schemaVersion of [0, 2]) {
    expectInvalid(request({ schemaVersion }), /schema is unsupported/i);
  }
  for (const key of ["buildFingerprint", "deploymentManifestSha256"]) {
    for (const value of [null, "", digest("A"), digest("g"), "a".repeat(63)]) {
      expectInvalid(request({ [key]: value }), /lowercase SHA-256/i);
    }
  }
  expectInvalid(request({ arguments: null }), /arguments must be explicit/i);
  for (const argument of [null, 1, "bad\0argument"]) {
    expectInvalid(request({ arguments: [argument] }), /argument must be text without NUL/i);
  }
});

test("execution request validator rejects malformed grant objects and collections", () => {
  const base = request().grants;
  const withSymbol = { ...base, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), base);
  const wrongKey = { ...base };
  delete wrongKey.network;
  wrongKey.otherNetwork = "denyAll";
  const nonEnumerable = { ...base };
  Object.defineProperty(nonEnumerable, "network", { value: "denyAll", enumerable: false });
  const accessor = { ...base };
  Object.defineProperty(accessor, "network", { get: () => "denyAll", enumerable: true });
  for (const grants of [null, 1, [], withSymbol]) {
    expectInvalid(request({ grants }), /grants is invalid/i);
  }
  for (const grants of [inherited, { ...base, extra: true }, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(request({ grants }), /grants shape/i);
  }
  for (const network of [null, "", "future"]) {
    expectInvalid(request({ grants: { ...base, network } }), /network policy/i);
  }
  for (const randomness of [null, 0, "false"]) {
    expectInvalid(request({ grants: { ...base, randomness } }), /randomness grant/i);
  }
  for (const key of ["environment", "preopens", "clocks"]) {
    expectInvalid(request({ grants: { ...base, [key]: null } }), /collections must be explicit/i);
  }
});

test("execution request validator enforces environment grants and values", () => {
  const base = request().grants;
  for (const name of [null, "", "BAD=NAME", "BAD\0NAME"]) {
    expectInvalid(request({ grants: { ...base, environment: [name] } }), /environment name/i);
  }
  expectInvalid(request({ grants: { ...base, environment: ["ALPHA", "ALPHA"] } }), /granted environment name.*duplicated/i);
  expectInvalid(request({ environment: null }), /environment must be explicit/i);

  const variable = { name: "ALPHA", value: "one" };
  const withSymbol = { ...variable, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), variable);
  const wrongKey = { name: "ALPHA", otherValue: "one" };
  const nonEnumerable = { ...variable };
  Object.defineProperty(nonEnumerable, "value", { value: "one", enumerable: false });
  const accessor = { ...variable };
  Object.defineProperty(accessor, "value", { get: () => "one", enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    expectInvalid(request({ environment: [value] }), /variable is invalid/i);
  }
  for (const value of [inherited, { ...variable, extra: true }, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(request({ environment: [value] }), /variable shape/i);
  }
  for (const name of [null, "", "BAD=NAME", "BAD\0NAME"]) {
    expectInvalid(request({ environment: [{ ...variable, name }] }), /environment name/i);
  }
  for (const value of [null, 1, "bad\0value"]) {
    expectInvalid(request({ environment: [{ ...variable, value }] }), /environment value/i);
  }
  expectInvalid(request({ environment: [variable, { ...variable }] }), /environment name.*duplicated/i);
  expectInvalid(request({ environment: [{ name: "BETA", value: "two" }] }), /was not granted/i);
});

test("execution request validator enforces preopen structure, paths and uniqueness", () => {
  const grants = request().grants;
  const preopen = grants.preopens[0];
  const withSymbol = { ...preopen, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), preopen);
  const wrongKey = { hostPath: "/host/data", guestPath: "/data", otherAccess: "readOnly" };
  const nonEnumerable = { ...preopen };
  Object.defineProperty(nonEnumerable, "access", { value: "readOnly", enumerable: false });
  const accessor = { ...preopen };
  Object.defineProperty(accessor, "access", { get: () => "readOnly", enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    expectInvalid(request({ grants: { ...grants, preopens: [value] } }), /preopen grant is invalid/i);
  }
  for (const value of [inherited, { ...preopen, extra: true }, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(request({ grants: { ...grants, preopens: [value] } }), /preopen grant shape/i);
  }

  let calls = 0;
  const validator = createValidator(value => {
    calls++;
    return value === "/host/data";
  });
  assert.equal(validator(request()).grants.preopens[0].hostPath, "/host/data");
  assert.equal(calls, 1);
  for (const hostPath of [null, "", " ", "relative", "/bad\0path"]) {
    expectInvalid(request({ grants: { ...grants, preopens: [{ ...preopen, hostPath }] } }), /host path/i);
  }
  expectInvalid(
    request({ grants: { ...grants, preopens: [{ ...preopen, hostPath: "/denied" }] } }),
    /host path/i,
    createValidator(() => false));

  for (const guestPath of [
    null, "", "relative", " /padded", "/padded ", "/bad\\path", "/bad\0path",
    "//", "/a//b", "/.", "/..", "/a/./b", "/a/../b",
  ]) {
    expectInvalid(request({ grants: { ...grants, preopens: [{ ...preopen, guestPath }] } }), /guest preopen path/i);
  }
  for (const access of [null, "", "execute"]) {
    expectInvalid(request({ grants: { ...grants, preopens: [{ ...preopen, access }] } }), /preopen access/i);
  }
  expectInvalid(request({ grants: { ...grants, preopens: [preopen, { ...preopen, hostPath: "/other" }] } }), /guest preopen path.*duplicated/i);
});

test("execution request validator enforces unique supported clocks", () => {
  const grants = request().grants;
  for (const clock of [null, "", "cpu"]) {
    expectInvalid(request({ grants: { ...grants, clocks: [clock] } }), /clock grant is unsupported/i);
  }
  expectInvalid(request({ grants: { ...grants, clocks: ["wall", "wall"] } }), /clock grant.*duplicated/i);
});

test("execution request validator enforces application import bindings", () => {
  expectInvalid(request({ applicationImports: null }), /bindings must be explicit/i);
  const binding = request().applicationImports[0];
  const withSymbol = { ...binding, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), binding);
  const wrongKey = { module: binding.module, artifactPath: binding.artifactPath, digest: binding.sha256 };
  const nonEnumerable = { ...binding };
  Object.defineProperty(nonEnumerable, "sha256", { value: binding.sha256, enumerable: false });
  const accessor = { ...binding };
  Object.defineProperty(accessor, "sha256", { get: () => binding.sha256, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    expectInvalid(request({ applicationImports: [value] }), /binding is invalid/i);
  }
  for (const value of [inherited, { ...binding, extra: true }, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(request({ applicationImports: [value] }), /binding shape/i);
  }
  for (const module of [null, "", "Module@1.0.0", "module@1.0", "bad_module@1.0.0"]){
    expectInvalid(request({ applicationImports: [{ ...binding, module }] }), /exact versioned identifier/i);
  }
  for (const module of ["wasi:logging/logger@1.0.0", "netwasm:platform/logging@1.0.0"]) {
    expectInvalid(request({ applicationImports: [{ ...binding, module }] }), /reserved/i);
  }
  expectInvalid(request({ applicationImports: [binding, { ...binding, artifactPath: "imports/other.mjs" }] }), /module.*duplicated/i);
  for (const artifactPath of [
    null, "", "/rooted.mjs", "C:/rooted.mjs", "nested\\file.mjs", "bad\0path",
    " padded.mjs", "padded.mjs ", "a//b", "./app.mjs", "a/../b",
  ]) {
    expectInvalid(request({ applicationImports: [{ ...binding, artifactPath }] }), /artifact path/i);
  }
  for (const sha256 of [null, "", digest("A"), digest("g"), "a".repeat(63)]) {
    expectInvalid(request({ applicationImports: [{ ...binding, sha256 }] }), /application import digest/i);
  }

  expectInvalid(
    request(),
    /selector returned an unsupported kind/i,
    createExecutionRequestValidator({
      isAbsoluteHostPath: value => value.startsWith("/"),
      selectProviderKind: () => "future",
    }));
});

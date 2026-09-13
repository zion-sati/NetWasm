import assert from "node:assert/strict";
import test from "node:test";

import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";

const platformModule = "wasi:cli/environment@0.2.11";
const applicationModule = "example:logging/logger@1.0.0";

function func(module, name = "call", parameters = [], results = []) {
  return { interface: module, name, parameters, results };
}

function platform(overrides = {}) {
  return {
    module: platformModule,
    capability: "baseline",
    functions: [func(platformModule, "get-arguments", [], ["list<string>"])],
    ...overrides,
  };
}

function application(overrides = {}) {
  return {
    module: applicationModule,
    functions: [func(applicationModule, "log", ["string"])],
    ...overrides,
  };
}

function createValidator(selector = selectProviderKind) {
  return createProviderMetadataValidator({ selectProviderKind: selector });
}

function validate(kind, provider, validator = createValidator()) {
  return validator({ kind, provider });
}

test("validates and snapshots exact platform and application metadata", () => {
  for (const capability of [
    "baseline",
    "environment",
    "monotonicClock",
    "network",
    "preopenedDirectories",
    "randomness",
    "wallClock",
  ]) {
    const source = platform({ capability });
    const result = validate("platform", source);
    assert.equal(result.capability, capability);
    assert.equal(Object.isFrozen(result), true);
    assert.equal(Object.isFrozen(result.functions), true);
    assert.equal(Object.isFrozen(result.functions[0]), true);
    assert.equal(Object.isFrozen(result.functions[0].parameters), true);
    assert.equal(Object.isFrozen(result.functions[0].results), true);
    assert.notStrictEqual(result, source);
  }

  const source = application();
  const result = validate("application", source);
  assert.deepEqual(result, source);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.hasOwn(result, "capability"), false);
});

test("validates the exact provider-kind dependency", () => {
  const valid = { selectProviderKind() {} };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createProviderMetadataValidator(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  assert.throws(
    () => createProviderMetadataValidator({ selectProviderKind: null }),
    /selector is required/i);
});

test("rejects invalid validation requests and kinds", () => {
  const valid = { kind: "platform", provider: platform() };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createValidator()(value.value),
      value.shape ? /request shape/i : /request is invalid/i);
  }
  for (const kind of [null, "", "future", "Platform"]) {
    assert.throws(() => validate(kind, platform()), /kind is unsupported/i);
  }
});

test("enforces exact provider shapes, identities and ownership", () => {
  for (const [kind, source, label] of [
    ["platform", platform(), /platform provider/],
    ["application", application(), /application provider/],
  ]) {
    for (const value of invalidObjects(source)) {
      assert.throws(
        () => validate(kind, value.value),
        value.shape ? new RegExp(`${label.source} shape`, "i") : new RegExp(`${label.source} is invalid`, "i"));
    }
  }

  for (const module of [
    null,
    "",
    "Module@1.0.0",
    "module@1.0",
    "bad_module/interface@1.0.0",
    "example:logging/logger@01.0.0",
    "example:logging/first/second@1.0.0",
  ]) {
    assert.throws(
      () => validate("platform", platform({ module })),
      /canonical exact versioned identifier/i);
  }
  assert.throws(
    () => validate("platform", {
      ...platform(),
      module: applicationModule,
      functions: [func(applicationModule)],
    }),
    /must be reserved/i);
  assert.throws(
    () => validate("application", {
      ...application(),
      module: platformModule,
      functions: [func(platformModule)],
    }),
    /must not be reserved/i);
  for (const capability of [null, "", "future"]) {
    assert.throws(
      () => validate("platform", platform({ capability })),
      /capability is unsupported/i);
  }
  assert.throws(
    () => validate("platform", platform(), createValidator(() => "future")),
    /selector returned an unsupported kind/i);
});

test("enforces exact provider function inventories and signatures", () => {
  assert.throws(
    () => validate("platform", platform({ functions: null })),
    /functions must be explicit/i);
  assert.deepEqual(validate("platform", platform({ functions: [] })).functions, []);
  const base = func(platformModule, "get-arguments");
  for (const value of invalidObjects(base)) {
    assert.throws(
      () => validate("platform", platform({ functions: [value.value] })),
      value.shape ? /provider function shape/i : /provider function is invalid/i);
  }
  assert.throws(
    () => validate("platform", platform({ functions: [func(applicationModule)] })),
    /does not belong/i);
  for (const name of [
    null, "", "Call", "bad_name", "[method]descriptor", "[method].read",
    "[method]descriptor.read.more", "[static]Descriptor.open",
    "[constructor]descriptor.open", "[resource-drop]", "[resource-drop]Descriptor",
    "[export-resource-new]descriptor.more",
  ]) {
    assert.throws(
      () => validate("platform", platform({ functions: [func(platformModule, name)] })),
      /canonical WIT identity/i);
  }
  for (const name of [
    "[resource-drop]descriptor",
    "[resource-dtor]descriptor",
    "[export-resource-new]descriptor",
    "[export-resource-rep]descriptor",
    "[export-resource-drop]descriptor",
  ]) {
    const value = platform({ functions: [func(platformModule, name)] });
    assert.equal(validate("platform", value).functions[0].name, name);
  }
  for (const name of [
    "[constructor]descriptor",
    "[method]descriptor.read-via-stream",
    "[static]descriptor.open-at",
  ]) {
    assert.equal(validate("platform", platform({
      functions: [func(platformModule, name)],
    })).functions[0].name, name);
  }
  assert.throws(
    () => validate("platform", platform({ functions: [base, { ...base }] })),
    /provider function.*duplicated/i);
  for (const key of ["parameters", "results"]) {
    assert.throws(
      () => validate("platform", platform({ functions: [{ ...base, [key]: null }] })),
      /signature must be explicit/i);
    for (const item of [null, "", " padded", "bad\0value", "bad\u007fvalue"]) {
      assert.throws(
        () => validate("platform", platform({ functions: [{ ...base, [key]: [item] }] })),
        /signature value/i);
    }
  }
});

function invalidObjects(base) {
  const withSymbol = { ...base, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), base);
  const keys = Object.keys(base);
  const wrongKey = { ...base };
  delete wrongKey[keys[0]];
  wrongKey.other = null;
  const nonEnumerable = { ...base };
  Object.defineProperty(nonEnumerable, keys[0], { value: base[keys[0]], enumerable: false });
  const accessor = { ...base };
  Object.defineProperty(accessor, keys[0], { get: () => base[keys[0]], enumerable: true });
  return [
    { value: null, shape: false },
    { value: 1, shape: false },
    { value: [], shape: false },
    { value: withSymbol, shape: false },
    { value: inherited, shape: true },
    { value: { ...base, extra: true }, shape: true },
    { value: wrongKey, shape: true },
    { value: nonEnumerable, shape: true },
    { value: accessor, shape: true },
  ];
}

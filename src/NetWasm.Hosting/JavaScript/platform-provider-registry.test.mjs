import assert from "node:assert/strict";
import test from "node:test";

import { createPlatformProviderRegistry } from "./platform-provider-registry.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";

const modules = ["wasi:cli/environment@0.2.11", "wasi:cli/exit@0.2.11"];

function provider(index = 0) {
  return {
    module: modules[index],
    capability: "baseline",
    functions: [{
      interface: modules[index],
      name: index === 0 ? "get-arguments" : "exit",
      parameters: [],
      results: [],
    }],
  };
}

function registrations() {
  return modules.map((module, index) => ({
    provider: provider(index),
    createSource: () => ({ module }),
  }));
}

function createRegistry(values = registrations(), overrides = {}) {
  return createPlatformProviderRegistry({
    registrations: values,
    validateProviderMetadata: createProviderMetadataValidator({ selectProviderKind }),
    ...overrides,
  });
}

test("resolves immutable exact registrations without invoking source factories", () => {
  let sourceCalls = 0;
  const values = registrations();
  values[0].createSource = () => { sourceCalls++; return {}; };
  const registry = createRegistry(values);
  assert.equal(Object.isFrozen(registry), true);
  const registration = registry.resolve({ module: modules[0] });
  assert.equal(Object.isFrozen(registration), true);
  assert.equal(Object.isFrozen(registration.provider), true);
  assert.equal(Object.isFrozen(registration.provider.functions), true);
  assert.strictEqual(registration.createSource, values[0].createSource);
  assert.equal(sourceCalls, 0);

  values[0].provider.module = "wasi:cli/changed@0.2.11";
  values[0].provider.functions.length = 0;
  values.length = 0;
  assert.equal(registry.resolve({ module: modules[0] }).provider.module, modules[0]);
  assert.equal(registry.resolve({ module: modules[0] }).provider.functions.length, 1);
});

test("validates exact registry dependencies and registration collections", () => {
  const valid = {
    registrations: registrations(),
    validateProviderMetadata() {},
  };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createPlatformProviderRegistry(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  assert.throws(
    () => createPlatformProviderRegistry({ ...valid, registrations: null }),
    /registrations are required/i);
  assert.throws(
    () => createPlatformProviderRegistry({ ...valid, validateProviderMetadata: null }),
    /validation action is required/i);
  assert.throws(() => createRegistry([]), /must not be empty/i);
});

test("rejects malformed, application-owned and duplicate registrations", () => {
  const base = registrations()[0];
  for (const value of invalidObjects(base)) {
    assert.throws(
      () => createRegistry([value.value]),
      value.shape ? /registration shape/i : /registration is invalid/i);
  }
  assert.throws(
    () => createRegistry([{ ...base, createSource: null }]),
    /source factory is required/i);
  assert.throws(
    () => createRegistry([{
      createSource() {},
      provider: {
        module: "example:logging/logger@1.0.0",
        capability: "baseline",
        functions: [{
          interface: "example:logging/logger@1.0.0",
          name: "log",
          parameters: [],
          results: [],
        }],
      },
    }]),
    /must be reserved/i);
  const values = registrations();
  values.push({ ...values[0] });
  assert.throws(() => createRegistry(values), /module.*duplicated/i);

  const failure = new Error("metadata rejected");
  assert.throws(
    () => createRegistry([base], { validateProviderMetadata() { throw failure; } }),
    error => error === failure);
});

test("rejects malformed metadata-validator products", () => {
  const base = registrations()[0];
  const valid = createProviderMetadataValidator({ selectProviderKind })({
    kind: "platform",
    provider: base.provider,
  });
  for (const product of [
    null,
    Object.freeze({ module: modules[0] }),
    { ...valid },
    Object.freeze({ ...valid, module: "" }),
    Object.freeze({ ...valid, capability: "" }),
    Object.freeze({ ...valid, functions: [...valid.functions] }),
    Object.freeze({ ...valid, functions: Object.freeze([{ ...valid.functions[0] }]) }),
  ]) {
    assert.throws(
      () => createRegistry([base], { validateProviderMetadata: () => product }),
      /validated platform provider metadata/i);
  }
});

test("rejects invalid and unsupported exact lookups without fallback", () => {
  const registry = createRegistry();
  const valid = { module: modules[0] };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => registry.resolve(value.value),
      value.shape ? /lookup request shape/i : /lookup request is invalid/i);
  }
  for (const module of [null, 1, ""]) {
    assert.throws(() => registry.resolve({ module }), /module is required/i);
  }
  for (const module of ["WASI:cli/environment@0.2.11", "wasi:cli/missing@0.2.11"]) {
    assert.throws(() => registry.resolve({ module }), /unsupported/i);
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

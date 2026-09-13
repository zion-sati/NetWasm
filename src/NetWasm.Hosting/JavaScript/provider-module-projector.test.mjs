import assert from "node:assert/strict";
import test from "node:test";

import { createProviderModuleProjector } from "./provider-module-projector.mjs";

const applicationModule = "example:logging/logger@1.0.0";
const platformModule = "wasi:cli/environment@0.2.11";

function provider(module, platform = false) {
  const value = { module, functions: [] };
  if (platform) value.capability = "environment";
  return value;
}

function identity({ module }) {
  const separator = module.lastIndexOf("@");
  const path = module.lastIndexOf("/");
  const packageName = module.slice(0, path);
  const interfaceName = module.slice(path + 1, separator);
  return Object.freeze({
    componentModule: `${packageName}/${interfaceName}`,
    rawModule: module,
  });
}

function createProjector(overrides = {}) {
  return createProviderModuleProjector({ projectIdentity: identity, ...overrides });
}

function request() {
  const application = Object.freeze({ log() {} });
  const platform = Object.freeze({ getEnvironment() { return []; } });
  return {
    value: {
      providers: [provider(platformModule, true), provider(applicationModule)],
      sources: [
        { module: platformModule, value: platform },
        { module: applicationModule, value: application },
      ],
    },
    application,
    platform,
  };
}

test("projects only selected sources into immutable component and raw maps", () => {
  const data = request();
  const project = createProjector();
  assert.equal(Object.isFrozen(project), true);
  const result = project(data.value);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.componentImports), true);
  assert.equal(Object.isFrozen(result.consumerModules), true);
  assert.equal(Object.isFrozen(result.rawProviders), true);
  assert.equal(Object.getPrototypeOf(result.componentImports), null);
  assert.equal(Object.getPrototypeOf(result.consumerModules), null);
  assert.equal(Object.getPrototypeOf(result.rawProviders), null);
  assert.strictEqual(result.componentImports["wasi:cli/environment"], data.platform);
  assert.strictEqual(result.componentImports["example:logging/logger"], data.application);
  assert.strictEqual(result.rawProviders["wasi:cli/environment@0.2.11"], data.platform);
  assert.strictEqual(result.rawProviders["example:logging/logger@1.0.0"], data.application);
  assert.strictEqual(result.consumerModules[platformModule], data.platform);
  assert.strictEqual(result.consumerModules[applicationModule], data.application);
  data.value.providers.length = 0;
  data.value.sources.length = 0;
  assert.equal(Object.keys(result.componentImports).length, 2);

  const empty = project({ providers: [], sources: [] });
  assert.deepEqual(Object.keys(empty.componentImports), []);
  assert.deepEqual(Object.keys(empty.consumerModules), []);
  assert.deepEqual(Object.keys(empty.rawProviders), []);
});

test("validates the exact projector dependency", () => {
  const valid = { projectIdentity: identity };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createProviderModuleProjector(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  assert.throws(
    () => createProviderModuleProjector({ projectIdentity: null }),
    /projection action is required/i);
});

test("validates exact requests and explicit collections", () => {
  const valid = request().value;
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createProjector()(value.value),
      value.shape ? /request shape/i : /request is invalid/i);
  }
  for (const key of ["providers", "sources"]) {
    assert.throws(
      () => createProjector()({ ...valid, [key]: null }),
      /collections must be explicit/i);
  }
});

test("rejects malformed and duplicate runtime sources", () => {
  const base = request().value.sources[0];
  for (const item of invalidObjects(base)) {
    const value = request().value;
    value.sources = [item.value, value.sources[1]];
    assert.throws(
      () => createProjector()(value),
      item.shape ? /source shape/i : /source is invalid/i);
  }
  for (const module of [null, ""]) {
    const value = request().value;
    value.sources[0] = { ...value.sources[0], module };
    assert.throws(() => createProjector()(value), /source module is invalid/i);
  }
  for (const sourceValue of [null, 1, [], () => {}]) {
    const value = request().value;
    value.sources[0] = { ...value.sources[0], value: sourceValue };
    assert.throws(() => createProjector()(value), /source value is invalid/i);
  }
  const duplicate = request().value;
  duplicate.sources.push({ ...duplicate.sources[0] });
  assert.throws(() => createProjector()(duplicate), /source.*duplicated/i);
});

test("rejects malformed, duplicate, missing and unselected provider metadata", () => {
  for (const selected of [null, 1, [], { module: platformModule }, {
    module: platformModule,
    functions: [],
    capability: "environment",
    extra: true,
  }]) {
    const value = request().value;
    value.providers[0] = selected;
    assert.throws(() => createProjector()(value), /provider metadata/i);
  }
  const symbol = provider(platformModule, true);
  symbol[Symbol("hidden")] = true;
  const symbolValue = request().value;
  symbolValue.providers[0] = symbol;
  assert.throws(() => createProjector()(symbolValue), /provider metadata is invalid/i);

  const accessor = { functions: [], capability: "environment" };
  Object.defineProperty(accessor, "module", { get: () => platformModule, enumerable: true });
  const accessorValue = request().value;
  accessorValue.providers[0] = accessor;
  assert.throws(() => createProjector()(accessorValue), /provider metadata shape/i);
  const inheritedValue = request().value;
  inheritedValue.providers[0] = Object.assign(
    Object.create({ inherited: true }),
    inheritedValue.providers[0]);
  assert.throws(() => createProjector()(inheritedValue), /provider metadata shape/i);

  const duplicate = request().value;
  duplicate.providers.push({ ...duplicate.providers[0] });
  assert.throws(() => createProjector()(duplicate), /provider module.*duplicated/i);
  const missing = request().value;
  missing.sources = missing.sources.slice(1);
  assert.throws(() => createProjector()(missing), /has no runtime source/i);
  const extra = request().value;
  extra.providers = extra.providers.slice(1);
  assert.throws(() => createProjector()(extra), /source was not selected/i);
});

test("validates projected identities and rejects host-name collisions", () => {
  const failure = new Error("identity failed");
  assert.throws(
    () => createProjector({ projectIdentity() { throw failure; } })(request().value),
    error => error === failure);
  for (const projected of [null, 1, [], {}, {
    componentModule: "component",
    rawModule: "raw",
    extra: true,
  }]) {
    assert.throws(
      () => createProjector({ projectIdentity: () => projected })(request().value),
      /projected provider identity/i);
  }
  for (const projected of [
    { componentModule: null, rawModule: "raw" },
    { componentModule: "", rawModule: "raw" },
    { componentModule: "component", rawModule: null },
    { componentModule: "component", rawModule: "" },
  ]) {
    assert.throws(
      () => createProjector({ projectIdentity: () => projected })(request().value),
      /identity is invalid/i);
  }
  for (const key of ["componentModule", "rawModule"]) {
    const calls = [];
    assert.throws(() => createProjector({
      projectIdentity({ module }) {
        calls.push(module);
        return {
          componentModule: key === "componentModule" ? "same" : `component-${calls.length}`,
          rawModule: key === "rawModule" ? "same" : `raw-${calls.length}`,
        };
      },
    })(request().value), /collides after projection/i);
    assert.deepEqual(calls, [platformModule, applicationModule]);
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

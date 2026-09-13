import assert from "node:assert/strict";
import test from "node:test";

import { createCapabilityBindingValidator } from "./capability-binding-validator.mjs";
import { isInternalImport } from "./internal-import-policy.mjs";
import { createPlatformProviderRegistry } from "./platform-provider-registry.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataSelector } from "./provider-metadata-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";

const platformModule = "wasi:cli/environment@0.2.11";
const applicationModule = "example:logging/logger@1.0.0";
const extraApplicationModule = "example:metrics/counter@1.0.0";

function func(module, name, parameters = [], results = []) {
  return { interface: module, name, parameters, results };
}

function createRegistry(createSource = () => ({})) {
  return createPlatformProviderRegistry({
    registrations: [{
      createSource,
      provider: {
        module: platformModule,
        capability: "baseline",
        functions: [
          func(platformModule, "get-arguments", [], ["list<string>"]),
          func(platformModule, "get-environment", [], ["list<tuple<string,string>>"]),
        ],
      },
    }],
    validateProviderMetadata: createProviderMetadataValidator({ selectProviderKind }),
  });
}

function data() {
  return {
    manifest: {
      buildFingerprint: "f".repeat(64),
      requiredImportModules: [platformModule, applicationModule],
      requiredImports: [
        func(platformModule, "get-arguments", [], ["list<string>"]),
        func(platformModule, "get-environment", [], ["list<tuple<string,string>>"]),
        func(applicationModule, "log", ["string"]),
      ],
      artifacts: [{
        relativePath: "imports/logger.mjs",
        role: "application-import",
        mediaType: "text/javascript",
        sha256: "a".repeat(64),
      }],
    },
    request: {
      buildFingerprint: "f".repeat(64),
      grants: {
        environment: [],
        preopens: [],
        network: "denyAll",
        clocks: [],
        randomness: false,
      },
      applicationImports: [{
        module: applicationModule,
        artifactPath: "imports/logger.mjs",
        sha256: "a".repeat(64),
      }],
    },
  };
}

function createSelector(overrides = {}) {
  const registry = createRegistry();
  return createProviderMetadataSelector({
    isInternalImport,
    resolvePlatformProvider: request => registry.resolve(request),
    selectProviderKind,
    ...overrides,
  });
}

test("selects first-occurrence required metadata without loading sources", () => {
  let resolves = 0;
  let sourceCalls = 0;
  const registry = createRegistry(() => { sourceCalls++; return {}; });
  const source = data();
  const select = createSelector({
    resolvePlatformProvider(request) {
      resolves++;
      return registry.resolve(request);
    },
  });
  const result = select(source);
  assert.equal(Object.isFrozen(select), true);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.platformProviders), true);
  assert.equal(Object.isFrozen(result.applicationProviders), true);
  assert.equal(result.platformProviders.length, 1);
  assert.equal(result.applicationProviders.length, 1);
  assert.equal(result.platformProviders[0].module, platformModule);
  assert.deepEqual(result.applicationProviders[0].functions, [
    func(applicationModule, "log", ["string"]),
  ]);
  assert.equal(Object.isFrozen(result.applicationProviders[0]), true);
  assert.equal(Object.isFrozen(result.applicationProviders[0].functions[0]), true);
  assert.equal(Object.isFrozen(result.applicationProviders[0].functions[0].parameters), true);
  assert.equal(resolves, 1);
  assert.equal(sourceCalls, 0);

  source.manifest.requiredImports[2].parameters[0] = "bytes";
  source.request.applicationImports.length = 0;
  assert.deepEqual(result.applicationProviders[0].functions[0].parameters, ["string"]);
});

test("selects only required explicitly bound application modules", () => {
  const value = data();
  value.request.applicationImports.push({
    module: extraApplicationModule,
    artifactPath: "imports/counter.mjs",
    sha256: "b".repeat(64),
  });
  assert.deepEqual(
    createSelector()(value).applicationProviders.map(provider => provider.module),
    [applicationModule]);

  value.request.applicationImports = [];
  assert.deepEqual(createSelector()(value).applicationProviders, []);

  const empty = data();
  empty.manifest.requiredImportModules = [];
  empty.manifest.requiredImports = [];
  empty.request.applicationImports = [];
  assert.deepEqual(createSelector()(empty), {
    platformProviders: [],
    applicationProviders: [],
  });
});

test("selects type-only provider modules without inventing function imports", () => {
  const platform = data();
  platform.manifest.requiredImportModules = [platformModule];
  platform.manifest.requiredImports = [];
  platform.request.applicationImports = [];
  const selectedPlatform = createSelector()(platform);
  assert.deepEqual(
    selectedPlatform.platformProviders.map(provider => provider.module),
    [platformModule]);
  assert.deepEqual(selectedPlatform.applicationProviders, []);

  const application = data();
  application.manifest.requiredImportModules = [applicationModule];
  application.manifest.requiredImports = [];
  const selectedApplication = createSelector()(application);
  assert.deepEqual(selectedApplication.platformProviders, []);
  assert.deepEqual(selectedApplication.applicationProviders, [{
    module: applicationModule,
    functions: [],
  }]);
});

test("excludes runtime-owned reactor imports from external provider selection", () => {
  const value = data();
  const reactorModule = "netwasm:runtime/reactor-host@1.0.0";
  value.manifest.requiredImportModules.push(reactorModule);
  value.manifest.requiredImports.push(
    func(reactorModule, "watch", ["u32", "s64"]),
    func(reactorModule, "cancel", ["u32"]));

  const selected = createSelector()(value);

  assert.deepEqual(selected.platformProviders.map(provider => provider.module), [platformModule]);
  assert.deepEqual(selected.applicationProviders.map(provider => provider.module), [applicationModule]);
});

test("feeds the capability boundary as selected metadata without runtime objects", () => {
  const value = data();
  const selected = createSelector()(value);
  const validateProviderMetadata = createProviderMetadataValidator({ selectProviderKind });
  const validateCapabilityBinding = createCapabilityBindingValidator({
    isInternalImport,
    selectProviderKind,
    validateManifest: manifest => manifest,
    validateProviderMetadata,
    validateRequest: request => request,
  });
  const result = validateCapabilityBinding({
    manifest: value.manifest,
    request: value.request,
    ...selected,
  });
  assert.deepEqual(result.platformProviders[0], selected.platformProviders[0]);
  assert.equal(result.applicationProviders[0].module, applicationModule);
});

test("validates exact selector dependencies", () => {
  const valid = { isInternalImport() {}, resolvePlatformProvider() {}, selectProviderKind() {} };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createProviderMetadataSelector(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createProviderMetadataSelector({ ...valid, [key]: null }),
      /policy, platform provider resolver, and provider-kind selector are required/i);
  }

  assert.throws(
    () => createSelector({ isInternalImport: () => "internal" })(data()),
    /policy returned an unsupported result/i);
});

test("rejects invalid selection requests and nested collection owners", () => {
  const valid = data();
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createSelector()(value.value),
      value.shape ? /selection request shape/i : /selection request is invalid/i);
  }
  for (const [key, label] of [
    ["manifest", /manifest required imports/],
    ["request", /request application imports/],
  ]) {
    for (const owner of [null, 1, [], { [Symbol("hidden")]: true }]) {
      assert.throws(() => createSelector()({ ...valid, [key]: owner }), label);
    }
    for (const owner of [{}, { [key === "manifest" ? "requiredImports" : "applicationImports"]: null }]) {
      assert.throws(() => createSelector()({ ...valid, [key]: owner }), /must be explicit/i);
    }
    const property = key === "manifest" ? "requiredImports" : "applicationImports";
    const nonEnumerable = {};
    Object.defineProperty(nonEnumerable, property, { value: [], enumerable: false });
    const accessor = {};
    Object.defineProperty(accessor, property, { get: () => [], enumerable: true });
    for (const owner of [nonEnumerable, accessor]) {
      assert.throws(() => createSelector()({ ...valid, [key]: owner }), /must be explicit/i);
    }
  }
});

test("rejects malformed required functions and inconsistent kind selection", () => {
  const base = data().manifest.requiredImports[0];
  for (const value of invalidObjects(base)) {
    const input = data();
    input.manifest.requiredImports = [value.value];
    assert.throws(
      () => createSelector()(input),
      value.shape ? /required import function shape/i : /required import function is invalid/i);
  }
  for (const mutation of [
    value => { value.interface = ""; },
    value => { value.name = ""; },
    value => { value.parameters = null; },
    value => { value.results = null; },
  ]) {
    const input = data();
    mutation(input.manifest.requiredImports[0]);
    assert.throws(() => createSelector()(input), /function is incomplete/i);
  }
  assert.throws(
    () => createSelector({ selectProviderKind: () => "future" })(data()),
    /selector returned an unsupported kind/i);

  let calls = 0;
  assert.throws(
    () => createSelector({
      selectProviderKind() {
        calls++;
        return calls === 1 ? "platform" : "application";
      },
    })(data()),
    /inconsistent ownership/i);
});

test("rejects invalid required module inventories", () => {
  for (const modules of [
    [null],
    [1],
    [""],
    [platformModule, platformModule],
  ]) {
    const value = data();
    value.manifest.requiredImportModules = modules;
    assert.throws(() => createSelector()(value), /module inventory is invalid or duplicated/i);
  }

  const missing = data();
  missing.manifest.requiredImportModules = [applicationModule];
  assert.throws(() => createSelector()(missing), /function has no required module/i);
});

test("rejects malformed application bindings", () => {
  const base = data().request.applicationImports[0];
  for (const value of invalidObjects(base)) {
    const input = data();
    input.request.applicationImports = [value.value];
    assert.throws(
      () => createSelector()(input),
      value.shape ? /application import binding shape/i : /application import binding is invalid/i);
  }
  for (const module of [null, 1, ""]) {
    const input = data();
    input.request.applicationImports[0].module = module;
    assert.throws(() => createSelector()(input), /binding module is required/i);
  }
});

test("rejects missing and malformed platform registry products without loading sources", () => {
  const failure = new Error("provider missing");
  assert.throws(
    () => createSelector({ resolvePlatformProvider() { throw failure; } })(data()),
    error => error === failure);

  const validProvider = createRegistry().resolve({ module: platformModule }).provider;
  const validRegistration = createRegistry().resolve({ module: platformModule });
  for (const registration of [
    null,
    { provider: validProvider },
    { createSource() {}, provider: validProvider },
    Object.freeze({ createSource: null, provider: validProvider }),
    Object.freeze({ createSource() {}, provider: null }),
    Object.freeze({ createSource() {}, provider: { ...validProvider } }),
    Object.freeze({
      createSource() {},
      provider: Object.freeze({ ...validProvider, module: "wasi:cli/exit@0.2.11" }),
    }),
    Object.freeze({
      createSource() {},
      provider: Object.freeze({ ...validProvider, functions: [...validProvider.functions] }),
    }),
    Object.freeze({
      ...validRegistration,
      provider: Object.freeze({
        ...validProvider,
        functions: Object.freeze([{ ...validProvider.functions[0] }]),
      }),
    }),
  ]) {
    assert.throws(
      () => createSelector({ resolvePlatformProvider: () => registration })(data()),
      /resolved platform provider/i);
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

import assert from "node:assert/strict";
import test from "node:test";

import { createCapabilityBindingValidator } from "./capability-binding-validator.mjs";
import { isInternalImport } from "./internal-import-policy.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";

const digest = character => character.repeat(64);
const platformModule = "wasi:cli/environment@0.2.11";
const applicationModule = "example:logging/logger@1.0.0";

function func(interface_, name, parameters = [], results = []) {
  return { interface: interface_, name, parameters, results };
}

function data() {
  const platformFunction = func(platformModule, "get-arguments", [], ["list<string>"]);
  const applicationFunction = func(applicationModule, "log", ["string"], []);
  return {
    manifest: {
      buildFingerprint: digest("a"),
      requiredImportModules: [platformModule, applicationModule],
      requiredImports: [platformFunction, applicationFunction],
      artifacts: [
        { relativePath: "app.wasm", role: "application", mediaType: "application/wasm", sha256: digest("b") },
        { relativePath: "imports/logger.mjs", role: "application-import", mediaType: "text/javascript", sha256: digest("c") },
      ],
    },
    request: {
      buildFingerprint: digest("a"),
      grants: {
        environment: [],
        preopens: [],
        network: "allowAll",
        clocks: ["wall", "monotonic"],
        randomness: true,
      },
      applicationImports: [{ module: applicationModule, artifactPath: "imports/logger.mjs", sha256: digest("c") }],
    },
    platformProviders: [{
      module: platformModule,
      capability: "baseline",
      functions: [func(platformModule, "get-arguments", [], ["list<string>"])],
    }],
    applicationProviders: [{
      module: applicationModule,
      functions: [func(applicationModule, "log", ["string"], [])],
    }],
  };
}

function createValidator(overrides = {}) {
  return createCapabilityBindingValidator({
    isInternalImport,
    selectProviderKind,
    validateManifest: value => value,
    validateProviderMetadata: createProviderMetadataValidator({ selectProviderKind }),
    validateRequest: value => value,
    ...overrides,
  });
}

function expectInvalid(value, pattern, validator = createValidator()) {
  assert.throws(() => validator(value), pattern);
}

test("capability binding validator accepts and snapshots every granted capability", () => {
  for (const capability of [
    "baseline",
    "environment",
    "preopenedDirectories",
    "network",
    "wallClock",
    "monotonicClock",
    "randomness",
  ]) {
    const value = data();
    value.platformProviders[0].capability = capability;
    const result = createValidator()(value);
    assert.equal(result.platformProviders[0].capability, capability);
    assert.equal(Object.isFrozen(result), true);
    assert.equal(Object.isFrozen(result.platformProviders), true);
    assert.equal(Object.isFrozen(result.platformProviders[0]), true);
    assert.equal(Object.isFrozen(result.platformProviders[0].functions[0]), true);
    assert.equal(Object.isFrozen(result.applicationProviders[0]), true);
    assert.notStrictEqual(result.platformProviders, value.platformProviders);
  }

  const empty = data();
  empty.manifest.requiredImportModules = [];
  empty.manifest.requiredImports = [];
  empty.request.applicationImports = [];
  empty.platformProviders = [];
  empty.applicationProviders = [];
  const result = createValidator()(empty);
  assert.deepEqual(result.platformProviders, []);
  assert.deepEqual(result.applicationProviders, []);

  const netwasmPlatform = data();
  const netwasmModule = "netwasm:platform/process@1.0.0";
  const netwasmFunction = func(netwasmModule, "exit", ["u32"]);
  netwasmPlatform.manifest.requiredImports[0] = netwasmFunction;
  netwasmPlatform.manifest.requiredImportModules[0] = netwasmModule;
  netwasmPlatform.platformProviders[0] = {
    module: netwasmModule,
    capability: "baseline",
    functions: [netwasmFunction],
  };
  assert.equal(createValidator()(netwasmPlatform).platformProviders[0].module, netwasmModule);
});

test("capability binding validator requires type-only provider modules", () => {
  const value = data();
  value.manifest.requiredImportModules = [platformModule];
  value.manifest.requiredImports = [];
  value.request.applicationImports = [];
  value.platformProviders[0].functions = [];
  value.applicationProviders = [];
  assert.equal(createValidator()(value).platformProviders[0].module, platformModule);

  value.platformProviders = [];
  expectInvalid(value, /every required import module/i);
});

test("capability binding validator leaves runtime-owned reactor imports to the executor", () => {
  const value = data();
  const reactorModule = "netwasm:runtime/reactor-host@1.0.0";
  value.manifest.requiredImportModules.push(reactorModule);
  value.manifest.requiredImports.push(
    func(reactorModule, "watch", ["u32", "s64"]),
    func(reactorModule, "cancel", ["u32"]));

  const result = createValidator()(value);

  assert.deepEqual(result.platformProviders.map(provider => provider.module), [platformModule]);
  assert.deepEqual(result.applicationProviders.map(provider => provider.module), [applicationModule]);
});

test("capability binding validator composes structural validators in order", () => {
  const calls = [];
  const value = data();
  const validate = createValidator({
    validateManifest(manifest) {
      calls.push("manifest");
      return manifest;
    },
    validateRequest(request) {
      calls.push("request");
      return request;
    },
  });
  assert.equal(Object.isFrozen(validate), true);
  validate(value);
  assert.deepEqual(calls, ["manifest", "request"]);

  const failure = new Error("manifest rejected");
  let requests = 0;
  const rejects = createValidator({
    validateManifest() { throw failure; },
    validateRequest(request) { requests++; return request; },
  });
  assert.throws(() => rejects(value), error => {
    assert.strictEqual(error, failure);
    return true;
  });
  assert.equal(requests, 0);
});

test("createCapabilityBindingValidator validates its exact dependencies", () => {
  const valid = {
    isInternalImport() {},
    selectProviderKind() {},
    validateManifest() {},
    validateProviderMetadata() {},
    validateRequest() {},
  };
  const withSymbol = { ...valid, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), valid);
  const wrongKey = { validateManifest() {}, other() {} };
  const nonEnumerable = { ...valid };
  Object.defineProperty(nonEnumerable, "validateRequest", { value() {}, enumerable: false });
  const accessor = { ...valid };
  Object.defineProperty(accessor, "validateRequest", { get: () => () => {}, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => createCapabilityBindingValidator(value), /options is invalid/i);
  }
  for (const value of [inherited, { ...valid, extra() {} }, wrongKey, nonEnumerable, accessor]) {
    assert.throws(() => createCapabilityBindingValidator(value), /options shape/i);
  }
  assert.throws(
    () => createCapabilityBindingValidator({ ...valid, isInternalImport: null }),
    /policy.*required/i);
  assert.throws(
    () => createCapabilityBindingValidator({ ...valid, validateManifest: null }),
    /validation actions are required/i);
  assert.throws(
    () => createCapabilityBindingValidator({ ...valid, validateRequest: null }),
    /validation actions are required/i);
  assert.throws(
    () => createCapabilityBindingValidator({ ...valid, validateProviderMetadata: null }),
    /validation actions are required/i);
  assert.throws(
    () => createCapabilityBindingValidator({ ...valid, selectProviderKind: null }),
    /validation actions are required/i);

  assert.throws(
    () => createValidator({ isInternalImport: () => "internal" })(data()),
    /policy returned an unsupported result/i);
});

test("capability binding validator rejects invalid requests and provider collections", () => {
  const valid = data();
  const withSymbol = { ...valid, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), valid);
  const wrongKey = { ...valid };
  delete wrongKey.manifest;
  wrongKey.deployment = valid.manifest;
  const nonEnumerable = { ...valid };
  Object.defineProperty(nonEnumerable, "manifest", { value: valid.manifest, enumerable: false });
  const accessor = { ...valid };
  Object.defineProperty(accessor, "manifest", { get: () => valid.manifest, enumerable: true });
  for (const value of [null, 1, [], withSymbol]) expectInvalid(value, /binding request is invalid/i);
  for (const value of [inherited, { ...valid, extra: true }, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(value, /binding request shape/i);
  }
  const wrongBuild = data();
  wrongBuild.request.buildFingerprint = digest("f");
  expectInvalid(wrongBuild, /does not target/i);
  for (const key of ["platformProviders", "applicationProviders"]) {
    const value = data();
    value[key] = null;
    expectInvalid(value, /collections must be explicit/i);
  }

  expectInvalid(
    data(),
    /selector returned an unsupported kind/i,
    createValidator({ selectProviderKind: () => "future" }));
});

test("capability binding validator enforces platform provider structure and authority", () => {
  const base = data().platformProviders[0];
  for (const provider of invalidObjects(base)) {
    const value = data();
    value.platformProviders = [provider.value];
    expectInvalid(value, provider.shape ? /platform provider shape/i : /platform provider is invalid/i);
  }
  for (const module of [null, "", "Module@1.0.0", "module@1.0", "bad_module@1.0.0"]) {
    const value = data();
    value.platformProviders[0].module = module;
    expectInvalid(value, /exact versioned identifier/i);
  }
  const nonreserved = data();
  nonreserved.platformProviders = [{
    module: applicationModule,
    capability: "baseline",
    functions: [func(applicationModule, "log", ["string"], [])],
  }];
  expectInvalid(nonreserved, /must be reserved/i);
  for (const capability of [null, "", "future"]) {
    const value = data();
    value.platformProviders[0].capability = capability;
    expectInvalid(value, /capability is unsupported/i);
  }
  for (const [capability, grants] of [
    ["network", { network: "denyAll" }],
    ["wallClock", { clocks: ["monotonic"] }],
    ["monotonicClock", { clocks: ["wall"] }],
    ["randomness", { randomness: false }],
  ]) {
    const value = data();
    value.platformProviders[0].capability = capability;
    value.request.grants = { ...value.request.grants, ...grants };
    expectInvalid(value, /was not granted/i);
  }
});

test("capability binding validator enforces application provider ownership", () => {
  const base = data().applicationProviders[0];
  for (const provider of invalidObjects(base)) {
    const value = data();
    value.applicationProviders = [provider.value];
    expectInvalid(value, provider.shape ? /application provider shape/i : /application provider is invalid/i);
  }
  const wasi = data();
  wasi.applicationProviders = [{ module: platformModule, functions: [func(platformModule, "get-arguments")] }];
  expectInvalid(wasi, /must not be reserved/i);
  const netwasm = data();
  netwasm.applicationProviders = [{
    module: "netwasm:platform/example@1.0.0",
    functions: [func("netwasm:platform/example@1.0.0", "call")],
  }];
  expectInvalid(netwasm, /must not be reserved/i);

  const unused = data();
  unused.platformProviders[0] = {
    module: "wasi:cli/exit@0.2.11",
    capability: "baseline",
    functions: [func("wasi:cli/exit@0.2.11", "exit")],
  };
  expectInvalid(unused, /not required/i);
  const unusedApplication = data();
  unusedApplication.applicationProviders[0] = {
    module: "example:unused/provider@1.0.0",
    functions: [func("example:unused/provider@1.0.0", "call")],
  };
  expectInvalid(unusedApplication, /not required/i);

  const duplicatePlatform = data();
  duplicatePlatform.platformProviders.push({ ...duplicatePlatform.platformProviders[0] });
  expectInvalid(duplicatePlatform, /provider module.*duplicated/i);
  const crossKind = data();
  crossKind.applicationProviders = [{ module: platformModule, functions: [func(platformModule, "get-arguments")] }];
  expectInvalid(crossKind, /must not be reserved/i);
  const duplicateApplication = data();
  duplicateApplication.applicationProviders.push({ ...duplicateApplication.applicationProviders[0] });
  expectInvalid(duplicateApplication, /provider module.*duplicated/i);

  expectInvalid(
    data(),
    /platform provider module must be reserved/i,
    createValidator({
      validateProviderMetadata: ({ provider }) => ({
        ...provider,
        module: applicationModule,
      }),
    }));
  expectInvalid(
    data(),
    /application provider module must not be reserved/i,
    createValidator({
      validateProviderMetadata: ({ kind, provider }) => kind === "platform"
        ? provider
        : { ...provider, module: platformModule },
    }));
});

test("capability binding validator validates provider function inventories", () => {
  const base = data().platformProviders[0];
  const missing = data();
  missing.platformProviders[0] = { ...base, functions: null };
  expectInvalid(missing, /functions must be explicit/i);
  const empty = data();
  empty.platformProviders[0] = { ...base, functions: [] };
  expectInvalid(empty, /required import.*missing/i);
  const baseFunction = base.functions[0];
  for (const entry of invalidObjects(baseFunction)) {
    const value = data();
    value.platformProviders[0].functions = [entry.value];
    expectInvalid(value, entry.shape ? /provider function shape/i : /provider function is invalid/i);
  }
  const wrongInterface = data();
  wrongInterface.platformProviders[0].functions[0].interface = "wasi:cli/exit@0.2.11";
  expectInvalid(wrongInterface, /does not belong/i);
  for (const name of [
    null, "", "Get", "bad_name", "[method]descriptor", "[method].read",
    "[method]descriptor.read.more", "[constructor]descriptor.open",
  ]) {
    const value = data();
    value.platformProviders[0].functions[0].name = name;
    expectInvalid(value, /canonical WIT identity/i);
  }
  const resourceIntrinsic = data();
  resourceIntrinsic.manifest.requiredImports[0].name = "[resource-drop]descriptor";
  resourceIntrinsic.platformProviders[0].functions[0].name = "[resource-drop]descriptor";
  assert.equal(
    createValidator()(resourceIntrinsic).platformProviders[0].functions[0].name,
    "[resource-drop]descriptor");
  const duplicate = data();
  duplicate.platformProviders[0].functions.push({ ...duplicate.platformProviders[0].functions[0] });
  expectInvalid(duplicate, /provider function.*duplicated/i);
  for (const key of ["parameters", "results"]) {
    const missing = data();
    missing.platformProviders[0].functions[0][key] = null;
    expectInvalid(missing, /signature must be explicit/i);
    for (const item of [null, "", " padded", "bad\0value", "bad\u007fvalue"]) {
      const value = data();
      value.platformProviders[0].functions[0][key] = [item];
      expectInvalid(value, /signature value/i);
    }
  }
});

test("capability binding validator enforces application artifact bindings", () => {
  const unrequired = data();
  unrequired.request.applicationImports[0].module = "example:unused/provider@1.0.0";
  expectInvalid(unrequired, /not required/i);
  const noProvider = data();
  noProvider.applicationProviders = [];
  expectInvalid(noProvider, /no selected provider/i);
  const missingArtifact = data();
  missingArtifact.request.applicationImports[0].artifactPath = "imports/missing.mjs";
  expectInvalid(missingArtifact, /artifact.*missing/i);
  for (const mutation of [
    artifact => { artifact.role = "asset"; },
    artifact => { artifact.mediaType = "application/javascript"; },
    artifact => { artifact.sha256 = digest("f"); },
  ]) {
    const value = data();
    mutation(value.manifest.artifacts[1]);
    expectInvalid(value, /does not match its artifact/i);
  }
  const noBinding = data();
  noBinding.request.applicationImports = [];
  expectInvalid(noBinding, /has no request binding/i);
});

test("capability binding validator enforces exact required members and signatures", () => {
  const missingPlatform = data();
  missingPlatform.platformProviders = [];
  expectInvalid(missingPlatform, /required import module.*no selected provider/i);
  const missingApplication = data();
  missingApplication.applicationProviders = [];
  missingApplication.request.applicationImports = [];
  expectInvalid(missingApplication, /required import module.*no selected provider/i);
  for (const key of ["platformProviders", "applicationProviders"]) {
    const value = data();
    value[key][0].functions[0].name = "other";
    expectInvalid(value, /required import.*missing/i);
  }
  const parameterLength = data();
  parameterLength.applicationProviders[0].functions[0].parameters = [];
  expectInvalid(parameterLength, /incompatible signature/i);
  const parameterValue = data();
  parameterValue.applicationProviders[0].functions[0].parameters = ["bytes"];
  expectInvalid(parameterValue, /incompatible signature/i);
  const resultLength = data();
  resultLength.applicationProviders[0].functions[0].results = ["u32"];
  expectInvalid(resultLength, /incompatible signature/i);
  const resultValue = data();
  resultValue.platformProviders[0].functions[0].results = ["list<u8>"];
  expectInvalid(resultValue, /incompatible signature/i);
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

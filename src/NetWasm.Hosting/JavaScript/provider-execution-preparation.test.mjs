import assert from "node:assert/strict";
import test from "node:test";

import { createProviderExecutionPreparation } from "./provider-execution-preparation.mjs";

const manifest = Object.freeze({ name: "manifest" });
const executionRequest = Object.freeze({ name: "request" });
const filesystem = Object.freeze({ name: "filesystem" });
const platformProvider = Object.freeze({ module: "wasi:cli/environment@0.2.11" });
const applicationProvider = Object.freeze({ module: "example:app/imports@1.0.0" });
const metadata = Object.freeze({
  applicationProviders: Object.freeze([applicationProvider]),
  platformProviders: Object.freeze([platformProvider]),
});
const binding = Object.freeze({ ...metadata, manifest, request: executionRequest });
const platformSource = Object.freeze({ module: platformProvider.module, value: Object.freeze({}) });
const applicationSource = Object.freeze({ module: applicationProvider.module, value: Object.freeze({}) });
const platformSources = Object.freeze([platformSource]);
const applicationSources = Object.freeze([applicationSource]);
const componentImports = Object.freeze(Object.create(null));
const consumerModules = Object.freeze(Object.create(null));
const rawProviders = Object.freeze(Object.create(null));
const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });

function fixture(overrides = {}) {
  const calls = [];
  const options = {
    validateManifest(value) {
      calls.push(["validate-manifest", value]);
      return manifest;
    },
    validateRequest(value) {
      calls.push(["validate-request", value]);
      return executionRequest;
    },
    selectProviderMetadata(value) {
      calls.push(["select", value]);
      return metadata;
    },
    validateCapabilityBinding(value) {
      calls.push(["validate", value]);
      return binding;
    },
    async loadApplicationProviders(value) {
      calls.push(["application", value]);
      return applicationSources;
    },
    async loadPlatformProviders(value) {
      calls.push(["platform", value]);
      return platformSources;
    },
    projectProviderModules(value) {
      calls.push(["project", value]);
      return Object.freeze({ componentImports, consumerModules, rawProviders });
    },
    ...overrides,
  };
  return { calls, options, prepare: createProviderExecutionPreparation(options) };
}

function request(overrides = {}) {
  return { filesystem, manifest, request: executionRequest, signal: null, stderr, stdout, ...overrides };
}

test("selects, validates, loads, and projects only the execution providers", async () => {
  const f = fixture();
  assert.equal(Object.isFrozen(f.prepare), true);
  const result = await f.prepare(request());

  assert.equal(Object.isFrozen(result), true);
  assert.equal(result.binding, binding);
  assert.equal(result.componentImports, componentImports);
  assert.equal(result.consumerModules, consumerModules);
  assert.equal(result.rawProviders, rawProviders);
  assert.deepEqual(f.calls, [
    ["validate-manifest", manifest],
    ["validate-request", executionRequest],
    ["select", { manifest, request: executionRequest }],
    ["validate", {
      applicationProviders: metadata.applicationProviders,
      manifest,
      platformProviders: metadata.platformProviders,
      request: executionRequest,
    }],
    ["application", { binding, signal: null }],
    ["platform", { binding, filesystem, signal: null, stderr, stdout }],
    ["project", {
      providers: [platformProvider, applicationProvider],
      sources: [platformSource, applicationSource],
    }],
  ]);
});

test("stops at each failed dependency without invoking later acts", async () => {
  const stages = [
    "validateManifest",
    "validateRequest",
    "selectProviderMetadata",
    "validateCapabilityBinding",
    "loadApplicationProviders",
    "loadPlatformProviders",
    "projectProviderModules",
  ];
  for (const stage of stages) {
    const error = new Error(stage);
    const f = fixture({ [stage]() { throw error; } });
    await assert.rejects(() => f.prepare(request()), value => value === error);
    assert.equal(f.calls.length, stages.indexOf(stage));
  }
});

test("observes cancellation before and between every side-effecting phase", async () => {
  for (const stage of ["before", "after-validation", "after-application", "after-platform"]) {
    const controller = new AbortController();
    if (stage === "before") controller.abort();
    const overrides = {};
    if (stage === "after-validation") {
      overrides.validateCapabilityBinding = () => { controller.abort(); return binding; };
    } else if (stage === "after-application") {
      overrides.loadApplicationProviders = async () => { controller.abort(); return applicationSources; };
    } else if (stage === "after-platform") {
      overrides.loadPlatformProviders = async () => { controller.abort(); return platformSources; };
    }
    const f = fixture(overrides);
    await assert.rejects(
      () => f.prepare(request({ signal: controller.signal })),
      error => error.name === "AbortError" && /cancelled/.test(error.message));
    assert.equal(f.calls.some(([name]) => name === "project"), false);
  }
});

test("rejects malformed factories and requests before selection", async () => {
  const base = fixture().options;
  for (const value of [null, 1, [], {}, { ...base, extra: () => {} }, Object.create(base)]) {
    assert.throws(() => createProviderExecutionPreparation(value), TypeError);
  }
  for (const name of Object.keys(base)) {
    assert.throws(() => createProviderExecutionPreparation({ ...base, [name]: null }), /action/);
  }
  const symbolicFactory = { ...base, [Symbol("invalid")]: true };
  assert.throws(() => createProviderExecutionPreparation(symbolicFactory), TypeError);
  const factoryAccessor = { ...base };
  Object.defineProperty(factoryAccessor, "selectProviderMetadata", {
    enumerable: true,
    get: () => base.selectProviderMetadata,
  });
  assert.throws(() => createProviderExecutionPreparation(factoryAccessor), TypeError);

  const f = fixture();
  for (const value of [null, 1, [], {}, { ...request(), extra: true }, Object.create(request())]) {
    await assert.rejects(() => f.prepare(value), TypeError);
  }
  const symbolicRequest = { ...request(), [Symbol("invalid")]: true };
  await assert.rejects(() => f.prepare(symbolicRequest), TypeError);
  const requestAccessor = { ...request() };
  Object.defineProperty(requestAccessor, "manifest", { enumerable: true, get: () => manifest });
  await assert.rejects(() => f.prepare(requestAccessor), TypeError);
  for (const value of [[], 1]) {
    await assert.rejects(() => f.prepare(request({ filesystem: value })), /filesystem/);
  }
  for (const signal of [1, {}, { aborted: false, addEventListener() {} }]) {
    await assert.rejects(() => f.prepare(request({ signal })), /AbortSignal/);
  }
  for (const sink of [null, {}, { write() {} }, Object.freeze({ write: null }),
    Object.freeze({ write() {}, extra: true })]) {
    await assert.rejects(() => f.prepare(request({ stdout: sink })), /stdout.*output sink/);
    await assert.rejects(() => f.prepare(request({ stderr: sink })), /stderr.*output sink/);
  }
  assert.deepEqual(f.calls, []);
});

test("rejects malformed dependency products at their owning boundaries", async () => {
  const cases = [
    ["validateManifest", null, /deployment manifest/],
    ["validateManifest", {}, /deployment manifest/],
    ["validateRequest", null, /execution request/],
    ["validateRequest", {}, /execution request/],
    ["selectProviderMetadata", null, /metadata/],
    ["selectProviderMetadata", { ...metadata, extra: true }, /metadata/],
    ["validateCapabilityBinding", null, /binding/],
    ["validateCapabilityBinding", { ...binding, extra: true }, /binding/],
    ["loadApplicationProviders", [], /immutable array/],
    ["loadApplicationProviders", Object.freeze({}), /immutable array/],
    ["loadPlatformProviders", [], /immutable array/],
    ["loadPlatformProviders", Object.freeze({}), /immutable array/],
    ["projectProviderModules", null, /modules/],
    ["projectProviderModules", { ...Object.freeze({ componentImports, consumerModules, rawProviders }), extra: true }, /modules/],
    ["projectProviderModules", Object.freeze({ componentImports: null, consumerModules, rawProviders }), /component imports/],
    ["projectProviderModules", Object.freeze({ componentImports: [], consumerModules, rawProviders }), /component imports/],
    ["projectProviderModules", Object.freeze({ componentImports: {}, consumerModules, rawProviders }), /component imports/],
    ["projectProviderModules", Object.freeze({ componentImports, consumerModules: null, rawProviders }), /consumer modules/],
    ["projectProviderModules", Object.freeze({ componentImports, consumerModules: [], rawProviders }), /consumer modules/],
    ["projectProviderModules", Object.freeze({ componentImports, consumerModules: {}, rawProviders }), /consumer modules/],
    ["projectProviderModules", Object.freeze({ componentImports, consumerModules, rawProviders: null }), /raw providers/],
  ];
  for (const [name, product, pattern] of cases) {
    const f = fixture({ [name]: asyncOrValue(name, product) });
    await assert.rejects(() => f.prepare(request()), pattern);
  }
});

function asyncOrValue(name, value) {
  return name.startsWith("load") ? async () => value : () => value;
}

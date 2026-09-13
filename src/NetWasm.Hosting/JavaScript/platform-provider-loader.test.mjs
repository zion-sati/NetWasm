import assert from "node:assert/strict";
import test from "node:test";

import { createPlatformProviderLoader } from "./platform-provider-loader.mjs";
import { createPlatformProviderRegistry } from "./platform-provider-registry.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";

const modules = ["wasi:cli/environment@0.2.11", "wasi:cli/exit@0.2.11"];
const stdout = Object.freeze({ write() {} });
const stderr = Object.freeze({ write() {} });

function func(module, name, parameters = [], results = []) {
  return { interface: module, name, parameters, results };
}

function providers() {
  return [
    {
      module: modules[0],
      capability: "environment",
      functions: [func(modules[0], "get-environment", [], ["list<tuple<string,string>>"])],
    },
    {
      module: modules[1],
      capability: "baseline",
      functions: [func(modules[1], "exit", ["result<_,_>"])],
    },
  ];
}

function createRegistry(factories = [() => ({}), () => ({})]) {
  const validateProviderMetadata = createProviderMetadataValidator({ selectProviderKind });
  return createPlatformProviderRegistry({
    registrations: providers().map((provider, index) => ({
      provider,
      createSource: factories[index],
    })),
    validateProviderMetadata,
  });
}

function executionRequest() {
  return Object.freeze({
    schemaVersion: 1,
    buildFingerprint: "a".repeat(64),
    deploymentManifestSha256: "b".repeat(64),
    arguments: Object.freeze(["first"]),
    environment: Object.freeze([Object.freeze({ name: "ALPHA", value: "one" })]),
    grants: Object.freeze({
      environment: Object.freeze(["ALPHA"]),
      preopens: Object.freeze([]),
      network: "denyAll",
      clocks: Object.freeze([]),
      randomness: false,
    }),
    applicationImports: Object.freeze([]),
  });
}

function binding(registry, selectedModules = modules) {
  return Object.freeze({
    manifest: Object.freeze({}),
    request: executionRequest(),
    platformProviders: Object.freeze(selectedModules.map(module => registry.resolve({ module }).provider)),
    applicationProviders: Object.freeze([]),
  });
}

function loadRequest(registry, overrides = {}) {
  return {
    binding: binding(registry),
    filesystem: null,
    signal: null,
    stderr,
    stdout,
    ...overrides,
  };
}

function createLoader(registry, overrides = {}) {
  return createPlatformProviderLoader({
    resolvePlatformProvider: request => registry.resolve(request),
    ...overrides,
  });
}

test("loads selected providers in order from one immutable explicit request", async () => {
  const calls = [];
  const values = [Object.freeze({ environment() {} }), Object.freeze({ exit() {} })];
  const filesystem = Object.freeze({ types: Object.freeze({}) });
  const signal = new AbortController().signal;
  let expectedRequest;
  const registry = createRegistry(values.map((value, index) => request => {
    calls.push(`source:${index}`);
    assert.equal(Object.isFrozen(request), true);
    if (expectedRequest === undefined) expectedRequest = request;
    else assert.strictEqual(request, expectedRequest);
    assert.deepEqual(request.arguments, ["first"]);
    assert.deepEqual(request.environment, [{ name: "ALPHA", value: "one" }]);
    assert.equal(request.grants.network, "denyAll");
    assert.strictEqual(request.filesystem, filesystem);
    assert.strictEqual(request.signal, signal);
    assert.strictEqual(request.stderr, stderr);
    assert.strictEqual(request.stdout, stdout);
    return value;
  }));
  let resolves = 0;
  const load = createLoader(registry, {
    resolvePlatformProvider(request) {
      calls.push(`resolve:${request.module}`);
      resolves++;
      return registry.resolve(request);
    },
  });
  assert.equal(Object.isFrozen(load), true);
  const sources = await load({ binding: binding(registry), filesystem, signal, stderr, stdout });
  assert.deepEqual(calls, [
    `resolve:${modules[0]}`,
    "source:0",
    `resolve:${modules[1]}`,
    "source:1",
  ]);
  assert.equal(resolves, 2);
  assert.equal(Object.isFrozen(sources), true);
  assert.equal(Object.isFrozen(sources[0]), true);
  assert.deepEqual(sources.map(source => source.module), modules);
  assert.strictEqual(sources[0].value, values[0]);
  assert.strictEqual(sources[1].value, values[1]);
});

test("does not resolve or invoke unselected platform providers", async () => {
  const calls = [];
  const registry = createRegistry([
    () => { calls.push("selected"); return {}; },
    () => { calls.push("unselected"); return {}; },
  ]);
  const selected = await createLoader(registry)({
    binding: binding(registry, [modules[0]]),
    filesystem: null,
    signal: null,
    stderr,
    stdout,
  });
  assert.deepEqual(selected.map(source => source.module), [modules[0]]);
  assert.deepEqual(calls, ["selected"]);

  let resolves = 0;
  const empty = await createLoader(registry, {
    resolvePlatformProvider() { resolves++; },
  })({
    binding: binding(registry, []),
    filesystem: null,
    signal: null,
    stderr,
    stdout,
  });
  assert.deepEqual(empty, []);
  assert.equal(resolves, 0);
});

test("validates its exact resolver dependency", () => {
  const valid = { resolvePlatformProvider() {} };
  for (const value of invalidObjects(valid)) {
    assert.throws(
      () => createPlatformProviderLoader(value.value),
      value.shape ? /options shape/i : /options is invalid/i);
  }
  assert.throws(
    () => createPlatformProviderLoader({ resolvePlatformProvider: null }),
    /resolver is required/i);
});

test("validates exact load requests, signals and optional filesystems before resolution", async () => {
  const registry = createRegistry();
  const valid = loadRequest(registry);
  for (const value of invalidObjects(valid)) {
    await assert.rejects(
      createLoader(registry)(value.value),
      value.shape ? /load request shape/i : /load request is invalid/i);
  }
  for (const signal of [1, {}, { aborted: false }, {
    aborted: false,
    addEventListener() {},
  }]) {
    await assert.rejects(createLoader(registry)({ ...valid, signal }), /must be an AbortSignal/i);
  }
  for (const filesystem of [1, [], () => {}]) {
    await assert.rejects(
      createLoader(registry)({ ...valid, filesystem }),
      /filesystem must be an object or null/i);
  }
  for (const sink of [null, {}, { write() {} }, Object.freeze({ write: null }),
    Object.freeze({ write() {}, extra: true })]) {
    await assert.rejects(createLoader(registry)({ ...valid, stdout: sink }), /stdout.*output sink/i);
    await assert.rejects(createLoader(registry)({ ...valid, stderr: sink }), /stderr.*output sink/i);
  }
});

test("rejects malformed or mutable capability-binding products before resolution", async () => {
  const registry = createRegistry();
  const base = binding(registry);
  for (const value of invalidObjects(base)) {
    await assert.rejects(
      createLoader(registry)({ binding: value.value, filesystem: null, signal: null, stderr, stdout }),
      value.shape ? /capability binding shape/i : /capability binding is invalid/i);
  }
  for (const product of [
    { ...base },
    Object.freeze({ ...base, platformProviders: null }),
    Object.freeze({ ...base, platformProviders: [...base.platformProviders] }),
  ]) {
    await assert.rejects(
      createLoader(registry)({ binding: product, filesystem: null, signal: null, stderr, stdout }),
      /capability binding is incomplete/i);
  }
});

test("rejects malformed or mutable execution-request products before resolution", async () => {
  const registry = createRegistry();
  const base = binding(registry);
  const run = request => createLoader(registry)({
    binding: Object.freeze({ ...base, request }),
    filesystem: null,
    signal: null,
    stderr,
    stdout,
  });
  for (const value of invalidObjects(base.request)) {
    await assert.rejects(
      run(value.value),
      value.shape ? /execution request shape/i : /execution request is invalid/i);
  }
  for (const request of [
    { ...base.request },
    Object.freeze({ ...base.request, arguments: null }),
    Object.freeze({ ...base.request, arguments: [...base.request.arguments] }),
    Object.freeze({ ...base.request, environment: null }),
    Object.freeze({ ...base.request, environment: [...base.request.environment] }),
    Object.freeze({ ...base.request, grants: null }),
    Object.freeze({ ...base.request, grants: { ...base.request.grants } }),
  ]) {
    await assert.rejects(run(request), /execution request is incomplete/i);
  }
});

test("rejects duplicate, malformed and mutable selected metadata before resolution", async () => {
  const registry = createRegistry();
  const base = binding(registry);
  const run = platformProviders => createLoader(registry)({
    binding: Object.freeze({ ...base, platformProviders: Object.freeze(platformProviders) }),
    filesystem: null,
    signal: null,
    stderr,
    stdout,
  });
  await assert.rejects(
    run([base.platformProviders[0], base.platformProviders[0]]),
    /module.*duplicated/i);

  const valid = base.platformProviders[0];
  for (const value of invalidObjects(valid)) {
    await assert.rejects(
      run([value.value]),
      value.shape ? /metadata shape/i : /metadata is invalid/i);
  }
  for (const provider of [
    { ...valid },
    Object.freeze({ ...valid, module: "" }),
    Object.freeze({ ...valid, capability: "" }),
    Object.freeze({ ...valid, functions: null }),
    Object.freeze({ ...valid, functions: [...valid.functions] }),
  ]) {
    await assert.rejects(run([provider]), /metadata is incomplete/i);
  }

  const validFunction = valid.functions[0];
  for (const value of invalidObjects(validFunction)) {
    const provider = Object.freeze({
      ...valid,
      functions: Object.freeze([value.value]),
    });
    await assert.rejects(
      run([provider]),
      value.shape ? /metadata function shape/i : /metadata function is invalid/i);
  }
  for (const function_ of [
    { ...validFunction },
    Object.freeze({ ...validFunction, interface: modules[1] }),
    Object.freeze({ ...validFunction, name: "" }),
    Object.freeze({ ...validFunction, parameters: null }),
    Object.freeze({ ...validFunction, parameters: [...validFunction.parameters] }),
    Object.freeze({ ...validFunction, results: null }),
    Object.freeze({ ...validFunction, results: [...validFunction.results] }),
  ]) {
    const provider = Object.freeze({
      ...valid,
      functions: Object.freeze([function_]),
    });
    await assert.rejects(run([provider]), /metadata function is incomplete/i);
  }
});

test("rejects malformed registry products and metadata drift before source creation", async () => {
  const registry = createRegistry();
  const request = loadRequest(registry, {
    binding: binding(registry, [modules[0]]),
  });
  for (const registration of invalidObjects(registry.resolve({ module: modules[0] }))) {
    await assert.rejects(
      createLoader(registry, { resolvePlatformProvider: () => registration.value })(request),
      registration.shape ? /registration shape/i : /registration is invalid/i);
  }
  const valid = registry.resolve({ module: modules[0] });
  for (const registration of [
    { ...valid },
    Object.freeze({ ...valid, createSource: null }),
  ]) {
    await assert.rejects(
      createLoader(registry, { resolvePlatformProvider: () => registration })(request),
      /registration is incomplete/i);
  }

  const selected = request.binding.platformProviders[0];
  const changes = [
    {
      ...selected,
      module: modules[1],
      functions: selected.functions.map(value => ({ ...value, interface: modules[1] })),
    },
    { ...selected, capability: "baseline" },
    { ...selected, functions: [] },
    { ...selected, functions: [{ ...selected.functions[0], name: "other" }] },
    { ...selected, functions: [{ ...selected.functions[0], parameters: ["string"] }] },
    { ...selected, functions: [{ ...selected.functions[0], results: ["string"] }] },
  ];
  for (const changed of changes) {
    const provider = freezeProvider(changed);
    const registration = Object.freeze({ ...valid, provider });
    await assert.rejects(
      createLoader(registry, { resolvePlatformProvider: () => registration })(request),
      /metadata changed after validation/i);
  }

  const invalidFunctionOwner = Object.freeze({
    ...valid,
    provider: freezeProvider({
      ...selected,
      functions: [{ ...selected.functions[0], interface: modules[1] }],
    }),
  });
  await assert.rejects(
    createLoader(registry, { resolvePlatformProvider: () => invalidFunctionOwner })(request),
    /metadata function is incomplete/i);
});

test("preserves factory failures and rejects invalid runtime sources", async () => {
  const failure = new Error("factory failed");
  const failing = createRegistry([async () => { throw failure; }, () => ({})]);
  await assert.rejects(
    createLoader(failing)({
      binding: binding(failing, [modules[0]]),
      filesystem: null,
      signal: null,
      stderr,
      stdout,
    }),
    error => error === failure);

  for (const source of [null, 1, [], () => {}]) {
    const registry = createRegistry([async () => source, () => ({})]);
    await assert.rejects(
      createLoader(registry)({
        binding: binding(registry, [modules[0]]),
        filesystem: null,
        signal: null,
        stderr,
        stdout,
      }),
      /returned an invalid source/i);
  }
});

test("observes cancellation before resolution, before factories and between factories", async () => {
  const before = new AbortController();
  before.abort();
  const registry = createRegistry();
  let resolves = 0;
  await assert.rejects(
    createLoader(registry, { resolvePlatformProvider() { resolves++; } })({
      binding: binding(registry),
      filesystem: null,
      signal: before.signal,
      stderr,
      stdout,
    }),
    /loading was cancelled/i);
  assert.equal(resolves, 0);

  const afterResolve = new AbortController();
  let sourceCalls = 0;
  await assert.rejects(
    createLoader(registry, {
      resolvePlatformProvider(request) {
        afterResolve.abort();
        return registry.resolve(request);
      },
    })({
      binding: binding(registry, [modules[0]]),
      filesystem: null,
      signal: afterResolve.signal,
      stderr,
      stdout,
    }),
    /loading was cancelled/i);
  assert.equal(sourceCalls, 0);

  const afterSource = new AbortController();
  const cancelling = createRegistry([
    () => { sourceCalls++; afterSource.abort(); return {}; },
    () => { sourceCalls++; return {}; },
  ]);
  await assert.rejects(
    createLoader(cancelling)({
      binding: binding(cancelling),
      filesystem: null,
      signal: afterSource.signal,
      stderr,
      stdout,
    }),
    /loading was cancelled/i);
  assert.equal(sourceCalls, 1);
});

function freezeProvider(provider) {
  return Object.freeze({
    ...provider,
    functions: Object.freeze(provider.functions.map(function_ => Object.freeze({
      ...function_,
      parameters: Object.freeze([...function_.parameters]),
      results: Object.freeze([...function_.results]),
    }))),
  });
}

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

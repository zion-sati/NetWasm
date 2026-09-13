import assert from "node:assert/strict";
import test from "node:test";
import { createRawExecutionStrategy } from "./raw-execution-strategy.mjs";
import { commandExecutionContract } from "./execution-contracts.mjs";

const application = Buffer.from(
  "AGFzbQEAAAABDQJgAAF/YAR/f39/AX8CGwEPbmV0d2FzbS5ob3N0LnYxB3NlcnZpY2UAAAMDAgEABQMBAAEHMQQGbWVtb3J5AgANY20zMnAyX21lbW9yeQIADmNtMzJwMl9yZWFsbG9jAAEDcnVuAAIKCwIEAEEICwQAEAALAEkEbmFtZQEYAwAHc2VydmljZQEHcmVhbGxvYwIDcnVuBB0CAAxzZXJ2aWNlLXR5cGUBDHJlYWxsb2MtdHlwZQYJAQAGbWVtb3J5",
  "base64");
const fingerprint = `sha256:${"1".repeat(64)}`;
const manifest = Object.freeze({ version: 1, target: "wasm32" });
const release = (code, action) => ({
  code,
  message: `The execution host could not release ${code}.`,
  release: action,
});

function emptyAdapter() {
  const metadata = Object.freeze({
    abiVersion: 1,
    bindingIdentities: Object.freeze([]),
    requiredCapabilities: Object.freeze([]),
    target: "wasm32",
    witSourceFingerprint: fingerprint,
  });
  return Object.freeze({
    rawAdapterMetadata: metadata,
    createAdapter: () => Object.freeze({
      imports: Object.freeze(Object.create(null)),
      metadata,
    }),
  });
}

async function loadedRawModule() {
  const module = await WebAssembly.compile(application);
  return Object.freeze({
    abi: Object.freeze({
      target: "wasm32",
      entryPoint: Object.freeze({
        parameterShape: "none",
        returnShape: "exitCode",
        completionShape: "synchronous",
      }),
      imports: Object.freeze(WebAssembly.Module.imports(module)),
      exports: Object.freeze(WebAssembly.Module.exports(module)),
    }),
    adapter: emptyAdapter(),
    interopManifest: manifest,
    module,
  });
}

function request(prepareInterop, overrides = {}) {
  return {
    artifacts: ["raw-artifacts"],
    contractKey: commandExecutionContract,
    prepareInterop,
    providers: {},
    ...overrides,
  };
}

test("loads, prepares, binds, executes, and closes one real raw instance", async () => {
  const calls = [];
  const loaded = await loadedRawModule();
  let boundInstance;
  const execute = createRawExecutionStrategy(async value => {
    assert.equal(Object.isFrozen(value), true);
    assert.deepEqual(value.artifacts, ["raw-artifacts"]);
    assert.equal(value.signal, null);
    calls.push("load");
    return loaded;
  });
  assert.equal(Object.isFrozen(execute), true);

  const result = await execute(request(value => {
    assert.equal(Object.isFrozen(value), true);
    assert.deepEqual(Object.keys(value).sort(), ["abi", "adapter", "manifest"]);
    assert.equal(value.abi, loaded.abi);
    assert.equal(value.adapter, loaded.adapter);
    assert.equal(value.manifest, manifest);
    calls.push("prepare");
    return {
      imports: {
        "netwasm.host.v1": {
          service() {
            assert.equal(boundInstance instanceof WebAssembly.Instance, true);
            calls.push("service");
            return 37;
          },
        },
      },
      bindInstance(instance) {
        assert.equal(instance instanceof WebAssembly.Instance, true);
        boundInstance = instance;
        calls.push("bind");
      },
      close() {
        assert.equal(boundInstance instanceof WebAssembly.Instance, true);
        calls.push("interop-close");
      },
    };
  }, {
    releaseActions: [release("caller", () => calls.push("caller-close"))],
  }));

  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 37);
  assert.deepEqual(calls, [
    "load",
    "prepare",
    "bind",
    "service",
    "interop-close",
    "caller-close",
  ]);
});

test("normalizes a custom instantiator result and binds that exact instance", async () => {
  const loaded = await loadedRawModule();
  let instantiated;
  let bound;
  const execute = createRawExecutionStrategy(async () => loaded);
  const result = await execute(request(() => ({
    imports: { "netwasm.host.v1": { service: () => 11 } },
    bindInstance(instance) { bound = instance; },
    close() {},
  }), {
    async instantiate(value) {
      assert.equal(Object.isFrozen(value), true);
      assert.equal(value.module, loaded.module);
      instantiated = await WebAssembly.instantiate(value.module, value.imports);
      return { instance: instantiated };
    },
  }));

  assert.equal(result.exitCode, 11);
  assert.equal(bound, instantiated);
});

test("maps loader failures and cancellation without preparing or entering the guest", async () => {
  const calls = [];
  const failed = createRawExecutionStrategy(() => {
    calls.push("load");
    throw new Error("private loader detail");
  });
  const failedResult = await failed(request(() => {
    calls.push("prepare");
  }, {
    releaseActions: [release("caller", () => calls.push("caller-close"))],
  }));
  assert.equal(failedResult.primaryFailure.code, "host.raw-load");
  assert.doesNotMatch(JSON.stringify(failedResult), /private/);
  assert.deepEqual(calls, ["load", "caller-close"]);

  const before = new AbortController();
  before.abort();
  let loads = 0;
  const beforeResult = await createRawExecutionStrategy(async () => { loads++; })(request(
    () => { throw new Error("must not prepare"); },
    { signal: before.signal }));
  assert.equal(beforeResult.primaryFailure.code, "caller.cancelled");
  assert.equal(loads, 0);

  for (const mode of ["throw", "return"]) {
    const during = new AbortController();
    let preparations = 0;
    const execute = createRawExecutionStrategy(async () => {
      loads++;
      during.abort();
      if (mode === "throw") throw new Error("private cancellation detail");
      return loadedRawModule();
    });
    const result = await execute(request(() => {
      preparations++;
      throw new Error("must not prepare");
    }, { signal: during.signal }));
    assert.equal(result.primaryFailure.code, "caller.cancelled");
    assert.equal(preparations, 0);
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
});

test("maps interop preparation failures before instantiation", async () => {
  const loaded = await loadedRawModule();
  const calls = [];
  const execute = createRawExecutionStrategy(async () => loaded);
  const invalidPreparations = [
    () => { throw new Error("private interop detail"); },
    () => null,
    () => [],
    () => ({}),
    () => ({ imports: {}, bindInstance() {}, close() {}, extra: true }),
    () => ({ imports: {}, bindInstance: null, close() {} }),
    () => ({ imports: {}, bindInstance() {}, close: null }),
    () => ({ imports: null, bindInstance() {}, close() {} }),
    () => ({ imports: [], bindInstance() {}, close() {} }),
    () => ({ imports: new Date(), bindInstance() {}, close() {} }),
  ];
  const inherited = Object.create({ imports: {}, bindInstance() {}, close() {} });
  invalidPreparations.push(() => inherited);
  const symbolic = { imports: {}, bindInstance() {}, close() {} };
  symbolic[Symbol("invalid")] = true;
  invalidPreparations.push(() => symbolic);
  const accessor = { imports: {}, bindInstance() {} };
  Object.defineProperty(accessor, "close", { enumerable: true, get: () => () => {} });
  invalidPreparations.push(() => accessor);
  const importAccessor = { bindInstance() {}, close() {} };
  Object.defineProperty(importAccessor, "imports", { enumerable: true, value: {} });
  Object.defineProperty(importAccessor.imports, "module", { enumerable: true, get: () => ({}) });
  invalidPreparations.push(() => importAccessor);

  for (const prepareInterop of invalidPreparations) {
    const result = await execute(request(prepareInterop, {
      instantiate() { calls.push("instantiate"); },
    }));
    assert.equal(result.primaryFailure.code, "host.interop-prepare");
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
  assert.deepEqual(calls, []);
});

test("contains instantiation, binding, and execution-boundary failures", async () => {
  const loaded = await loadedRawModule();
  for (const mode of ["instantiate", "invalid-result", "bind", "loaded-accessor", "loaded-null"]) {
    let closes = 0;
    const execute = createRawExecutionStrategy(async () => {
      if (mode === "loaded-null") return null;
      if (mode !== "loaded-accessor") return loaded;
      return {
        adapter: loaded.adapter,
        interopManifest: loaded.interopManifest,
        module: loaded.module,
        get abi() { throw new Error("private loaded boundary detail"); },
      };
    });
    const result = await execute(request(() => ({
      imports: { "netwasm.host.v1": { service: () => 0 } },
      bindInstance() {
        if (mode === "bind") throw new Error("private bind detail");
      },
      close() { closes++; },
    }), mode === "instantiate" || mode === "invalid-result" ? {
      instantiate() {
        if (mode === "instantiate") throw new Error("private instantiate detail");
        return {};
      },
    } : {}));
    const loaderFailure = mode === "loaded-accessor" || mode === "loaded-null";
    const expectedCode = loaderFailure
      ? "host.raw-load"
      : mode === "bind"
        ? "host.raw-instance-bind"
        : "host.raw-instantiate";
    assert.equal(result.primaryFailure.code, expectedCode);
    assert.equal(closes, loaderFailure ? 0 : 1);
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
});

test("records interop and caller cleanup failures in ownership order", async () => {
  const loaded = await loadedRawModule();
  const calls = [];
  const execute = createRawExecutionStrategy(async () => loaded);
  const result = await execute(request(() => ({
    imports: { "netwasm.host.v1": { service: () => 0 } },
    bindInstance() {},
    close() { calls.push("interop-close"); throw new Error("private interop close"); },
  }), {
    releaseActions: [release("host.caller-close", () => {
      calls.push("caller-close");
      throw new Error("private caller close");
    })],
  }));

  assert.deepEqual(calls, ["interop-close", "caller-close"]);
  assert.equal(result.primaryFailure.code, "host.interop-close");
  assert.deepEqual(result.cleanupFailures.map(value => value.code), ["host.caller-close"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("contains unexpected raw executor failures and closes managed interop", async () => {
  const loaded = await loadedRawModule();
  const calls = [];
  let abortedReads = 0;
  const signal = {
    get aborted() {
      abortedReads++;
      if (abortedReads === 4) throw new Error("private signal detail");
      return false;
    },
    addEventListener() {},
    removeEventListener() {},
  };
  const execute = createRawExecutionStrategy(async () => loaded);
  const result = await execute(request(() => ({
    imports: { "netwasm.host.v1": { service: () => 0 } },
    bindInstance() { calls.push("bind"); },
    close() { calls.push("interop-close"); },
  }), {
    instantiate() { calls.push("instantiate"); },
    signal,
  }));

  assert.equal(abortedReads, 4);
  assert.equal(result.primaryFailure.code, "host.raw-execution");
  assert.deepEqual(calls, ["interop-close"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("validates the factory and exact request before loading", async () => {
  for (const loader of [null, undefined, {}, "load"]) {
    assert.throws(() => createRawExecutionStrategy(loader), /loading action/);
  }
  let loads = 0;
  const execute = createRawExecutionStrategy(async () => { loads++; });
  const base = request(() => ({ imports: {}, bindInstance() {}, close() {} }));
  for (const invalid of [
    null,
    1,
    [],
    {},
    { artifacts: [], contractKey: commandExecutionContract, prepareInterop() {} },
    { ...base, extra: true },
    Object.create(base),
  ]) {
    await assert.rejects(() => execute(invalid), /request/);
  }
  const symbolic = { ...base, [Symbol("invalid")]: true };
  await assert.rejects(() => execute(symbolic), /request/);
  const accessor = { ...base };
  Object.defineProperty(accessor, "providers", { enumerable: true, get: () => ({}) });
  await assert.rejects(() => execute(accessor), /request/);

  const invalidValues = [
    { contractKey: "unsupported@1.0.0" },
    { prepareInterop: null },
    { instantiate: null },
    { signal: {} },
    { signal: { aborted: false } },
    { signal: { aborted: false, addEventListener() {} } },
    { schedule: null },
    { providers: null },
    { providers: [] },
    { providers: new Date() },
    { releaseActions: null },
  ];
  for (const invalid of invalidValues) {
    await assert.rejects(() => execute({ ...base, ...invalid }), TypeError);
  }
  const providerSymbol = { [Symbol("invalid")]: true };
  await assert.rejects(() => execute({ ...base, providers: providerSymbol }), TypeError);
  const providerAccessor = {};
  Object.defineProperty(providerAccessor, "module", { enumerable: true, get: () => ({}) });
  await assert.rejects(() => execute({ ...base, providers: providerAccessor }), TypeError);
  assert.equal(loads, 0);
});

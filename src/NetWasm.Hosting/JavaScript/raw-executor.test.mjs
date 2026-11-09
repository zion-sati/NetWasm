import assert from "node:assert/strict";
import test from "node:test";
import { commandExecutionContract as commandComponentContract } from "./execution-contracts.mjs";
import { executeRaw } from "./raw-executor.mjs";

const functionImport = (module, name) => ({ module, name, kind: "function" });
const functionExport = name => ({ name, kind: "function" });
const commandAbi = (returnShape = "exitCode") => ({
  target: "wasm32",
  entryPoint: { parameterShape: "none", returnShape, completionShape: "synchronous" },
  imports: [functionImport("platform", "value")],
  exports: [functionExport("run")],
});
const asyncCommandAbi = () => ({
  target: "wasm32",
  entryPoint: { parameterShape: "none", returnShape: "exitCode", completionShape: "asynchronous" },
  imports: [functionImport("platform", "value"), functionImport("$root", "[waitable-set-new]")],
  exports: [
    functionExport("run"),
    functionExport("netwasm.entry.callback"),
    functionExport("netwasm.entry.exit_code"),
  ],
});
const fingerprint = `sha256:${"1".repeat(64)}`;
const emptyAdapter = (target = "wasm32") => {
  const metadata = Object.freeze({
    abiVersion: 2,
    bindingIdentities: Object.freeze([]),
    requiredCapabilities: Object.freeze([]),
    target,
    witSourceFingerprint: fingerprint,
  });
  return Object.freeze({
    rawAdapterMetadata: metadata,
    createAdapter: () => Object.freeze({
      imports: Object.freeze(Object.create(null)),
      metadata,
    }),
  });
};
const canonicalIdentity = {
  module: "cm32p2|sample:raw/api@1",
  name: "read-value",
};
// The code a generated adapter emits for read-value: () -> u32, with its provider resolved
// when bound.
function provider(canonical, interfaceName, name) {
  const source = canonical.providers[interfaceName];
  if (source === null || typeof source !== "object") {
    throw new TypeError(`raw logical provider '${interfaceName}' is unavailable`);
  }
  if (typeof source[name] !== "function") throw new TypeError("raw provider function is invalid");
  return source;
}
const readValue = canonical => {
  const source = provider(canonical, "sample:raw@1/api", "readValue");
  return () => {
    canonical.memory();
    canonical.store();
    const value = source.readValue();
    if (!Number.isInteger(value) || value < 0 || value > 0xffff_ffff) {
      throw new TypeError("raw canonical u32 value is invalid");
    }
    return value | 0;
  };
};
const canonicalAdapter = () => {
  const metadata = Object.freeze({
    abiVersion: 2,
    bindingIdentities: Object.freeze([Object.freeze(canonicalIdentity)]),
    requiredCapabilities: Object.freeze(["canonical"]),
    target: "wasm32",
    witSourceFingerprint: fingerprint,
  });
  return Object.freeze({
    rawAdapterMetadata: metadata,
    createAdapter(request) {
      const module = Object.freeze({
        [canonicalIdentity.name]: readValue(request.canonical),
      });
      return Object.freeze({
        imports: Object.freeze({ [canonicalIdentity.module]: module }),
        metadata,
      });
    },
  });
};
const rawInstance = exports => ({
  exports: {
    cm32p2_memory: new WebAssembly.Memory({ initial: 1 }),
    cm32p2_realloc() { return 0; },
    ...exports,
  },
});
const base = () => ({
  adapter: emptyAdapter(),
  module: {},
  physicalProviders: { platform: { value() {} } },
  providers: {},
  releaseActions: [],
});
test("executes a raw command through the exact projected imports", async () => {
  const calls = [];
  const module = {};
  const result = await executeRaw({
    ...base(),
    module,
    contractKey: commandComponentContract,
    abi: commandAbi(),
    async instantiate(request) {
      assert.equal(Object.isFrozen(request), true);
      assert.equal(request.module, module);
      assert.deepEqual(Object.keys(request.imports), ["platform"]);
      calls.push("instantiate");
      return rawInstance({ run() { calls.push("run"); return -19; } });
    },
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, -19);
  assert.deepEqual(calls, ["instantiate", "run"]);
});

test("drains a terminal report when managed cleanup cannot be bound", async () => {
  const calls = [];
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({
      run() { throw new WebAssembly.RuntimeError("terminal managed failure"); },
    }),
    consumeTerminalEvent() {
      calls.push("consume");
      return { typeId: 7 };
    },
    async drainTerminalReports() { calls.push("drain"); },
  });

  assert.equal(result.primaryFailure.code, "host.command-invoke");
  assert.deepEqual(calls, ["consume", "drain"]);
});

test("clears the active exception before returning a managed command failure", async () => {
  const calls = [];
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({
      run() { throw new WebAssembly.RuntimeError("terminal managed failure"); },
      exception_clear_active() { calls.push("clear"); },
    }),
    consumeTerminalEvent() {
      calls.push("consume");
      return { typeId: 7 };
    },
    async drainTerminalReports() { calls.push("drain"); },
  });

  assert.equal(result.primaryFailure.code, "managed.failure");
  assert.deepEqual(calls, ["consume", "clear", "drain"]);
});

test("preserves an unreported raw trap as a host command failure", async () => {
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({
      run() { throw new WebAssembly.RuntimeError("ordinary raw trap"); },
    }),
  });

  assert.equal(result.primaryFailure.code, "host.command-invoke");
});

test("executes a generated canonical import beside an explicit physical import", async () => {
  let canonicalImport;
  const result = await executeRaw({
    ...base(),
    adapter: canonicalAdapter(),
    contractKey: commandComponentContract,
    abi: {
      ...commandAbi(),
      imports: [
        functionImport(canonicalIdentity.module, canonicalIdentity.name),
        functionImport("platform", "value"),
      ],
    },
    providers: {
      "sample:raw@1/api": { readValue() { return 40; } },
    },
    physicalProviders: { platform: { value() { return 2; } } },
    instantiate: async request => {
      canonicalImport = request.imports[canonicalIdentity.module][canonicalIdentity.name];
      return rawInstance({
        run() { return canonicalImport() + request.imports.platform.value(); },
      });
    },
  });
  assert.equal(result.exitCode, 42);
  assert.throws(() => canonicalImport(), /closed/);
});

test("closes caller and canonical state when raw adapter preparation fails", async () => {
  for (const [contractKey, abi] of [
    [commandComponentContract, commandAbi()],
    [commandComponentContract, asyncCommandAbi()],
  ]) {
    for (const adapter of [{}, emptyAdapter("wasm64")]) {
      const calls = [];
      const result = await executeRaw({
        ...base(),
        adapter,
        contractKey,
        abi,
        instantiate() { calls.push("instantiate"); },
        releaseActions: [{
          code: "host.caller",
          message: "close",
          release() { calls.push("close"); },
        }],
      });
      assert.equal(result.primaryFailure.code, "contract.raw-abi");
      assert.deepEqual(calls, ["close"]);
    }
  }
});

test("closes caller resources for ABI failure and pre-instantiation cancellation", async () => {
  for (const [abi, signal, expectedCode] of [
    [{ ...commandAbi(), exports: [] }, null, "contract.raw-abi"],
    [commandAbi(), new AbortController().signal, "caller.cancelled"],
  ]) {
    if (signal) signal.onabort = null;
    const controller = signal ? new AbortController() : null;
    if (controller) controller.abort();
    const calls = [];
    const result = await executeRaw({
      ...base(),
      contractKey: commandComponentContract,
      abi,
      signal: controller?.signal ?? signal,
      instantiate() { calls.push("instantiate"); },
      releaseActions: [{ code: "host.caller", message: "close", release() { calls.push("close"); } }],
    });
    assert.equal(result.primaryFailure.code, expectedCode);
    assert.deepEqual(calls, ["close"]);
  }
});

test("maps raw instantiation and instantiated-export failures with their causes", async () => {
  const failedInstantiation = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate() { throw new Error("private instantiate detail"); },
  });
  assert.equal(failedInstantiation.primaryFailure.code, "host.raw-instantiate");
  assert.equal(failedInstantiation.primaryFailure.detail, "Error: private instantiate detail");

  const failedBinding = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({}),
  });
  assert.equal(failedBinding.primaryFailure.code, "contract.raw-exports");

  const failedInstanceBinding = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({ run: () => 0 }),
    bindInstance() { throw new Error("private instance binding detail"); },
  });
  assert.equal(failedInstanceBinding.primaryFailure.code, "host.raw-instance-bind");
  assert.equal(failedInstanceBinding.primaryFailure.detail, "Error: private instance binding detail");
});

test("drives an asynchronous Main through the instance's Component Model", async () => {
  const calls = [];
  let observer;
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: asyncCommandAbi(),
    subscribeInteropCompletion(observe) {
      observer = observe;
      return () => { calls.push("unsubscribe"); observer = undefined; };
    },
    instantiate: async request => {
      assert.deepEqual(Object.keys(request.imports).sort(), ["$root", "platform"]);
      const newSet = request.imports.$root["[waitable-set-new]"];
      return rawInstance({
        run() {
          calls.push("run");
          // A JavaScript completion arrives while Main waits on its set.
          setTimeout(() => observer(), 0);
          return 2 | (newSet() << 4);
        },
        "netwasm.entry.callback"(...event) { calls.push(["callback", ...event]); return 0; },
        "netwasm.entry.exit_code"() { calls.push("exit"); return 5; },
      });
    },
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 5);
  assert.deepEqual(calls, ["run", ["callback", 0, 0, 0], "exit", "unsubscribe"]);
});

test("keeps the Component Model's own built-ins from physical providers", async () => {
  const calls = [];
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: asyncCommandAbi(),
    physicalProviders: { platform: { value() {} }, $root: { "[waitable-set-new]"() { return 1; } } },
    instantiate() { calls.push("instantiate"); },
    releaseActions: [{ code: "host.caller", message: "close", release() { calls.push("close"); } }],
  });
  assert.equal(result.primaryFailure.code, "contract.raw-abi");
  assert.match(result.primaryFailure.detail, /reserved/);
  assert.deepEqual(calls, ["close"]);
});

test("validates the raw execution request before invoking dependencies", async () => {
  for (const request of [null, [], 1]) {
    await assert.rejects(() => executeRaw(request), /request/);
  }
  const valid = {
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate: async () => rawInstance({ run: () => 0 }),
  };
  for (const mutation of [
    { contractKey: "other@1.0.0" },
    { module: null },
    { abi: null },
    { adapter: null },
    { providers: null },
    { physicalProviders: null },
    { instantiate: null },
    { bindInstance: null },
    { consumeTerminalEvent: null },
    { drainTerminalReports: null },
    { instanceReleaseActions: null },
    { releaseActions: null },
    { signal: {} },
  ]) {
    await assert.rejects(() => executeRaw({ ...valid, ...mutation }), TypeError);
  }
});

test("validates and snapshots cleanup before creating execution resources", async () => {
  let adapterCreations = 0;
  const guardedAdapter = {
    ...emptyAdapter(),
    createAdapter() { adapterCreations++; throw new Error("must not create"); },
  };
  const invalidActions = [
    { instanceReleaseActions: [{ code: "host.invalid", message: "invalid", release: null }] },
    { releaseActions: [{ code: "INVALID", message: "invalid", release() {} }] },
  ];
  for (const mutation of invalidActions) {
    await assert.rejects(() => executeRaw({
      ...base(),
      adapter: guardedAdapter,
      contractKey: commandComponentContract,
      abi: asyncCommandAbi(),
      instantiate: async () => rawInstance({}),
      ...mutation,
    }), TypeError);
  }
  assert.equal(adapterCreations, 0);

  const calls = [];
  const instanceReleaseActions = [{
    code: "host.instance-original",
    message: "close original instance",
    release() { calls.push("instance-original"); },
  }];
  const releaseActions = [{
    code: "host.caller-original",
    message: "close original caller",
    release() { calls.push("caller-original"); },
  }];
  const result = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instanceReleaseActions,
    releaseActions,
    instantiate: async () => {
      instanceReleaseActions.splice(0, 1, {
        code: "host.instance-mutated",
        message: "close mutated instance",
        release() { calls.push("instance-mutated"); },
      });
      releaseActions.length = 0;
      return rawInstance({ run: () => 0 });
    },
  });
  assert.equal(result.exitCode, 0);
  assert.deepEqual(calls, ["instance-original", "caller-original"]);
});

test("rejects an invalid interop observer subscription before guest entry", async () => {
  await assert.rejects(() => executeRaw({ ...base(), contractKey: commandComponentContract,
    abi: commandAbi(), instantiate: async () => { assert.fail("must not enter guest"); }, subscribeInteropCompletion: null }), /subscription/);
});

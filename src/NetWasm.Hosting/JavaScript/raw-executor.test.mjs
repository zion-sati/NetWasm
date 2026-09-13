import assert from "node:assert/strict";
import test from "node:test";
import {
  commandExecutionContract as commandComponentContract,
  processExecutionContract as processComponentContract,
} from "./execution-contracts.mjs";
import { executeRaw } from "./raw-executor.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const functionImport = (module, name) => ({ module, name, kind: "function" });
const functionExport = name => ({ name, kind: "function" });
const commandAbi = (returnShape = "exitCode") => ({
  target: "wasm32",
  entryPoint: { parameterShape: "none", returnShape, completionShape: "synchronous" },
  imports: [functionImport("platform", "value")],
  exports: [functionExport("run")],
});
const processAbi = () => ({
  target: "wasm32",
  entryPoint: { parameterShape: "none", returnShape: "exitCode", completionShape: "asynchronous" },
  imports: [
    functionImport("platform", "value"),
    functionImport("cm32p2|fixture:reactor/source@1", "get-pollable"),
    functionImport("cm32p2|netwasm:runtime/reactor-host@1", "watch"),
    functionImport("cm32p2|netwasm:runtime/reactor-host@1", "cancel"),
  ],
  exports: [
    functionExport("run"),
    functionExport("netwasm.process.status"),
    functionExport("netwasm.process.result"),
    functionExport("netwasm.process.complete"),
    functionExport("cm32p2|netwasm:runtime/reactor-guest@1|wake"),
  ],
});
const fingerprint = `sha256:${"1".repeat(64)}`;
const emptyAdapter = (target = "wasm32") => {
  const metadata = Object.freeze({
    abiVersion: 1,
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
const pollableIdentity = {
  module: "cm32p2|fixture:reactor/source@1",
  name: "get-pollable",
};
const reactorModule = "cm32p2|netwasm:runtime/reactor-host@1";
const watchIdentity = { module: reactorModule, name: "watch" };
const cancelIdentity = { module: reactorModule, name: "cancel" };
const canonicalAdapter = () => {
  const metadata = Object.freeze({
    abiVersion: 1,
    bindingIdentities: Object.freeze([Object.freeze(canonicalIdentity)]),
    requiredCapabilities: Object.freeze(["bindCallable"]),
    target: "wasm32",
    witSourceFingerprint: fingerprint,
  });
  const binding = Object.freeze({
    kind: "callable",
    target: "wasm32",
    physical: canonicalIdentity,
    coreSignature: Object.freeze({ parameters: [], results: ["i32"] }),
    provider: Object.freeze({
      interface: "sample:raw@1/api",
      function: "read-value",
      javascriptName: "readValue",
      functionKind: "freestanding",
      resourceType: null,
      resourceName: null,
      resourceJavaScriptName: null,
    }),
    parameters: Object.freeze([]),
    result: Object.freeze({ kind: "u32" }),
    canonicalSignature: Object.freeze({
      parameters: [],
      result: "i32",
      flatParameters: [],
      flatResults: ["i32"],
      indirectParameters: false,
      indirectResult: false,
    }),
    parameterMemory: Object.freeze({ size: 0, alignment: 1 }),
    resultMemory: Object.freeze({ size: 4, alignment: 4 }),
  });
  return Object.freeze({
    rawAdapterMetadata: metadata,
    createAdapter(request) {
      const module = Object.freeze({
        [canonicalIdentity.name]: request.bindCallable(binding),
      });
      return Object.freeze({
        imports: Object.freeze({ [canonicalIdentity.module]: module }),
        metadata,
      });
    },
  });
};
const pollableBinding = Object.freeze({
  kind: "callable",
  target: "wasm32",
  physical: Object.freeze(pollableIdentity),
  coreSignature: Object.freeze({ parameters: [], results: ["i32"] }),
  provider: Object.freeze({
    interface: "fixture:reactor@1/source",
    function: "get-pollable",
    javascriptName: "getPollable",
    functionKind: "freestanding",
    resourceType: null,
    resourceName: null,
    resourceJavaScriptName: null,
  }),
  parameters: Object.freeze([]),
  result: Object.freeze({ kind: "owned-resource", resourceType: 7 }),
  canonicalSignature: Object.freeze({
    parameters: [], result: "i32", flatParameters: [], flatResults: ["i32"],
    indirectParameters: false, indirectResult: false,
  }),
  parameterMemory: Object.freeze({ size: 0, alignment: 1 }),
  resultMemory: Object.freeze({ size: 4, alignment: 4 }),
});
const reactorBinding = operation => Object.freeze({
  kind: "callable",
  target: "wasm32",
  physical: Object.freeze(operation === "watch" ? watchIdentity : cancelIdentity),
  coreSignature: Object.freeze({
    parameters: Object.freeze(operation === "watch" ? ["i32", "i32"] : ["i32"]),
    results: Object.freeze([]),
  }),
  provider: Object.freeze({
    interface: "netwasm:runtime/reactor-host@1.0.0",
    function: operation,
    javascriptName: operation,
    functionKind: "freestanding",
    resourceType: null,
    resourceName: null,
    resourceJavaScriptName: null,
  }),
  parameters: Object.freeze(operation === "watch"
    ? [
        Object.freeze({
          name: "ready",
          javascriptName: "ready",
          type: Object.freeze({ kind: "owned-resource", resourceType: 7 }),
        }),
        Object.freeze({ name: "token", javascriptName: "token", type: Object.freeze({ kind: "u32" }) }),
      ]
    : [Object.freeze({ name: "token", javascriptName: "token", type: Object.freeze({ kind: "u32" }) })]),
  result: null,
  canonicalSignature: Object.freeze({
    parameters: Object.freeze(operation === "watch" ? ["i32", "i32"] : ["i32"]),
    result: null,
    flatParameters: Object.freeze(operation === "watch" ? ["i32", "i32"] : ["i32"]),
    flatResults: Object.freeze([]),
    indirectParameters: false,
    indirectResult: false,
  }),
  parameterMemory: Object.freeze({ size: operation === "watch" ? 8 : 4, alignment: 4 }),
  resultMemory: null,
});
const processAdapter = () => {
  const identities = Object.freeze([
    Object.freeze(pollableIdentity),
    Object.freeze(watchIdentity),
    Object.freeze(cancelIdentity),
  ]);
  const metadata = Object.freeze({
    abiVersion: 1,
    bindingIdentities: identities,
    requiredCapabilities: Object.freeze(["bindCallable", "bindReactor"]),
    target: "wasm32",
    witSourceFingerprint: fingerprint,
  });
  return Object.freeze({
    rawAdapterMetadata: metadata,
    createAdapter(request) {
      return Object.freeze({
        imports: Object.freeze({
          [pollableIdentity.module]: Object.freeze({
            [pollableIdentity.name]: request.bindCallable(pollableBinding),
          }),
          [reactorModule]: Object.freeze({
            watch: request.bindReactor(reactorBinding("watch")),
            cancel: request.bindReactor(reactorBinding("cancel")),
          }),
        }),
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
const processBase = (getPollable = () => ({ block() {} })) => ({
  ...base(),
  adapter: processAdapter(),
  providers: {
    "fixture:reactor@1/source": { getPollable },
  },
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
    [processComponentContract, processAbi()],
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

test("maps raw instantiation and instantiated-export failures without leaking details", async () => {
  const failedInstantiation = await executeRaw({
    ...base(),
    contractKey: commandComponentContract,
    abi: commandAbi(),
    instantiate() { throw new Error("private instantiate detail"); },
  });
  assert.equal(failedInstantiation.primaryFailure.code, "host.raw-instantiate");
  assert.doesNotMatch(JSON.stringify(failedInstantiation), /private/);

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
  assert.doesNotMatch(JSON.stringify(failedInstanceBinding), /private/);
});

test("observes an immediate raw managed process and closes in ownership order", async () => {
  const calls = [];
  const result = await executeRaw({
    ...processBase(),
    contractKey: processComponentContract,
    abi: processAbi(),
    instantiate: async request => {
      assert.deepEqual(Object.keys(request.imports["cm32p2|netwasm:runtime/reactor-host@1"]),
        ["watch", "cancel"]);
      calls.push("instantiate");
      return rawInstance({
        run() { calls.push("start"); return 7; },
        "netwasm.process.status"() { calls.push("status"); return 1; },
        "netwasm.process.result"() { calls.push("result"); return 23; },
        "netwasm.process.complete"() { calls.push("complete"); },
        "cm32p2|netwasm:runtime/reactor-guest@1|wake"() { calls.push("wake"); },
      });
    },
    releaseActions: [{ code: "host.caller", message: "close", release() { calls.push("caller-close"); } }],
  });
  assert.equal(result.exitCode, 23);
  assert.deepEqual(calls, ["instantiate", "start", "status", "result", "complete", "caller-close"]);
});

test("closes reactor, instance services, canonical state, and caller resources in order", async () => {
  const calls = [];
  let canonicalImport;
  let nextPollable = 0;
  const result = await executeRaw({
    ...processBase(() => {
      const id = ++nextPollable;
      return {
        block: () => new Promise(() => {}),
        [resourceDisposeSymbol]() { calls.push(`pollable-close:${id}`); },
      };
    }),
    contractKey: processComponentContract,
    abi: processAbi(),
    schedule() {},
    instantiate: async request => {
      canonicalImport = request.imports[pollableIdentity.module][pollableIdentity.name];
      return rawInstance({
        run() {
          const handle = canonicalImport();
          request.imports[reactorModule].watch(handle, 17);
          return 1;
        },
        "netwasm.process.status": () => 1,
        "netwasm.process.result": () => 0,
        "netwasm.process.complete": () => { calls.push("complete"); },
        "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {},
      });
    },
    instanceReleaseActions: [{
      code: "host.interop-close",
      message: "close interop",
      release() {
        calls.push("interop-close");
        canonicalImport();
      },
    }],
    releaseActions: [{
      code: "host.caller-close",
      message: "close caller",
      release() {
        calls.push("caller-close");
        assert.throws(() => canonicalImport(), /closed/);
      },
    }],
  });
  assert.equal(result.exitCode, 0);
  assert.deepEqual(calls, [
    "complete",
    "pollable-close:1",
    "interop-close",
    "pollable-close:2",
    "caller-close",
  ]);
});

test("uses guarded readiness to resume a deferred raw process", async () => {
  const jobs = [];
  const calls = [];
  let status = 0;
  const result = executeRaw({
    ...processBase(() => ({
      block() { calls.push("block"); status = 1; return Promise.resolve(); },
      [resourceDisposeSymbol]() { calls.push("dispose"); },
    })),
    contractKey: processComponentContract,
    abi: processAbi(),
    schedule: callback => jobs.push(callback),
    instantiate: async request => rawInstance({
      run() {
        calls.push("start");
        assert.equal(
          request.imports["cm32p2|netwasm:runtime/reactor-host@1"].cancel(99),
          undefined);
        const handle = request.imports[pollableIdentity.module][pollableIdentity.name]();
        request.imports["cm32p2|netwasm:runtime/reactor-host@1"].watch(handle, 13);
        return 7;
      },
      "netwasm.process.status"() { calls.push("status"); return status; },
      "netwasm.process.result"() { calls.push("result"); return 29; },
      "netwasm.process.complete"() { calls.push("complete"); },
      "cm32p2|netwasm:runtime/reactor-guest@1|wake"(token) { calls.push(`wake:${token}`); },
    }),
  });
  await Promise.resolve();
  assert.deepEqual(calls, ["start", "status"]);
  jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal((await result).exitCode, 29);
  assert.deepEqual(calls, ["start", "status", "block", "dispose", "wake:13", "status", "result", "complete"]);
});

test("rejects initialization-time raw reactor use even when instantiation catches it", async () => {
  const result = await executeRaw({
    ...processBase(),
    contractKey: processComponentContract,
    abi: processAbi(),
    instantiate: async request => {
      try {
        request.imports["cm32p2|netwasm:runtime/reactor-host@1"].cancel(1);
      } catch {}
      return { exports: {} };
    },
  });
  assert.equal(result.primaryFailure.code, "contract.reactor-initialization");
});

test("maps every raw process setup and binding failure before guest execution", async () => {
  const controller = new AbortController();
  controller.abort();
  const cases = [
    {
      abi: { ...processAbi(), exports: [] },
      instantiate: async () => { throw new Error("must not instantiate"); },
      expected: "contract.raw-abi",
    },
    {
      abi: processAbi(),
      signal: controller.signal,
      instantiate: async () => { throw new Error("must not instantiate"); },
      expected: "caller.cancelled",
    },
    {
      abi: processAbi(),
      physicalProviders: {},
      instantiate: async () => { throw new Error("must not instantiate"); },
      expected: "contract.raw-abi",
    },
    {
      abi: processAbi(),
      instantiate: async () => { throw new Error("private instantiate failure"); },
      expected: "host.raw-instantiate",
    },
    {
      abi: processAbi(),
      instantiate: async request => {
        try {
          request.imports["cm32p2|netwasm:runtime/reactor-host@1"].watch({}, 1);
        } catch {}
        throw new Error("private initialization failure");
      },
      expected: "contract.reactor-initialization",
    },
    {
      abi: processAbi(),
      instantiate: async () => rawInstance({}),
      expected: "contract.raw-exports",
    },
    {
      abi: processAbi(),
      instantiate: async () => rawInstance({}),
      bindInstance() { throw new Error("private binding failure"); },
      expected: "host.raw-instance-bind",
    },
  ];
  for (const item of cases) {
    const request = processBase();
    const result = await executeRaw({
      ...request,
      contractKey: processComponentContract,
      abi: item.abi,
      physicalProviders: item.physicalProviders ?? request.physicalProviders,
      signal: item.signal,
      instantiate: item.instantiate,
      bindInstance: item.bindInstance,
    });
    assert.equal(result.primaryFailure.code, item.expected);
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
});

test("contains a rejected raw pollable as an opaque observation failure", async () => {
  const jobs = [];
  const result = executeRaw({
    ...processBase(() => ({
      block: () => Promise.reject(new Error("private pollable failure")),
      [resourceDisposeSymbol]() {},
    })),
    contractKey: processComponentContract,
    abi: processAbi(),
    schedule: callback => jobs.push(callback),
    instantiate: async request => rawInstance({
      run() {
        const handle = request.imports[pollableIdentity.module][pollableIdentity.name]();
        request.imports["cm32p2|netwasm:runtime/reactor-host@1"].watch(handle, 7);
        return 3;
      },
      "netwasm.process.status": () => 0,
      "netwasm.process.result": () => 0,
      "netwasm.process.complete": () => {},
      "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {
        throw new Error("must not wake");
      },
    }),
  });
  await Promise.resolve();
  jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  const outcome = await result;
  assert.equal(outcome.primaryFailure.code, "host.process-wake");
  assert.doesNotMatch(JSON.stringify(outcome), /private/);
});

test("releases a transferred pollable when the reactor rejects its watch", async () => {
  const releases = [];
  let next = 0;
  const result = await executeRaw({
    ...processBase(() => {
      const id = ++next;
      return {
        block: () => new Promise(() => {}),
        [resourceDisposeSymbol]() { releases.push(id); },
      };
    }),
    contractKey: processComponentContract,
    abi: processAbi(),
    schedule() {},
    instantiate: async request => rawInstance({
      run() {
        const imports = request.imports;
        const first = imports[pollableIdentity.module][pollableIdentity.name]();
        imports[reactorModule].watch(first, 11);
        const rejected = imports[pollableIdentity.module][pollableIdentity.name]();
        imports[reactorModule].watch(rejected, 11);
        return 1;
      },
      "netwasm.process.status": () => 0,
      "netwasm.process.result": () => 0,
      "netwasm.process.complete": () => {},
      "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {},
    }),
  });

  assert.equal(result.primaryFailure.code, "host.process-start");
  assert.deepEqual(releases, [2, 1]);
});

test("rejects a caller-owned logical reactor provider before instantiation", async () => {
  let instantiated = false;
  const request = processBase();
  const result = await executeRaw({
    ...request,
    providers: {
      ...request.providers,
      "netwasm:runtime/reactor-host@1.0.0": { watch() {}, cancel() {} },
    },
    contractKey: processComponentContract,
    abi: processAbi(),
    instantiate() { instantiated = true; },
  });

  assert.equal(result.primaryFailure.code, "contract.raw-abi");
  assert.equal(instantiated, false);
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
    { instanceReleaseActions: null },
    { releaseActions: null },
    { schedule: null },
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
      ...processBase(),
      adapter: guardedAdapter,
      contractKey: processComponentContract,
      abi: processAbi(),
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

test("contains an unexpected observation rejection and closes every owned scope", async () => {
  const calls = [];
  let abortedReads = 0;
  const signal = {
    get aborted() {
      abortedReads++;
      if (abortedReads === 3) throw new Error("private observation detail");
      return false;
    },
    addEventListener() {},
    removeEventListener() {},
  };
  const result = await executeRaw({
    ...processBase(),
    contractKey: processComponentContract,
    abi: processAbi(),
    signal,
    instantiate: async () => rawInstance({
      run: () => 1,
      "netwasm.process.status": () => 1,
      "netwasm.process.result": () => 0,
      "netwasm.process.complete": () => {},
      "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {},
    }),
    instanceReleaseActions: [{
      code: "host.instance-close",
      message: "close instance",
      release() { calls.push("instance-close"); },
    }],
    releaseActions: [{
      code: "host.caller-close",
      message: "close caller",
      release() { calls.push("caller-close"); },
    }],
  });
  assert.equal(result.primaryFailure.code, "host.raw-execution");
  assert.deepEqual(calls, ["instance-close", "caller-close"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("records raw reactor and caller cleanup failures", async () => {
  let disposed = false;
  const result = await executeRaw({
    ...processBase(() => ({
      block: () => new Promise(() => {}),
      [resourceDisposeSymbol]() { disposed = true; throw new Error("private reactor close"); },
    })),
    contractKey: processComponentContract,
    abi: processAbi(),
    schedule() {},
    instantiate: async request => rawInstance({
      run() {
        const handle = request.imports[pollableIdentity.module][pollableIdentity.name]();
        request.imports["cm32p2|netwasm:runtime/reactor-host@1"].watch(handle, 9);
        return 3;
      },
      "netwasm.process.status": () => 1,
      "netwasm.process.result": () => 0,
      "netwasm.process.complete": () => {},
      "cm32p2|netwasm:runtime/reactor-guest@1|wake": () => {},
    }),
    releaseActions: [{
      code: "host.caller-close",
      message: "The caller could not close.",
      release() { throw new Error("private caller close"); },
    }],
  });
  assert.equal(disposed, true);
  assert.equal(result.primaryFailure.code, "host.raw-reactor");
  assert.deepEqual(result.cleanupFailures.map(failure => failure.code), ["host.caller-close"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

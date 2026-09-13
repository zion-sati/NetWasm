import assert from "node:assert/strict";
import test from "node:test";
import { createComponentExecutionStrategy } from "./component-execution-strategy.mjs";
import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";

const release = (code, action) => ({
  code,
  message: `The execution host could not release ${code}.`,
  release: action,
});

test("loads and executes a command before closing the outer caller scope", async () => {
  const calls = [];
  const signal = new AbortController().signal;
  const instantiateCore = () => {};
  const execute = createComponentExecutionStrategy(async request => {
    assert.equal(Object.isFrozen(request), true);
    assert.deepEqual(request.artifacts, ["artifact"]);
    assert.equal(request.signal, signal);
    calls.push("load");
    return {
      adapter: {
        contractKey: commandExecutionContract,
        instantiate({ imports, instantiateCore: actualInstantiateCore }) {
          assert.equal(Object.isFrozen(imports), true);
          assert.equal(actualInstantiateCore, instantiateCore);
          calls.push("instantiate");
          return { command: { run() { calls.push("run"); return -17; } } };
        },
      },
      loadCoreModule() {},
    };
  });
  assert.equal(Object.isFrozen(execute), true);

  const result = await execute({
    artifacts: ["artifact"],
    contractKey: commandExecutionContract,
    imports: {},
    instantiateCore,
    signal,
    releaseActions: [release("caller", () => calls.push("caller-release"))],
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, -17);
  assert.deepEqual(calls, ["load", "instantiate", "run", "caller-release"]);
});

test("closes the component reactor before the outer caller scope", async () => {
  const calls = [];
  const jobs = [];
  let reactorHost;
  let status = 0;
  const execute = createComponentExecutionStrategy(async () => ({
    adapter: {
      contractKey: processExecutionContract,
      instantiate({ imports }) {
        reactorHost = imports["netwasm:runtime/reactor-host"];
        return {
          process: {
            start() {
              reactorHost.watch({
                block: () => Promise.resolve(),
                dispose: () => calls.push("reactor-release"),
              }, 7);
              return 1;
            },
            status() { return status; },
            exitCode() { return 29; },
            complete() { calls.push("complete"); },
          },
          reactorGuest: { wake() { status = 1; } },
        };
      },
    },
    loadCoreModule() {},
  }));
  const resultPromise = execute({
    artifacts: [],
    contractKey: processExecutionContract,
    imports: {},
    schedule: callback => jobs.push(callback),
    releaseActions: [release("caller", () => calls.push("caller-release"))],
  });
  await Promise.resolve();
  await Promise.resolve();
  jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  const result = await resultPromise;
  assert.equal(result.exitCode, 29);
  assert.deepEqual(calls, ["reactor-release", "complete", "caller-release"]);
});

test("maps load failure opaquely and closes caller resources", async () => {
  const calls = [];
  const execute = createComponentExecutionStrategy(() => {
    calls.push("load");
    throw new Error("private loader detail");
  });
  const result = await execute({
    artifacts: [],
    contractKey: commandExecutionContract,
    imports: {},
    releaseActions: [release("caller", () => calls.push("release"))],
  });
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.component-load");
  assert.deepEqual(calls, ["load", "release"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("maps pre-load and in-load cancellation without guest entry", async () => {
  const before = new AbortController();
  before.abort();
  let loads = 0;
  const executeBefore = createComponentExecutionStrategy(async () => { loads++; });
  const beforeResult = await executeBefore({
    artifacts: [],
    contractKey: commandExecutionContract,
    imports: {},
    signal: before.signal,
  });
  assert.equal(beforeResult.completionKind, "callerCancellation");
  assert.equal(loads, 0);

  const during = new AbortController();
  const executeDuring = createComponentExecutionStrategy(async () => {
    loads++;
    during.abort();
    throw new Error("abort detail");
  });
  const duringResult = await executeDuring({
    artifacts: [],
    contractKey: commandExecutionContract,
    imports: {},
    signal: during.signal,
  });
  assert.equal(duringResult.completionKind, "callerCancellation");
  assert.equal(duringResult.primaryFailure.code, "caller.cancelled");
  assert.equal(loads, 1);
  assert.doesNotMatch(JSON.stringify(duringResult), /detail/);
});

test("maps invalid loaded boundaries as opaque execution failure", async () => {
  for (const loaded of [null, {}, { adapter: {}, loadCoreModule() {} }]) {
    const result = await createComponentExecutionStrategy(async () => loaded)({
      artifacts: [],
      contractKey: commandExecutionContract,
      imports: {},
    });
    assert.equal(result.completionKind, "hostFailure");
    assert.equal(result.primaryFailure.code, "host.component-execution");
  }
});

test("appends caller cleanup failures after load and execution outcomes", async () => {
  for (const mode of ["load", "execution"]) {
    const execute = createComponentExecutionStrategy(async () => {
      if (mode === "load") throw new Error("load");
      return {
        adapter: {
          contractKey: commandExecutionContract,
          instantiate: () => ({ command: { run: () => 0 } }),
        },
        loadCoreModule() {},
      };
    });
    const result = await execute({
      artifacts: [],
      contractKey: commandExecutionContract,
      imports: {},
      releaseActions: [release("caller", () => { throw new Error("cleanup"); })],
    });
    assert.equal(result.completionKind, "hostFailure");
    if (mode === "load") {
      assert.equal(result.primaryFailure.code, "host.component-load");
      assert.deepEqual(result.cleanupFailures.map(value => value.code), ["caller"]);
    } else {
      assert.equal(result.primaryFailure.code, "caller");
      assert.deepEqual(result.cleanupFailures, []);
    }
  }
});

test("validates factory, request and common inputs before loading", async () => {
  for (const loadArtifacts of [null, undefined, {}, "load"]) {
    assert.throws(() => createComponentExecutionStrategy(loadArtifacts), /loading action/);
  }
  let loads = 0;
  const execute = createComponentExecutionStrategy(async () => { loads++; });
  for (const request of [null, 1, [], {},
    { artifacts: [], contractKey: commandExecutionContract },
    { artifacts: [], contractKey: commandExecutionContract, imports: {}, extra: true },
    Object.create({ artifacts: [], contractKey: commandExecutionContract, imports: {} })]) {
    await assert.rejects(() => execute(request), /request/);
  }
  const withSymbol = {
    artifacts: [],
    contractKey: commandExecutionContract,
    imports: {},
    [Symbol("invalid")]: true,
  };
  await assert.rejects(() => execute(withSymbol), /request/);
  const withAccessor = { artifacts: [], contractKey: commandExecutionContract };
  Object.defineProperty(withAccessor, "imports", { get: () => ({}), enumerable: true });
  await assert.rejects(() => execute(withAccessor), /request/);
  for (const invalid of [
    { contractKey: "unsupported@1.0.0" },
    { imports: null },
    { instantiateCore: 1 },
    { schedule: null },
    { releaseActions: null },
    { signal: {} },
  ]) {
    await assert.rejects(() => execute({
      artifacts: [],
      contractKey: commandExecutionContract,
      imports: {},
      ...invalid,
    }), TypeError);
  }
  assert.equal(loads, 0);
});

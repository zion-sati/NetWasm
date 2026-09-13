import assert from "node:assert/strict";
import test from "node:test";
import {
  commandComponentContract,
  processComponentContract,
} from "./canonical-component-binder.mjs";
import { executeComponent } from "./component-executor.mjs";

const loadCoreModule = async () => ({});
const instantiateCore = async () => ({});
const release = (code, action) => ({
  code,
  message: `The execution host could not complete ${code}.`,
  release: action,
});
const adapter = (contractKey, instantiate) => ({ contractKey, instantiate });

test("executes one exact command adapter and closes its caller scope", async () => {
  const calls = [];
  const imports = { "wasi:cli/environment": Object.freeze({}) };
  const result = await executeComponent({
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, function (request) {
      assert.equal(this.contractKey, commandComponentContract);
      assert.equal(Object.isFrozen(request), true);
      assert.equal(Object.isFrozen(request.imports), true);
      assert.equal(request.loadCoreModule, loadCoreModule);
      assert.equal(request.instantiateCore, instantiateCore);
      assert.equal(request.imports["wasi:cli/environment"], imports["wasi:cli/environment"]);
      assert.equal(request.imports["netwasm:runtime/reactor-host"], undefined);
      calls.push("instantiate");
      return { command: { run() { calls.push("run"); return 23; } } };
    }),
    imports,
    loadCoreModule,
    instantiateCore,
    releaseActions: [release("host.output", () => calls.push("release"))],
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 23);
  assert.deepEqual(calls, ["instantiate", "run", "release"]);
});

test("rejects adapter mismatch and pre-cancellation before instantiation", async () => {
  for (const expected of [
    ["mismatch", "contractFailure", "contract.component-adapter"],
    ["cancel", "callerCancellation", "caller.cancelled"],
  ]) {
    const calls = [];
    const controller = new AbortController();
    if (expected[0] === "cancel") controller.abort();
    const result = await executeComponent({
      contractKey: commandComponentContract,
      adapter: adapter(
        expected[0] === "mismatch" ? processComponentContract : commandComponentContract,
        () => calls.push("instantiate")),
      imports: {},
      loadCoreModule,
      signal: controller.signal,
      releaseActions: [release("host.output", () => calls.push("release"))],
    });
    assert.equal(result.completionKind, expected[1]);
    assert.equal(result.primaryFailure.code, expected[2]);
    assert.deepEqual(calls, ["release"]);
  }
});

test("maps command instantiation, binding and cleanup failures safely", async () => {
  const cases = [
    [() => { throw new Error("private instantiate detail"); },
      "hostFailure", "host.component-instantiate"],
    [() => ({}), "contractFailure", "contract.component-exports"],
  ];
  for (const [instantiate, kind, code] of cases) {
    const result = await executeComponent({
      contractKey: commandComponentContract,
      adapter: adapter(commandComponentContract, instantiate),
      imports: {},
      loadCoreModule,
      releaseActions: [release("host.output", () => {
        throw new Error("private release detail");
      })],
    });
    assert.equal(result.completionKind, kind);
    assert.equal(result.primaryFailure.code, code);
    assert.deepEqual(result.cleanupFailures.map(failure => failure.code), ["host.output"]);
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
});

test("validates every component dependency before adapter invocation", async () => {
  let instantiated = 0;
  const valid = {
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, () => { instantiated++; }),
    imports: {},
    loadCoreModule,
  };
  for (const request of [null, 1, []]) {
    await assert.rejects(() => executeComponent(request), TypeError);
  }
  for (const contractKey of [undefined, "", "wasi-command@0.2.10"]) {
    await assert.rejects(() => executeComponent({ ...valid, contractKey }), TypeError);
  }
  for (const value of [null, 1, [], {}, { contractKey: "x", instantiate() {}, extra: true },
    { contractKey: null, instantiate() {} }, { contractKey: "x", instantiate: null }]) {
    await assert.rejects(() => executeComponent({ ...valid, adapter: value }), TypeError);
  }
  for (const imports of [null, 1, [], Object.create({ inherited: {} }),
    { "": {} }, { invalid: null }, { invalid: () => {} }]) {
    await assert.rejects(() => executeComponent({ ...valid, imports }), TypeError);
  }
  const symbolImports = { valid: {} };
  symbolImports[Symbol("invalid")] = {};
  await assert.rejects(() => executeComponent({ ...valid, imports: symbolImports }), TypeError);
  const hiddenImports = {};
  Object.defineProperty(hiddenImports, "hidden", { value: {}, enumerable: false });
  await assert.rejects(() => executeComponent({ ...valid, imports: hiddenImports }), TypeError);
  const accessorImports = {};
  Object.defineProperty(accessorImports, "getter", { get: () => ({}), enumerable: true });
  await assert.rejects(() => executeComponent({ ...valid, imports: accessorImports }), TypeError);
  for (const reserved of ["netwasm:runtime/reactor-host",
    "netwasm:runtime/reactor-host@1.0.0"]) {
    await assert.rejects(() => executeComponent({
      ...valid,
      imports: { [reserved]: {} },
    }), /reserved/);
  }
  await assert.rejects(() => executeComponent({ ...valid, loadCoreModule: null }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, instantiateCore: 1 }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, signal: {} }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, releaseActions: null }), TypeError);
  assert.equal(instantiated, 0);
});

test("executes an immediately completed managed process", async () => {
  const calls = [];
  const result = await executeComponent({
    contractKey: processComponentContract,
    adapter: adapter(processComponentContract, ({ imports }) => {
      assert.equal(Object.isFrozen(imports), true);
      assert.equal(typeof imports["netwasm:runtime/reactor-host"].watch, "function");
      calls.push("instantiate");
      return {
        process: {
          start() { calls.push("start"); return 7; },
          status(handle) { assert.equal(handle, 7); calls.push("status"); return 1; },
          exitCode(handle) { assert.equal(handle, 7); calls.push("exit"); return -9; },
          complete(handle) { assert.equal(handle, 7); calls.push("complete"); },
        },
        reactorGuest: { wake() { calls.push("wake"); } },
      };
    }),
    imports: {},
    loadCoreModule,
    releaseActions: [release("host.output", () => calls.push("release"))],
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, -9);
  assert.deepEqual(calls, ["instantiate", "start", "status", "exit", "complete", "release"]);
});

test("delivers pollable readiness before managed process re-observation", async () => {
  const calls = [];
  const jobs = [];
  let processStatus = 0;
  let reactorHost;
  const resultPromise = executeComponent({
    contractKey: processComponentContract,
    adapter: adapter(processComponentContract, ({ imports }) => {
      reactorHost = imports["netwasm:runtime/reactor-host"];
      return {
        process: {
          start() {
            calls.push("start");
            assert.equal(reactorHost.cancel(99), false);
            reactorHost.watch({
              block() { calls.push("block"); return Promise.resolve(); },
              dispose() { calls.push("dispose"); },
            }, 13);
            return 5;
          },
          status() { calls.push("status"); return processStatus; },
          exitCode() { calls.push("exit"); return 31; },
          complete() { calls.push("complete"); },
        },
        reactorGuest: {
          wake(token) {
            assert.equal(token, 13);
            calls.push("wake");
            processStatus = 1;
          },
        },
      };
    }),
    imports: {},
    loadCoreModule,
    schedule: callback => jobs.push(callback),
    releaseActions: [release("host.output", () => calls.push("release"))],
  });
  await Promise.resolve();
  assert.deepEqual(calls, ["start", "status"]);
  jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  const result = await resultPromise;
  assert.equal(result.exitCode, 31);
  assert.deepEqual(calls, [
    "start", "status", "block", "dispose", "wake", "status", "exit",
    "complete", "release",
  ]);
});

test("rejects reactor use during initialization even when the adapter catches it", async () => {
  let started = false;
  const result = await executeComponent({
    contractKey: processComponentContract,
    adapter: adapter(processComponentContract, ({ imports }) => {
      assert.throws(() => imports["netwasm:runtime/reactor-host"].watch({
        block() {},
      }, 1), /before guest entry/);
      return {
        process: {
          start() { started = true; return 1; },
          status() { return 1; },
          exitCode() { return 0; },
          complete() {},
        },
        reactorGuest: { wake() {} },
      };
    }),
    imports: {},
    loadCoreModule,
  });
  assert.equal(result.completionKind, "contractFailure");
  assert.equal(result.primaryFailure.code, "contract.reactor-initialization");
  assert.equal(started, false);
});

test("turns a component pollable failure into safe process failure", async () => {
  const jobs = [];
  let completed = 0;
  const resultPromise = executeComponent({
    contractKey: processComponentContract,
    adapter: adapter(processComponentContract, ({ imports }) => ({
      process: {
        start() {
          imports["netwasm:runtime/reactor-host"].watch({
            block: () => Promise.reject(new Error("private pollable detail")),
            dispose() {},
          }, 17);
          return 4;
        },
        status() { return 0; },
        exitCode() { return 0; },
        complete() { completed++; },
      },
      reactorGuest: { wake() { throw new Error("must not wake"); } },
    })),
    imports: {},
    loadCoreModule,
    schedule: callback => jobs.push(callback),
  });
  await Promise.resolve();
  jobs.shift()();
  await Promise.resolve();
  await Promise.resolve();
  const result = await resultPromise;
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.process-wake");
  assert.equal(completed, 1);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

test("classifies thrown early reactor use and ordinary process setup failures", async () => {
  const cases = [
    [({ imports }) => imports["netwasm:runtime/reactor-host"].cancel(1),
      "contractFailure", "contract.reactor-initialization"],
    [() => { throw new Error("private instantiation detail"); },
      "hostFailure", "host.component-instantiate"],
    [() => ({}), "contractFailure", "contract.component-exports"],
  ];
  for (const [instantiate, kind, code] of cases) {
    const result = await executeComponent({
      contractKey: processComponentContract,
      adapter: adapter(processComponentContract, instantiate),
      imports: {},
      loadCoreModule,
    });
    assert.equal(result.completionKind, kind);
    assert.equal(result.primaryFailure.code, code);
    assert.doesNotMatch(JSON.stringify(result), /private/);
  }
});

test("maps unexpected process observation errors and closes pending pollables first", async () => {
  const calls = [];
  let reactorHost;
  const result = await executeComponent({
    contractKey: processComponentContract,
    adapter: adapter(processComponentContract, ({ imports }) => {
      reactorHost = imports["netwasm:runtime/reactor-host"];
      return {
        process: {
          start() {
            reactorHost.watch({
              block: () => new Promise(() => {}),
              dispose() { calls.push("reactor-release"); },
            }, 3);
            return 9;
          },
          status() { throw new Error("private observation detail"); },
          exitCode() { return 0; },
          complete() { calls.push("complete"); },
        },
        reactorGuest: { wake() {} },
      };
    }),
    imports: {},
    loadCoreModule,
    schedule: () => {},
    releaseActions: [release("host.output", () => calls.push("caller-release"))],
  });
  assert.equal(result.completionKind, "hostFailure");
  assert.equal(result.primaryFailure.code, "host.process-status");
  assert.deepEqual(calls, ["complete", "reactor-release", "caller-release"]);
  assert.doesNotMatch(JSON.stringify(result), /private/);
});

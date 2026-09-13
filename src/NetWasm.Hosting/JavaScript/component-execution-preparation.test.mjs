import assert from "node:assert/strict";
import test from "node:test";
import { prepareComponentExecution } from "./component-execution-preparation.mjs";
import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { normalExecutionResult } from "./execution-result.mjs";

test("prepares immutable common inputs and one snapshotted caller scope", async () => {
  const calls = [];
  const provider = Object.freeze({ getArguments() { return []; } });
  const imports = { "wasi:cli/environment": provider };
  const action = {
    code: "host.output",
    message: "The execution host could not close output.",
    release() { calls.push("original"); },
  };
  const releaseActions = [action];
  const instantiateCore = () => {};
  const schedule = () => {};
  const prepared = prepareComponentExecution({
    contractKey: commandExecutionContract,
    imports,
    instantiateCore,
    releaseActions,
    schedule,
  });

  assert.equal(Object.isFrozen(prepared), true);
  assert.equal(Object.isFrozen(prepared.imports), true);
  assert.equal(Object.getPrototypeOf(prepared.imports), null);
  assert.equal(prepared.imports["wasi:cli/environment"], provider);
  assert.equal(prepared.instantiateCore, instantiateCore);
  assert.equal(prepared.signal, null);
  assert.equal(prepared.schedule, schedule);
  imports["wasi:cli/environment"] = {};
  action.release = () => calls.push("changed");
  releaseActions.length = 0;
  await prepared.closeCallerScope(normalExecutionResult(0));
  assert.deepEqual(calls, ["original"]);
});

test("accepts both exact contracts and defaults optional runtime inputs", () => {
  for (const contractKey of [commandExecutionContract, processExecutionContract]) {
    const prepared = prepareComponentExecution({ contractKey, imports: {} });
    assert.equal(prepared.contractKey, contractKey);
    assert.equal(prepared.instantiateCore, undefined);
    assert.equal(typeof prepared.schedule, "function");
  }
});

test("rejects invalid preparation request shapes", () => {
  for (const request of [null, 1, [], {}, { contractKey: commandExecutionContract },
    { contractKey: commandExecutionContract, imports: {}, extra: true },
    Object.create({ contractKey: commandExecutionContract, imports: {} })]) {
    assert.throws(() => prepareComponentExecution(request), /request/);
  }
  const withSymbol = { contractKey: commandExecutionContract, imports: {}, [Symbol("invalid")]: true };
  assert.throws(() => prepareComponentExecution(withSymbol), /request/);
  const withAccessor = { contractKey: commandExecutionContract };
  Object.defineProperty(withAccessor, "imports", { get: () => ({}), enumerable: true });
  assert.throws(() => prepareComponentExecution(withAccessor), /request/);
});

test("rejects unsupported contracts and invalid optional dependencies", () => {
  for (const contractKey of [null, "", "wasi-command@0.2.10"]) {
    assert.throws(() => prepareComponentExecution({ contractKey, imports: {} }), /unsupported/);
  }
  assert.throws(() => prepareComponentExecution({
    contractKey: commandExecutionContract,
    imports: {},
    instantiateCore: 1,
  }), /instantiator/);
  for (const signal of [{}, { aborted: false }, { aborted: false, addEventListener() {} }]) {
    assert.throws(() => prepareComponentExecution({
      contractKey: commandExecutionContract,
      imports: {},
      signal,
    }), /AbortSignal/);
  }
  assert.throws(() => prepareComponentExecution({
    contractKey: commandExecutionContract,
    imports: {},
    schedule: null,
  }), /scheduler/);
  assert.throws(() => prepareComponentExecution({
    contractKey: commandExecutionContract,
    imports: {},
    releaseActions: null,
  }), /release actions/);
});

test("projects only valid plain import data and reserves the reactor host", () => {
  const invalidImports = [null, 1, [], Object.create({ inherited: {} }),
    { "": {} }, { invalid: null }, { invalid: () => {} }];
  for (const imports of invalidImports) assertInvalidImports(imports);

  const withSymbol = { valid: {}, [Symbol("invalid")]: {} };
  assertInvalidImports(withSymbol);
  const hidden = {};
  Object.defineProperty(hidden, "hidden", { value: {}, enumerable: false });
  assertInvalidImports(hidden);
  const accessor = {};
  Object.defineProperty(accessor, "getter", { get: () => ({}), enumerable: true });
  assertInvalidImports(accessor);
  for (const name of ["netwasm:runtime/reactor-host", "netwasm:runtime/reactor-host@1.0.0"]) {
    assertInvalidImports({ [name]: {} }, /reserved/);
  }
});

function assertInvalidImports(imports, expectation = /imports|import/) {
  assert.throws(() => prepareComponentExecution({
    contractKey: commandExecutionContract,
    imports,
  }), expectation);
}

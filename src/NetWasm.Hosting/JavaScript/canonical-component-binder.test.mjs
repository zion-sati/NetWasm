import assert from "node:assert/strict";
import test from "node:test";
import {
  bindCanonicalComponent,
  commandComponentContract,
  processComponentContract,
} from "./canonical-component-binder.mjs";

test("binds one exact command contract and preserves its receiver", () => {
  const command = {
    value: 19,
    run() { return this.value; },
  };
  const binding = bindCanonicalComponent({
    contractKey: commandComponentContract,
    instance: { command },
  });
  assert.equal(binding.contractKey, commandComponentContract);
  assert.equal(binding.command.run(), 19);
  assert.equal(Object.isFrozen(binding), true);
  assert.equal(Object.isFrozen(binding.command), true);
});

test("binds one exact process contract and preserves both receivers", () => {
  const calls = [];
  const process = {
    value: 7,
    start() { calls.push(this.value); return this.value; },
    status(handle) { calls.push(this.value, handle); return 1; },
    exitCode(handle) { calls.push(this.value, handle); return 23; },
    complete(handle) { calls.push(this.value, handle); },
  };
  const reactorGuest = {
    value: 11,
    wake(token) { calls.push(this.value, token); },
  };
  const binding = bindCanonicalComponent({
    contractKey: processComponentContract,
    instance: { process, reactorGuest },
  });
  assert.equal(binding.process.start(), 7);
  assert.equal(binding.process.status(7), 1);
  assert.equal(binding.process.exitCode(7), 23);
  binding.reactorGuest.wake(13);
  binding.process.complete(7);
  assert.deepEqual(calls, [7, 7, 7, 7, 7, 11, 13, 7, 7]);
  assert.equal(Object.isFrozen(binding.process), true);
  assert.equal(Object.isFrozen(binding.reactorGuest), true);
});

test("rejects invalid requests before contract binding", () => {
  for (const request of [null, 1]) {
    assert.throws(() => bindCanonicalComponent(request), TypeError);
  }
  for (const contractKey of [undefined, null, ""]) {
    assert.throws(() => bindCanonicalComponent({ contractKey, instance: {} }), TypeError);
  }
  for (const instance of [undefined, null, 1]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: commandComponentContract,
      instance,
    }), TypeError);
  }
  assert.throws(() => bindCanonicalComponent({
    contractKey: "wasi-command@0.2.10",
    instance: {},
  }), /unsupported/);
});

test("rejects missing and mixed command bindings without fallback", () => {
  for (const instance of [
    {},
    { command: null },
    { command: 1 },
    { command: {} },
    { command: { run: null } },
  ]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: commandComponentContract,
      instance,
    }), /incomplete/);
  }
  for (const instance of [
    { command: { run() {} }, process: {} },
    { command: { run() {} }, reactorGuest: {} },
  ]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: commandComponentContract,
      instance,
    }), /mixes/);
  }
});

test("rejects missing and mixed process bindings without fallback", () => {
  const completeProcess = {
    start() {},
    status() {},
    exitCode() {},
    complete() {},
  };
  for (const instance of [
    {},
    { process: null, reactorGuest: {} },
    { process: 1, reactorGuest: {} },
    { process: completeProcess, reactorGuest: null },
    { process: completeProcess, reactorGuest: 1 },
  ]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: processComponentContract,
      instance,
    }), /incomplete/);
  }
  for (const operation of ["start", "status", "exitCode", "complete"]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: processComponentContract,
      instance: {
        process: { ...completeProcess, [operation]: null },
        reactorGuest: { wake() {} },
      },
    }), /incomplete/);
  }
  assert.throws(() => bindCanonicalComponent({
    contractKey: processComponentContract,
    instance: { process: completeProcess, reactorGuest: {} },
  }), /reactor guest/);
  assert.throws(() => bindCanonicalComponent({
    contractKey: processComponentContract,
    instance: {
      command: { run() {} },
      process: completeProcess,
      reactorGuest: { wake() {} },
    },
  }), /mixes/);
});

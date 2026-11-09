import assert from "node:assert/strict";
import test from "node:test";
import {
  bindCanonicalComponent,
  commandComponentContract,
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
  for (const contractKey of ["wasi-command@0.2.11", "netwasm:runtime/process@1.0.0"]) {
    assert.throws(() => bindCanonicalComponent({ contractKey, instance: {} }), /unsupported/);
  }
});

test("rejects missing command bindings without fallback", () => {
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
});

test("binds optional diagnostic command and preserves its receiver", () => {
  const diagnosticCommand = { value: { tag: "exited", val: 0 }, run() { return this.value; } };
  const binding = bindCanonicalComponent({
    contractKey: commandComponentContract,
    instance: { command: { run() { return 0; } }, diagnosticCommand },
  });
  assert.equal(binding.diagnosticCommand.run(), diagnosticCommand.value);
  assert.equal(Object.isFrozen(binding.diagnosticCommand), true);
  for (const invalid of [null, 1, {}, { run: null }]) {
    assert.throws(() => bindCanonicalComponent({
      contractKey: commandComponentContract,
      instance: { command: { run() { return 0; } }, diagnosticCommand: invalid },
    }), /diagnostic command component is incomplete/u);
  }
});

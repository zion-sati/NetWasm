import assert from "node:assert/strict";
import test from "node:test";
import { commandExecutionContract } from "./execution-contracts.mjs";
import { buildRawAbiPlan } from "./raw-abi-plan-builder.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";

const functionImport = (module, name) => ({ module, name, kind: "function" });
const functionExport = name => ({ name, kind: "function" });

function commandPlan(imports = []) {
  return buildRawAbiPlan({
    contractKey: commandExecutionContract,
    abi: {
      target: "wasm32",
      entryPoint: {
        parameterShape: "none",
        returnShape: "exitCode",
        completionShape: "synchronous",
      },
      imports,
      exports: [functionExport("run")],
    },
  });
}

function canonicalBinding(imports = {}, target = "wasm32") {
  return { imports, target };
}

function importInventory(plan = commandPlan()) {
  return {
    target: plan.target,
    imports: plan.imports,
  };
}

function compose(overrides = {}) {
  return composeRawImports({
    canonicalBinding: canonicalBinding(),
    inventory: importInventory(),
    physicalProviders: {},
    ...overrides,
  });
}

test("composes exact canonical and separately declared physical imports", () => {
  const canonicalCall = () => 17;
  const memory = {};
  const plan = commandPlan([
    functionImport("cm32p2|sample:api/value@1", "read"),
    { module: "cm32p2|sample:api/value@1", name: "memory", kind: "memory" },
    functionImport("netwasm:runtime/js@1", "invoke"),
  ]);
  const imports = compose({
    inventory: importInventory(plan),
    canonicalBinding: canonicalBinding({
      "cm32p2|sample:api/value@1": { read: canonicalCall },
    }),
    physicalProviders: {
      "cm32p2|sample:api/value@1": { memory },
      "netwasm:runtime/js@1": { invoke: () => 23, unused: true },
      unused: { value: true },
    },
  });

  assert.equal(imports["cm32p2|sample:api/value@1"].read, canonicalCall);
  assert.equal(imports["cm32p2|sample:api/value@1"].memory, memory);
  assert.equal(imports["netwasm:runtime/js@1"].invoke(), 23);
  assert.deepEqual(Object.keys(imports), [
    "cm32p2|sample:api/value@1",
    "netwasm:runtime/js@1",
  ]);
  assert.equal(Object.getPrototypeOf(imports), null);
  assert.equal(Object.isFrozen(imports), true);
  assert.equal(Object.values(imports).every(Object.isFrozen), true);
});

test("rejects malformed requests and forged import inventories", () => {
  for (const request of [null, [], {}, { extra: true }]) {
    assert.throws(() => composeRawImports(request), /request/);
  }
  const valid = importInventory();
  for (const inventory of [
    null,
    [],
    {},
    { ...valid, extra: true },
    { ...valid, target: "wasm128" },
    { ...valid, imports: null },
  ]) {
    assert.throws(() => compose({ inventory }), /inventory/);
  }
});

test("rejects malformed, wrong-target, reserved and invalid canonical imports", () => {
  const moduleName = "cm32p2|sample:api/value@1";
  const plan = commandPlan([functionImport(moduleName, "read")]);
  for (const binding of [
    null,
    [],
    {},
    { imports: {}, target: "wasm32", extra: true },
    canonicalBinding({}, "wasm64"),
    canonicalBinding(null),
    canonicalBinding([]),
    canonicalBinding(Object.assign({}, { [Symbol("bad")]: true })),
    canonicalBinding(Object.create({})),
    canonicalBinding({ "": { read() {} } }),
    canonicalBinding({ [moduleName]: null }),
    canonicalBinding({ [moduleName]: [] }),
    canonicalBinding({ [moduleName]: Object.create({}) }),
    canonicalBinding({ [moduleName]: { "": () => 1 } }),
    canonicalBinding({ [moduleName]: { read: 1 } }),
    canonicalBinding(Object.defineProperty({}, moduleName, {
      get() { return { read() {} }; },
      enumerable: true,
    })),
  ]) {
    assert.throws(() => compose({
      canonicalBinding: binding,
      inventory: importInventory(plan),
    }), /canonical/);
  }
});

test("requires every canonical member to be one exact final function import", () => {
  const moduleName = "cm32p2|sample:api/value@1";
  const binding = canonicalBinding({ [moduleName]: { read() {} } });
  assert.throws(() => compose({ canonicalBinding: binding }), /absent/);
  assert.throws(() => compose({
    canonicalBinding: binding,
    inventory: importInventory(commandPlan([
      { module: moduleName, name: "read", kind: "memory" },
    ])),
  }), /function import/);
  assert.throws(() => compose({
    canonicalBinding: binding,
    inventory: importInventory(commandPlan([functionImport(moduleName, "read")])),
    physicalProviders: { [moduleName]: { read() {} } },
  }), /cannot be replaced/);
  const getter = Object.defineProperty({}, moduleName, {
    get() { return {}; },
    enumerable: true,
  });
  assert.throws(() => compose({
    canonicalBinding: binding,
    inventory: importInventory(commandPlan([functionImport(moduleName, "read")])),
    physicalProviders: getter,
  }), /data property/);
});

test("rejects malformed physical providers and unavailable physical members", () => {
  const plan = commandPlan([functionImport("platform", "value")]);
  for (const physicalProviders of [null, [], Object.create({}), { [Symbol("bad")]: true }]) {
    assert.throws(() => compose({
      physicalProviders,
      inventory: importInventory(plan),
    }), /providers/);
  }
  for (const physicalProviders of [
    {},
    { platform: null },
    { platform: 1 },
    { platform: {} },
    { platform: { value: undefined } },
    { platform: { value: 1 } },
    Object.defineProperty({}, "platform", { get() { return {}; }, enumerable: true }),
    { platform: Object.defineProperty({}, "value", { get() { return () => {}; }, enumerable: true }) },
    { platform: Object.defineProperty({}, "value", { value() {}, enumerable: false }) },
  ]) {
    assert.throws(() => compose({
      physicalProviders,
      inventory: importInventory(plan),
    }), /provider|import|function/);
  }
});

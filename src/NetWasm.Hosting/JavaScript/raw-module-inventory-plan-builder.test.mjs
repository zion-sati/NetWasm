import assert from "node:assert/strict";
import test from "node:test";
import { buildRawModuleInventoryPlan } from "./raw-module-inventory-plan-builder.mjs";

const importDescriptor = (module = "wasi:cli/stdout@0.2.11", name = "get-stdout",
  kind = "function") => ({ module, name, kind });
const exportDescriptor = (name = "memory", kind = "memory") => ({ name, kind });
const abi = (target = "wasm32", overrides = {}) => ({
  target,
  entryPoint: null,
  imports: [importDescriptor()],
  exports: [exportDescriptor()],
  ...overrides,
});

test("snapshots immutable wasm32 and wasm64 module inventories", () => {
  for (const [target, prefix, zero] of [
    ["wasm32", "cm32p2", 0],
    ["wasm64", "cm64p2", 0n],
  ]) {
    const source = abi(target, {
      imports: [
        importDescriptor(),
        importDescriptor("host", "state", "global"),
        importDescriptor("host", "memory", "memory"),
        importDescriptor("host", "dispatch", "table"),
        importDescriptor("host", "fault", "tag"),
      ],
      exports: [
        exportDescriptor("run", "function"),
        exportDescriptor("state", "global"),
        exportDescriptor(),
        exportDescriptor("dispatch", "table"),
        exportDescriptor("fault", "tag"),
      ],
    });
    const plan = buildRawModuleInventoryPlan({ abi: source });

    assert.deepEqual(plan, {
      target,
      prefix,
      zero,
      imports: source.imports,
      exports: source.exports,
      reactorHostModule: `${prefix}|netwasm:runtime/reactor-host@1`,
      reactorGuestExport: `${prefix}|netwasm:runtime/reactor-guest@1|wake`,
    });
    assert.equal(Object.isFrozen(plan), true);
    assert.equal(Object.isFrozen(plan.imports), true);
    assert.equal(Object.isFrozen(plan.exports), true);
    assert.equal(plan.imports.every(Object.isFrozen), true);
    assert.equal(plan.exports.every(Object.isFrozen), true);

    source.imports[0].name = "changed";
    source.exports[0].name = "changed";
    source.imports.push(importDescriptor("later", "value"));
    assert.equal(plan.imports[0].name, "get-stdout");
    assert.equal(plan.exports[0].name, "run");
    assert.equal(plan.imports.length, 5);
  }
});

test("accepts the same import name from distinct modules and separator characters", () => {
  const plan = buildRawModuleInventoryPlan({ abi: abi("wasm32", {
    imports: [
      importDescriptor("first", "value"),
      importDescriptor("second", "value"),
      importDescriptor("host\u0000member", "tail"),
      importDescriptor("host", "member\u0000tail"),
    ],
    exports: [],
  }) });

  assert.deepEqual(plan.imports, [
    importDescriptor("first", "value"),
    importDescriptor("second", "value"),
    importDescriptor("host\u0000member", "tail"),
    importDescriptor("host", "member\u0000tail"),
  ]);
  assert.deepEqual(plan.exports, []);
});

test("rejects malformed ABI and target shapes", () => {
  for (const value of [
    null,
    [],
    {},
    { ...abi(), extra: true },
    { ...abi(), [Symbol("invalid")]: true },
    Object.create(abi()),
  ]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: value }), /raw ABI/);
  }
  const accessor = { ...abi() };
  Object.defineProperty(accessor, "target", { enumerable: true, get: () => "wasm32" });
  assert.throws(() => buildRawModuleInventoryPlan({ abi: accessor }), /raw ABI/);
  for (const target of [null, "", "wasm128", "toString", 32]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: abi(target) }), /target/);
  }
});

test("rejects malformed and duplicate import descriptors", () => {
  for (const imports of [
    null,
    [null],
    [["host", "value", "function"]],
    [{ ...importDescriptor(), extra: true }],
    [{ ...importDescriptor(), [Symbol("invalid")]: true }],
    [importDescriptor(null)],
    [importDescriptor("")],
    [importDescriptor("host", null)],
    [importDescriptor("host", "")],
    [importDescriptor("host", "value", "record")],
    [importDescriptor(), importDescriptor()],
  ]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: abi("wasm32", { imports }) }),
      /raw import/);
  }
  const accessor = importDescriptor();
  Object.defineProperty(accessor, "name", { enumerable: true, get: () => "get-stdout" });
  assert.throws(() => buildRawModuleInventoryPlan({ abi: abi("wasm32", { imports: [accessor] }) }),
    /raw import/);
});

test("rejects malformed and duplicate export descriptors", () => {
  for (const exports of [
    null,
    [null],
    [["memory", "memory"]],
    [{ ...exportDescriptor(), extra: true }],
    [{ ...exportDescriptor(), [Symbol("invalid")]: true }],
    [exportDescriptor(null)],
    [exportDescriptor("")],
    [exportDescriptor("value", "record")],
    [exportDescriptor(), exportDescriptor()],
  ]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: abi("wasm32", { exports }) }),
      /raw export/);
  }
  const accessor = exportDescriptor();
  Object.defineProperty(accessor, "kind", { enumerable: true, get: () => "memory" });
  assert.throws(() => buildRawModuleInventoryPlan({ abi: abi("wasm32", { exports: [accessor] }) }),
    /raw export/);
});

test("rejects Preview 1 and wrong-target canonical imports", () => {
  for (const module of [
    "wasi_snapshot_preview1",
    "wasi_unstable",
    "cm32p2|wasi_snapshot_preview1",
    "cm64p2|wasi_unstable",
    "custom|wasi_snapshot_preview1",
    "custom|wasi_unstable",
  ]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: abi("wasm32", {
      imports: [importDescriptor(module, "value")],
    }) }), /Preview 1/);
  }
  for (const [target, module] of [
    ["wasm32", "cm64p2|wasi:cli/stdout@0.2.11"],
    ["wasm64", "cm32p2|wasi:cli/stdout@0.2.11"],
  ]) {
    assert.throws(() => buildRawModuleInventoryPlan({ abi: abi(target, {
      imports: [importDescriptor(module)],
    }) }), /does not match/);
  }
});

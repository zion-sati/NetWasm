import assert from "node:assert/strict";
import test from "node:test";
import { createDeclaredOracleImports } from "./declared-oracle-imports.mjs";

test("preserves explicitly declared functions and their results", () => {
  const operation = value => value + 1;
  const imports = createDeclaredOracleImports("test", { operation });
  assert.equal(imports.operation, operation);
  assert.equal(imports.operation(41), 42);
});

test("rejects an undeclared import instead of supplying a no-op", () => {
  const imports = createDeclaredOracleImports("test", {});
  assert.throws(() => imports.missing, /Undeclared oracle import: test.missing/);
});

test("does not inherit imports from the object prototype", () => {
  const imports = createDeclaredOracleImports("test", Object.create({ inherited: () => 0 }));
  assert.throws(() => imports.inherited, /Undeclared oracle import/);
  assert.throws(() => imports.toString, /Undeclared oracle import/);
});

test("retains replacement memory bindings when the test runtime resets", () => {
  const first = new WebAssembly.Memory({ initial: 1 });
  const second = new WebAssembly.Memory({ initial: 1 });
  const values = { memory: first };
  const imports = createDeclaredOracleImports("test", values);
  assert.equal(imports.memory, first);
  values.memory = second;
  assert.equal(imports.memory, second);
});

test("permits explicitly declared zero-returning simulations", () => {
  const imports = createDeclaredOracleImports("test", { simulated: () => 0 });
  assert.equal(imports.simulated(), 0);
});

test("rejects absent module names and declaration objects", () => {
  for (const name of [undefined, null, 1, ""]) {
    assert.throws(() => createDeclaredOracleImports(name, {}), TypeError);
  }
  for (const values of [undefined, null, 1, "", () => 0]) {
    assert.throws(() => createDeclaredOracleImports("test", values), TypeError);
  }
});

import assert from "node:assert/strict";
import test from "node:test";
import { inspectRawModuleImports } from "./raw-module-import-inspector.mjs";

const entry = (module, name) => ({ module, name, kind: "function" });
const signature = (module, name) => ({ module, name, parameters: ["i64"], results: ["i32"] });

test("inspection preserves core inventory order and exact identities with owned signature arrays", async () => {
  const inventory = [entry("a:b", "c"), entry("a", "b:c"), entry("", ""), entry("A", "b:c")];
  const signatures = inventory.map(value => signature(value.module, value.name)).reverse();
  const calls = [];
  const bytes = new Uint8Array([1, 2]);
  const result = await inspectRawModuleImports(bytes, {
    async readCoreImports(input) {
      calls.push("core");
      assert.notEqual(input, bytes);
      assert.deepEqual(input, bytes);
      return inventory;
    },
    readFunctionImports(input) {
      calls.push("functions");
      assert.notEqual(input, bytes);
      assert.deepEqual(input, bytes);
      return signatures;
    },
  });
  assert.deepEqual(calls, ["core", "functions"]);
  assert.deepEqual(result, inventory.map(value => signature(value.module, value.name)));
  assert.ok(Object.isFrozen(result));
  assert.ok(result.every(value => Object.isFrozen(value)
    && Object.isFrozen(value.parameters) && Object.isFrozen(value.results)));
  signatures[0].parameters[0] = "f32";
  signatures[0].results[0] = "f64";
  inventory[0].name = "changed";
  assert.equal(result[0].name, "c");
  assert.deepEqual(result.at(-1).parameters, ["i64"]);
  assert.deepEqual(result.at(-1).results, ["i32"]);
});

test("inspection snapshots bytes before async work and isolates both readers", async () => {
  const bytes = new Uint8Array([1, 2]);
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const result = inspectRawModuleImports(bytes, {
    async readCoreImports(input) {
      assert.deepEqual(input, new Uint8Array([1, 2]));
      input.fill(8);
      await pending;
      return [];
    },
    readFunctionImports(input) {
      assert.deepEqual(input, new Uint8Array([1, 2]));
      input.fill(7);
      return [];
    },
  });
  bytes.fill(9);
  release();
  assert.deepEqual(await result, []);
  assert.deepEqual(bytes, new Uint8Array([9, 9]));
});

test("inspection rejects invalid inputs and incomplete reader contracts", async () => {
  await assert.rejects(inspectRawModuleImports(null, null), { code: "invalid-bytes" });
  for (const readers of [null, {}, { readCoreImports: 1 },
    { readCoreImports() {} }, { readCoreImports() {}, readFunctionImports: 1 }]) {
    await assert.rejects(inspectRawModuleImports(new Uint8Array(), readers), TypeError);
  }
});

for (const kind of ["memory", "table", "global", "tag", "unknown"]) {
  test(`inspection rejects ${kind} imports before decoding functions`, async () => {
    await assert.rejects(inspectRawModuleImports(new Uint8Array(), {
      readCoreImports: () => [{ ...entry("host", "member"), kind }],
      readFunctionImports() { assert.fail("Cannot decode rejected import inventory."); },
    }), { code: "unsupported-import-kind" });
  });
}

test("inspection rejects duplicate core identities before decoding functions", async () => {
  await assert.rejects(inspectRawModuleImports(new Uint8Array(), {
    readCoreImports: () => [entry("host", "member"), entry("host", "member")],
    readFunctionImports() { assert.fail("Cannot decode duplicate import inventory."); },
  }), { code: "duplicate-import" });
});

for (const [name, signatures] of [
  ["missing", []],
  ["different", [signature("host", "Member")]],
  ["extra", [signature("host", "member"), signature("host", "extra")]],
  ["duplicate", [signature("host", "member"), signature("host", "member")]],
]) {
  test(`inspection rejects ${name} decoder identities without returning a partial plan`, async () => {
    await assert.rejects(inspectRawModuleImports(new Uint8Array(), {
      readCoreImports: () => [entry("host", "member")],
      readFunctionImports: () => signatures,
    }), { code: "inconsistent-import-inventory" });
  });
}

test("inspection preserves core reader failure and never invokes function reader", async () => {
  const cause = new Error("core rejected");
  await assert.rejects(inspectRawModuleImports(new Uint8Array(), {
    readCoreImports() { throw cause; },
    readFunctionImports() { assert.fail("Cannot decode a failed core module."); },
  }), error => error === cause);
});

test("inspection preserves function reader failure without returning a partial plan", async () => {
  const cause = new Error("decoder rejected");
  await assert.rejects(inspectRawModuleImports(new Uint8Array(), {
    readCoreImports: () => [],
    readFunctionImports() { throw cause; },
  }), error => error === cause);
});

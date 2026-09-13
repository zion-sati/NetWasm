import assert from "node:assert/strict";
import test from "node:test";
import { readCoreModuleImports } from "./core-module-import-reader.mjs";
import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

test("engine adapter compiles owned bytes and preserves every import kind without instantiation", async () => {
  const bytes = new Uint8Array([1, 2]);
  const module = {};
  const entries = ["function", "memory", "table", "global", "tag"]
    .map(kind => ({ module: "host", name: kind, kind }));
  const calls = [];
  const result = await readCoreModuleImports(bytes, {
    async compile(input) {
      calls.push("compile");
      assert.notEqual(input, bytes);
      assert.deepEqual(input, bytes);
      input.fill(9);
      return module;
    },
    Module: { imports(input) {
      calls.push("imports");
      assert.equal(input, module);
      return entries;
    } },
    instantiate() { assert.fail("Inspection cannot instantiate."); },
  });
  assert.deepEqual(calls, ["compile", "imports"]);
  assert.deepEqual(bytes, new Uint8Array([1, 2]));
  assert.deepEqual(result, entries);
  assert.ok(Object.isFrozen(result));
  entries[0].name = "changed";
  assert.equal(result[0].name, "function");
  assert.ok(result.every(Object.isFrozen));
});

test("engine adapter accepts an empty import inventory", async () => {
  assert.deepEqual(await readCoreModuleImports(new Uint8Array(), {
    compile: async () => ({}), Module: { imports: () => [] },
  }), []);
});

test("engine adapter rejects invalid bytes and incomplete engine before compilation", async () => {
  await assert.rejects(readCoreModuleImports(null, null), { code: "invalid-bytes" });
  for (const engine of [null, {}, { compile: 1 }, { compile() {} },
    { compile() {}, Module: {} }, { compile() {}, Module: { imports: 1 } }]) {
    await assert.rejects(readCoreModuleImports(new Uint8Array(), engine), TypeError);
  }
});

test("engine adapter preserves compile failure and never reads the inventory", async () => {
  const cause = new Error("compile rejected");
  await assert.rejects(readCoreModuleImports(new Uint8Array(), {
    compile() { throw cause; },
    Module: { imports() { assert.fail("Cannot inspect a failed compilation."); } },
  }), error => {
    assert.ok(error instanceof RawModuleInspectionError);
    assert.equal(error.name, "RawModuleInspectionError");
    assert.equal(error.code, "invalid-core-module");
    assert.equal(error.cause, cause);
    return true;
  });
});

test("engine adapter preserves inventory failure without a partial result", async () => {
  const cause = new Error("inventory rejected");
  await assert.rejects(readCoreModuleImports(new Uint8Array(), {
    compile: async () => ({}), Module: { imports() { throw cause; } },
  }), error => error.code === "invalid-core-module" && error.cause === cause);
});

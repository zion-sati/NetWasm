import assert from "node:assert/strict";
import test from "node:test";
import { resolveInteropMemory } from "./interop-memory-resolver.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

for (const [target, name] of [["wasm32", "cm32p2_memory"], ["wasm64", "cm64p2_memory"]]) {
  test(`${target} resolves canonical, legacy and runtime memory without adding aliases`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    for (const [exports, runtimeModules] of [
      [{ [name]: memory }, {}],
      [{ memory }, {}],
      [{}, { "netwasm.runtime.v1": { memory } }],
      [{ [name]: memory, memory }, { "netwasm.runtime.v1": { memory } }],
      [{ [name]: null, memory }, { "netwasm.runtime.v1": {} }],
    ]) {
      const keys = Object.keys(exports);
      assert.equal(resolveInteropMemory({ instance: { exports }, runtimeModules, target }), memory);
      assert.deepEqual(Object.keys(exports), keys);
    }
  });

  test(`${target} rejects missing, invalid and conflicting memory before binding`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const other = new WebAssembly.Memory({ initial: 1 });
    for (const [exports, runtimeModules, message] of [
      [{}, {}, /did not expose/],
      [{ [name]: {} }, {}, /alias is invalid/],
      [{ memory: 1 }, {}, /alias is invalid/],
      [{}, { "netwasm.runtime.v1": { memory: {} } }, /alias is invalid/],
      [{ [name]: memory, memory: other }, {}, /aliases disagree/],
      [{ [name]: memory }, { "netwasm.runtime.v1": { memory: other } }, /aliases disagree/],
    ]) {
      assert.throws(() => resolveInteropMemory({ instance: { exports }, runtimeModules, target }),
        error => error instanceof NetWasmHostError && message.test(error.message));
    }
  });
}

test("rejects unsupported target before reading instance state", () => {
  assert.throws(() => resolveInteropMemory({ instance: null, runtimeModules: null, target: "wasm128" }),
    error => error instanceof NetWasmHostError && /target is unsupported/.test(error.message));
});

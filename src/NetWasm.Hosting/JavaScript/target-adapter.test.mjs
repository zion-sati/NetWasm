import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { createTargetAdapter } from "./target-adapter.mjs";

test("createTargetAdapter validates the target and optional memory", () => {
  for (const target of [null, "", "wasm128"]) {
    assert.throws(
      () => createTargetAdapter(target),
      error => error instanceof NetWasmHostError && /wasm32 or wasm64/i.test(error.message));
  }
  assert.throws(
    () => createTargetAdapter("wasm32", {}),
    error => error instanceof NetWasmHostError && /WebAssembly.Memory/i.test(error.message));

  const withoutMemory = createTargetAdapter("wasm32");
  assert.equal(withoutMemory.memory, undefined);
  assert.equal(Object.isFrozen(withoutMemory), true);
});

test("wasm32 target adapter accepts only unsigned 32-bit number addresses", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const adapter = createTargetAdapter("wasm32", memory);

  assert.equal(adapter.target, "wasm32");
  assert.equal(adapter.memory, memory);
  assert.equal(adapter.toAddress(0), 0);
  assert.equal(adapter.toAddress(0xffffffff), 0xffffffff);

  for (const value of [NaN, 1.5, -1, 0x100000000, 1n]) {
    assert.throws(
      () => adapter.toAddress(value),
      error => error instanceof NetWasmHostError && /wasm32 address/i.test(error.message));
  }
});

test("wasm64 target adapter accepts only unsigned 64-bit BigInt addresses", () => {
  const adapter = createTargetAdapter("wasm64");

  assert.equal(adapter.target, "wasm64");
  assert.equal(adapter.toAddress(0n), 0n);
  assert.equal(adapter.toAddress(0xffffffffffffffffn), 0xffffffffffffffffn);

  for (const value of [0, -1n, 0x10000000000000000n]) {
    assert.throws(
      () => adapter.toAddress(value),
      error => error instanceof NetWasmHostError && /wasm64 address/i.test(error.message));
  }
});

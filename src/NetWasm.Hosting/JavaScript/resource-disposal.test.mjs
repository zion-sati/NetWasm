import assert from "node:assert/strict";
import test from "node:test";
import { resourceDisposeSymbol, selectResourceDisposeSymbol } from "./resource-disposal.mjs";

test("uses the standard resource disposal symbol when available", () => {
  const standard = Symbol("standard");
  assert.equal(selectResourceDisposeSymbol({
    dispose: standard,
    for() { assert.fail("the legacy registry must not be consulted"); },
  }), standard);
});

test("uses the jco registered symbol when standard disposal is unavailable", () => {
  const expected = Symbol("registered");
  const keys = [];
  assert.equal(selectResourceDisposeSymbol({
    for(key) { keys.push(key); return expected; },
  }), expected);
  assert.deepEqual(keys, ["dispose"]);
});

test("exports the symbol selected from the actual host", () => {
  assert.equal(selectResourceDisposeSymbol(), Symbol.dispose);
  assert.equal(resourceDisposeSymbol, Symbol.dispose);
});

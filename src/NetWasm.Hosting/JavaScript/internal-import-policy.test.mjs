import assert from "node:assert/strict";
import test from "node:test";

import { isInternalImport } from "./internal-import-policy.mjs";

test("recognizes only exact runtime-owned reactor modules", () => {
  for (const module of [
    "netwasm:runtime/reactor-host",
    "netwasm:runtime/reactor-host@1.0.0",
  ]) {
    assert.equal(isInternalImport({ module }), true);
  }
  for (const module of [
    "netwasm:runtime/reactor-host@1.0.1",
    "netwasm:runtime/reactor-host@1",
    "netwasm:runtime/reactor-hosted@1.0.0",
    "example:runtime/reactor-host@1.0.0",
  ]) {
    assert.equal(isInternalImport({ module }), false);
  }
});

test("rejects missing import module identities", () => {
  for (const module of [null, 1, ""]) {
    assert.throws(() => isInternalImport({ module }), /module identity is required/i);
  }
});

test("rejects invalid policy request shapes without evaluating accessors", () => {
  const withSymbol = {
    module: "netwasm:runtime/reactor-host@1.0.0",
    [Symbol("hidden")]: true,
  };
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => isInternalImport(value), /request is invalid/i);
  }

  const inherited = Object.assign(
    Object.create({ inherited: true }),
    { module: "netwasm:runtime/reactor-host@1.0.0" });
  const nonEnumerable = {};
  Object.defineProperty(nonEnumerable, "module", {
    value: "netwasm:runtime/reactor-host@1.0.0",
    enumerable: false,
  });
  const accessor = {};
  Object.defineProperty(accessor, "module", {
    get() { throw new Error("must not evaluate"); },
    enumerable: true,
  });
  for (const value of [
    {},
    inherited,
    { module: "netwasm:runtime/reactor-host@1.0.0", extra: true },
    { other: "netwasm:runtime/reactor-host@1.0.0" },
    nonEnumerable,
    accessor,
  ]) {
    assert.throws(() => isInternalImport(value), /request shape/i);
  }
});

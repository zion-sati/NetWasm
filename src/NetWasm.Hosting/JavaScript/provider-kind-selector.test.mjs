import assert from "node:assert/strict";
import test from "node:test";

import { selectProviderKind } from "./provider-kind-selector.mjs";

test("selects exact reserved platform ownership without probing interfaces", () => {
  for (const module of [
    "wasi:cli/environment@0.2.11",
    "netwasm:platform/process@1.0.0",
  ]) {
    assert.equal(selectProviderKind({ module }), "platform");
  }
  for (const module of [
    "example:logging/logger@1.0.0",
    "netwasm:platformish/process@1.0.0",
    "netwasm:platform@1.0.0/process",
  ]) {
    assert.equal(selectProviderKind({ module }), "application");
  }
});

test("rejects missing provider module identities", () => {
  for (const module of [null, 1, ""]) {
    assert.throws(() => selectProviderKind({ module }), /module identity is required/i);
  }
});

test("rejects invalid selection request shapes without evaluating accessors", () => {
  const withSymbol = { module: "wasi:cli/environment@0.2.11", [Symbol("hidden")]: true };
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => selectProviderKind(value), /request is invalid/i);
  }

  const inherited = Object.assign(
    Object.create({ inherited: true }),
    { module: "wasi:cli/environment@0.2.11" });
  const nonEnumerable = {};
  Object.defineProperty(nonEnumerable, "module", {
    value: "wasi:cli/environment@0.2.11",
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
    { module: "wasi:cli/environment@0.2.11", extra: true },
    { other: "wasi:cli/environment@0.2.11" },
    nonEnumerable,
    accessor,
  ]) {
    assert.throws(() => selectProviderKind(value), /request shape/i);
  }
});

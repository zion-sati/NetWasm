import assert from "node:assert/strict";
import test from "node:test";

import { projectProviderModuleIdentity } from "./provider-module-identity.mjs";

test("projects exact deployment modules into component and raw provider identities", () => {
  for (const [module, componentModule] of [
    [
      "wasi:cli/environment@0.2.11",
      "wasi:cli/environment",
    ],
    [
      "example:logging/logger@1.20.300",
      "example:logging/logger",
    ],
    [
      "netwasm:platform/process-control@10.0.1",
      "netwasm:platform/process-control",
    ],
  ]) {
    const result = projectProviderModuleIdentity({ module });
    assert.deepEqual(result, { componentModule, rawModule: module });
    assert.equal(Object.isFrozen(result), true);
  }
});

test("rejects malformed and noncanonical versioned WIT interface identities", () => {
  for (const module of [
    null,
    "",
    "wasi:cli@0.2.11/environment",
    "wasi:cli/environment",
    "wasi:cli/environment@0.2",
    "wasi:cli/environment@00.2.11",
    "wasi:cli/environment@0.02.11",
    "wasi:cli/environment@0.2.011",
    "Wasi:cli/environment@0.2.11",
    "wasi:CLI/environment@0.2.11",
    "wasi:cli/Environment@0.2.11",
    "wasi_cli/environment@0.2.11",
    "wasi:cli/environment_name@0.2.11",
    "wasi:cli/first/second@0.2.11",
    "wasi:/environment@0.2.11",
    ":cli/environment@0.2.11",
  ]) {
    assert.throws(
      () => projectProviderModuleIdentity({ module }),
      /canonical versioned WIT interface syntax/i);
  }
});

test("rejects invalid projection request shapes without evaluating accessors", () => {
  const withSymbol = { module: "wasi:cli/environment@0.2.11", [Symbol("hidden")]: true };
  for (const value of [null, 1, [], withSymbol]) {
    assert.throws(() => projectProviderModuleIdentity(value), /request is invalid/i);
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
    assert.throws(() => projectProviderModuleIdentity(value), /request shape/i);
  }
});

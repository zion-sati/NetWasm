import assert from "node:assert/strict";
import test from "node:test";
import { platformProviderCatalog as catalog } from "./platform-provider-catalog.generated.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";

test("binds the exact generated Preview 2 provider catalog", () => {
  assert.equal(catalog.schema, 1);
  assert.equal(catalog.world, "netwasm:hosting-platform/preview2@1.0.0");
  assert.equal(catalog.witSha256,
    "b5c451ab66d662b71c242cd83caeb586cfe5f29641c3d554ec62cf2b5358ef95");
  assert.deepEqual(catalog.shim, {
    package: "@bytecodealliance/preview2-shim",
    version: "0.24.1",
  });
  assert.equal(catalog.providers.length, 27);
  assert.equal(catalog.providers.reduce(
    (count, provider) => count + provider.functions.length, 0), 175);
  assert.deepEqual(catalog.providers.map(provider => provider.module),
    [...catalog.providers.map(provider => provider.module)].sort());
  assert.deepEqual([...new Set(catalog.providers.map(provider => provider.capability))].sort(), [
    "baseline",
    "environment",
    "monotonicClock",
    "network",
    "preopenedDirectories",
    "randomness",
    "wallClock",
  ]);
});

test("is deeply immutable and satisfies the runtime provider contract", () => {
  const validate = createProviderMetadataValidator({ selectProviderKind });
  assert.equal(Object.isFrozen(catalog), true);
  assert.equal(Object.isFrozen(catalog.shim), true);
  assert.equal(Object.isFrozen(catalog.providers), true);
  for (const provider of catalog.providers) {
    assert.equal(Object.isFrozen(provider), true);
    assert.equal(Object.isFrozen(provider.functions), true);
    assert.deepEqual(validate({ kind: "platform", provider }), provider);
    for (const function_ of provider.functions) {
      assert.equal(Object.isFrozen(function_), true);
      assert.equal(Object.isFrozen(function_.parameters), true);
      assert.equal(Object.isFrozen(function_.results), true);
    }
  }
  assert.throws(() => {
    catalog.providers[0].module = "drift";
  }, TypeError);
});

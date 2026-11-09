import assert from "node:assert/strict";
import test from "node:test";
import { platformProviderCatalog as catalog } from "./platform-provider-catalog.generated.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";

test("binds the exact generated WASI 0.3 provider catalog", () => {
  assert.equal(catalog.schema, 2);
  assert.equal(catalog.world, "netwasm:hosting-platform/wasi@1.0.0");
  assert.equal(catalog.witSha256,
    "4f122e65638086e633ab12a4b1248c3263ff26385bedf1ff1ef1d23f76a34f16");
  assert.equal(Object.hasOwn(catalog, "shim"), false);
  assert.equal(catalog.providers.length, 23);
  assert.equal(catalog.providers.reduce(
    (count, provider) => count + provider.functions.length, 0), 125);
  // Types-only interfaces are providers too: JavaScript bindings construct their resources.
  assert.deepEqual(catalog.providers.filter(provider => provider.functions.length === 0)
    .map(provider => provider.module), [
    "wasi:cli/terminal-input@0.3.2",
    "wasi:cli/terminal-output@0.3.2",
    "wasi:cli/types@0.3.2",
    "wasi:clocks/types@0.3.2",
  ]);
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

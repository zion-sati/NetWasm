import assert from "node:assert/strict";
import test from "node:test";

import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createPlatformProviderLoader } from "./platform-provider-loader.mjs";
import {
  createPreview2PlatformProviderRegistry,
} from "./preview2-platform-provider-registry.mjs";

test("composes the generated catalog into the existing validated registry", () => {
  const registry = createPreview2PlatformProviderRegistry({
    createShim() {},
    validateProviderMetadata: createProviderMetadataValidator({ selectProviderKind }),
  });

  const resolved = registry.resolve({
    module: "wasi:cli/environment@0.2.11",
  });
  assert.equal(Object.isFrozen(resolved), true);
  assert.equal(resolved.provider.capability, "environment");
  assert.equal(typeof resolved.createSource, "function");
});

test("shares one shim through the existing selected-provider loader", async () => {
  let shimCreations = 0;
  class OutputStream {}
  class Pollable {}
  const sources = {
    "wasi:cli/environment@0.2.11": Object.freeze({ environment: true }),
    "wasi:cli/exit@0.2.11": Object.freeze({ exit: true }),
    "wasi:io/poll@0.2.11": Object.freeze({ Pollable }),
    "wasi:io/streams@0.2.11": Object.freeze({ OutputStream }),
  };
  const registry = createPreview2PlatformProviderRegistry({
    createShim() {
      shimCreations++;
      return { getImportObject: () => sources };
    },
    validateProviderMetadata: createProviderMetadataValidator({ selectProviderKind }),
  });
  const modules = Object.keys(sources);
  const request = Object.freeze({
    applicationImports: Object.freeze([]),
    arguments: Object.freeze([]),
    buildFingerprint: "a".repeat(64),
    deploymentManifestSha256: "b".repeat(64),
    environment: Object.freeze([]),
    grants: Object.freeze({
      clocks: Object.freeze([]),
      environment: Object.freeze([]),
      network: "denyAll",
      preopens: Object.freeze([]),
      randomness: false,
    }),
    schemaVersion: 1,
  });
  const binding = Object.freeze({
    applicationProviders: Object.freeze([]),
    manifest: Object.freeze({}),
    platformProviders: Object.freeze(modules.map(
      module => registry.resolve({ module }).provider)),
    request,
  });
  const load = createPlatformProviderLoader({
    resolvePlatformProvider: lookup => registry.resolve(lookup),
  });

  const loaded = await load({
    binding,
    filesystem: null,
    signal: null,
    stderr: Object.freeze({ write() {} }),
    stdout: Object.freeze({ write() {} }),
  });

  assert.equal(shimCreations, 1);
  assert.deepEqual(loaded.map(source => source.module), modules);
  assert.strictEqual(loaded[0].value, sources[modules[0]]);
  assert.strictEqual(loaded[1].value, sources[modules[1]]);
});

test("validates exact composition dependencies", () => {
  const valid = {
    createShim() {},
    validateProviderMetadata() {},
  };
  for (const value of invalidObjects(valid)) {
    assert.throws(() => createPreview2PlatformProviderRegistry(value.value),
      value.shape ? /options shape/u : /options is invalid/u);
  }
  assert.throws(() => createPreview2PlatformProviderRegistry({
    ...valid,
    createShim: null,
  }), /are required/u);
  assert.throws(() => createPreview2PlatformProviderRegistry({
    ...valid,
    validateProviderMetadata: null,
  }), /are required/u);
});

function invalidObjects(valid) {
  const values = [
    { value: null, shape: false },
    { value: [], shape: false },
    { value: () => {}, shape: false },
    { value: { ...valid, extra: true }, shape: true },
  ];
  for (const key of Object.keys(valid)) {
    const missing = { ...valid };
    delete missing[key];
    values.push({ value: missing, shape: true });
  }
  return values;
}

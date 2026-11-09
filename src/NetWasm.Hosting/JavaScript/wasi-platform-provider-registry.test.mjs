import assert from "node:assert/strict";
import test from "node:test";

import { platformProviderCatalog } from "./platform-provider-catalog.generated.mjs";
import { selectProviderKind } from "./provider-kind-selector.mjs";
import { createProviderMetadataValidator } from "./provider-metadata-validator.mjs";
import { createWasiPlatformProviderRegistry } from "./wasi-platform-provider-registry.mjs";

const validateProviderMetadata = createProviderMetadataValidator({ selectProviderKind });
const environment = "wasi:cli/environment@0.3.2";
const stdout = "wasi:cli/stdout@0.3.2";

function registry(createImports) {
  return createWasiPlatformProviderRegistry({
    catalog: platformProviderCatalog,
    createImports,
    validateProviderMetadata,
  });
}

test("creates one import set per execution request and releases it once", async () => {
  const created = [];
  const releases = [];
  const modules = Object.freeze({
    [environment]: Object.freeze({ getArguments() {} }),
    [stdout]: Object.freeze({ writeViaStream() {} }),
  });
  const subject = registry(request => {
    created.push(request);
    return Object.freeze({ imports: modules, release: async () => { releases.push(request); } });
  });
  assert.equal(Object.isFrozen(subject), true);
  const first = Object.freeze({ id: 1 });
  const second = Object.freeze({ id: 2 });
  const resolved = subject.resolve({ module: environment });
  assert.equal(resolved.provider.module, environment);
  assert.equal(resolved.createSource(first), modules[environment]);
  assert.equal(subject.resolve({ module: stdout }).createSource(first), modules[stdout]);
  assert.equal(resolved.createSource(second), modules[environment]);
  assert.deepEqual(created, [first, second]);

  await subject.release(first);
  await subject.release(first);
  await subject.release(Object.freeze({ id: 3 }));
  assert.deepEqual(releases, [first]);
  resolved.createSource(first);
  assert.deepEqual(created, [first, second, first]);
});

test("reports a catalog module the platform does not supply", () => {
  const subject = registry(() => Object.freeze({ imports: Object.freeze({}), release: async () => {} }));
  assert.throws(() => subject.resolve({ module: "wasi:http/client@0.3.2" }).createSource({}),
    /does not provide module 'wasi:http\/client@0.3.2'/);
  const invalid = registry(() => Object.freeze({
    imports: Object.freeze({ [environment]: [] }), release: async () => {},
  }));
  assert.throws(() => invalid.resolve({ module: environment }).createSource({}), /does not provide/);
});

test("rejects malformed import sets", () => {
  for (const importSet of [null, {}, { imports: {}, release() {} }, Object.freeze({ imports: null, release() {} }),
    Object.freeze({ imports: {}, release: null }), Object.freeze({ imports: {}, release() {}, extra: 1 })]) {
    assert.throws(() => registry(() => importSet).resolve({ module: environment }).createSource({}), TypeError);
  }
});

test("rejects malformed options and incompatible catalogs", () => {
  const valid = { catalog: platformProviderCatalog, createImports() {}, validateProviderMetadata };
  for (const options of [null, {}, { ...valid, extra: 1 }, { ...valid, createImports: null },
    { ...valid, validateProviderMetadata: null }]) {
    assert.throws(() => createWasiPlatformProviderRegistry(options), TypeError);
  }
  const provider = platformProviderCatalog.providers[0];
  const catalog = (overrides, providers = [provider]) => Object.freeze({
    providers: Object.freeze(providers),
    schema: 2,
    witSha256: "a".repeat(64),
    world: "netwasm:hosting-platform/wasi@1.0.0",
    ...overrides,
  });
  for (const value of [
    null,
    { ...catalog({}) },
    catalog({ schema: 1 }),
    catalog({ world: "netwasm:hosting-platform/preview2@1.0.0" }),
    catalog({ witSha256: "bad" }),
    catalog({}, []),
    catalog({ providers: [provider] }),
    catalog({}, [{ ...provider }]),
    catalog({}, [Object.freeze({ ...provider, module: "" })]),
    catalog({}, [Object.freeze({ ...provider, functions: [] })]),
    catalog({}, [Object.freeze({ capability: "baseline", module: "m" })]),
  ]) {
    assert.throws(() => createWasiPlatformProviderRegistry({ ...valid, catalog: value }), TypeError);
  }
  // A types-only interface's provider has no functions.
  const typesOnly = Object.freeze({ ...provider, functions: Object.freeze([]) });
  assert.equal(typeof createWasiPlatformProviderRegistry({ ...valid, catalog: catalog({}, [typesOnly]) })
    .resolve({ module: provider.module }).createSource, "function");
});

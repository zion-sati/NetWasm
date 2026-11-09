import { createPlatformProviderRegistry } from "./platform-provider-registry.mjs";

// Registers each provider of NetWasm's WASI 0.3 platform catalog. One execution request
// gets one import set, created on its first provider and released after the execution.

const factoryKeys = ["catalog", "createImports", "validateProviderMetadata"];
const catalogKeys = ["providers", "schema", "witSha256", "world"];
const providerKeys = ["capability", "functions", "module"];
const importSetKeys = ["imports", "release"];
const world = "netwasm:hosting-platform/wasi@1.0.0";

export function createWasiPlatformProviderRegistry(options) {
  assertExactDataObject(options, factoryKeys, "WASI platform provider registry options");
  if (typeof options.createImports !== "function"
      || typeof options.validateProviderMetadata !== "function") {
    throw new TypeError("WASI import factory and provider metadata validator are required");
  }
  const providers = readCatalog(options.catalog);
  const importSets = new WeakMap();
  const openImports = request => {
    let importSet = importSets.get(request);
    if (importSet === undefined) {
      importSet = readImportSet(options.createImports(request));
      importSets.set(request, importSet);
    }
    return importSet.imports;
  };
  const registry = createPlatformProviderRegistry({
    registrations: providers.map(provider => Object.freeze({
      provider,
      createSource(request) {
        const source = openImports(request)[provider.module];
        if (source === null || typeof source !== "object" || Array.isArray(source)) {
          throw new TypeError(`WASI platform does not provide module '${provider.module}'`);
        }
        return source;
      },
    })),
    validateProviderMetadata: options.validateProviderMetadata,
  });
  return Object.freeze({
    resolve: registry.resolve,
    async release(request) {
      const importSet = importSets.get(request);
      if (importSet === undefined) return;
      importSets.delete(request);
      await importSet.release();
    },
  });
}

function readImportSet(value) {
  assertExactDataObject(value, importSetKeys, "WASI import set");
  if (!Object.isFrozen(value) || value.imports === null || typeof value.imports !== "object"
      || typeof value.release !== "function") {
    throw new TypeError("WASI import set is incomplete");
  }
  return value;
}

function readCatalog(catalog) {
  assertExactDataObject(catalog, catalogKeys, "WASI platform catalog");
  if (!Object.isFrozen(catalog) || catalog.schema !== 2 || catalog.world !== world
      || typeof catalog.witSha256 !== "string" || !/^[0-9a-f]{64}$/u.test(catalog.witSha256)
      || !Array.isArray(catalog.providers) || !Object.isFrozen(catalog.providers)
      || catalog.providers.length === 0) {
    throw new TypeError("WASI platform catalog is incompatible");
  }
  for (const provider of catalog.providers) {
    assertExactDataObject(provider, providerKeys, "WASI platform provider");
    if (!Object.isFrozen(provider)
        || typeof provider.module !== "string" || provider.module.length === 0
        || !Array.isArray(provider.functions) || !Object.isFrozen(provider.functions)) {
      throw new TypeError("WASI platform provider is incomplete");
    }
  }
  return catalog.providers;
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

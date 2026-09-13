const factoryKeys = ["projectIdentity"];
const requestKeys = ["providers", "sources"];
const applicationProviderKeys = ["functions", "module"];
const platformProviderKeys = ["capability", "functions", "module"];
const sourceKeys = ["module", "value"];
const identityKeys = ["componentModule", "rawModule"];

export function createProviderModuleProjector(options) {
  assertExactDataObject(options, factoryKeys, "provider module projector options");
  if (typeof options.projectIdentity !== "function") {
    throw new TypeError("provider identity projection action is required");
  }

  return Object.freeze(function projectProviderModules(request) {
    assertExactDataObject(request, requestKeys, "provider module projection request");
    if (!Array.isArray(request.providers) || !Array.isArray(request.sources)) {
      throw new TypeError("provider metadata and source collections must be explicit");
    }
    const sources = readSources(request.sources);
    const providerModules = new Set();
    const componentImports = Object.create(null);
    const consumerModules = Object.create(null);
    const rawProviders = Object.create(null);
    for (const provider of request.providers) {
      const module = readProviderModule(provider);
      if (providerModules.has(module)) {
        throw new TypeError(`selected provider module '${module}' is duplicated`);
      }
      providerModules.add(module);
      const source = sources.get(module);
      if (source === undefined) {
        throw new TypeError(`selected provider module '${module}' has no runtime source`);
      }
      sources.delete(module);
      const identity = readProjectedIdentity(options.projectIdentity({ module }));
      if (Object.hasOwn(componentImports, identity.componentModule)
          || Object.hasOwn(rawProviders, identity.rawModule)) {
        throw new TypeError(`selected provider module '${module}' collides after projection`);
      }
      componentImports[identity.componentModule] = source;
      consumerModules[module] = source;
      rawProviders[identity.rawModule] = source;
    }
    if (sources.size !== 0) {
      throw new TypeError("a runtime provider source was not selected by validated metadata");
    }
    return Object.freeze({
      componentImports: Object.freeze(componentImports),
      consumerModules: Object.freeze(consumerModules),
      rawProviders: Object.freeze(rawProviders),
    });
  });
}

function readSources(values) {
  const sources = new Map();
  for (const value of values) {
    assertExactDataObject(value, sourceKeys, "runtime provider source");
    if (typeof value.module !== "string" || value.module.length === 0) {
      throw new TypeError("runtime provider source module is invalid");
    }
    if (value.value === null || typeof value.value !== "object" || Array.isArray(value.value)) {
      throw new TypeError("runtime provider source value is invalid");
    }
    if (sources.has(value.module)) {
      throw new TypeError(`runtime provider source '${value.module}' is duplicated`);
    }
    sources.set(value.module, value.value);
  }
  return sources;
}

function readProviderModule(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("selected provider metadata is invalid");
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const keys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || !sameStrings(keys, applicationProviderKeys) && !sameStrings(keys, platformProviderKeys)
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))
      || typeof value.module !== "string" || value.module.length === 0) {
    throw new TypeError("selected provider metadata shape is invalid");
  }
  return value.module;
}

function readProjectedIdentity(value) {
  assertExactDataObject(value, identityKeys, "projected provider identity");
  if (typeof value.componentModule !== "string" || value.componentModule.length === 0
      || typeof value.rawModule !== "string" || value.rawModule.length === 0) {
    throw new TypeError("projected provider identity is invalid");
  }
  return value;
}

function sameStrings(left, right) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
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
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

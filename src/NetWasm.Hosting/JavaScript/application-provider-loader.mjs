const factoryKeys = ["hashBytes", "importModule", "readArtifact"];
const requestKeys = ["binding", "signal"];
const bindingKeys = ["applicationProviders", "manifest", "platformProviders", "request"];
const providerKeys = ["functions", "module"];
const importKeys = ["artifactPath", "module", "sha256"];
const artifactKeys = ["mediaType", "relativePath", "role", "schemaVersion", "sha256"];

export function createApplicationProviderLoader(options) {
  assertExactDataObject(options, factoryKeys, "application provider loader options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`application provider loader '${name}' action is required`);
    }
  }

  return Object.freeze(async function loadApplicationProviders(request) {
    assertExactDataObject(request, requestKeys, "application provider load request");
    validateSignal(request.signal);
    const plans = createLoadPlans(request.binding);
    throwIfAborted(request.signal);
    const verified = [];
    for (const plan of plans) {
      const transportBytes = await options.readArtifact(plan.artifact, request.signal);
      if (!(transportBytes instanceof Uint8Array)) {
        throw new TypeError("application provider transport returned invalid bytes");
      }
      const bytes = new Uint8Array(transportBytes);
      const digest = await options.hashBytes(bytes);
      if (typeof digest !== "string" || digest !== plan.artifact.sha256) {
        throw new TypeError("application provider artifact failed integrity validation");
      }
      verified.push(Object.freeze({ ...plan, bytes }));
      throwIfAborted(request.signal);
    }

    const sources = [];
    for (const plan of verified) {
      const namespace = await options.importModule(plan.bytes, plan.artifact);
      if (namespace === null || typeof namespace !== "object" || Array.isArray(namespace)) {
        throw new TypeError("application provider module returned an invalid namespace");
      }
      sources.push(Object.freeze({ module: plan.module, value: namespace }));
      throwIfAborted(request.signal);
    }
    return Object.freeze(sources);
  });
}

function createLoadPlans(value) {
  assertExactDataObject(value, bindingKeys, "validated capability binding");
  if (!Array.isArray(value.applicationProviders)
      || value.manifest === null || typeof value.manifest !== "object"
      || !Array.isArray(value.manifest.artifacts)
      || value.request === null || typeof value.request !== "object"
      || !Array.isArray(value.request.applicationImports)) {
    throw new TypeError("validated capability binding is incomplete");
  }
  const imports = uniqueBy(
    value.request.applicationImports,
    importKeys,
    "application import binding",
    item => item.module);
  const artifacts = uniqueBy(
    value.manifest.artifacts,
    artifactKeys,
    "deployment artifact",
    item => item.relativePath);
  const providers = new Set();
  return Object.freeze(value.applicationProviders.map(provider => {
    assertExactDataObject(provider, providerKeys, "application provider metadata");
    if (typeof provider.module !== "string" || provider.module.length === 0
        || providers.has(provider.module)) {
      throw new TypeError("application provider metadata module is invalid");
    }
    providers.add(provider.module);
    const binding = imports.get(provider.module);
    if (binding === undefined) {
      throw new TypeError(`application provider module '${provider.module}' has no import binding`);
    }
    const artifact = artifacts.get(binding.artifactPath);
    if (artifact === undefined || artifact.role !== "application-import"
        || artifact.mediaType !== "text/javascript" || artifact.sha256 !== binding.sha256) {
      throw new TypeError(`application provider module '${provider.module}' has no matching artifact`);
    }
    return Object.freeze({ module: provider.module, artifact });
  }));
}

function uniqueBy(values, keys, label, selectKey) {
  const result = new Map();
  for (const value of values) {
    assertExactDataObject(value, keys, label);
    const key = selectKey(value);
    if (typeof key !== "string" || key.length === 0 || result.has(key)) {
      throw new TypeError(`${label} identity is invalid`);
    }
    result.set(key, value);
  }
  return result;
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("application provider signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (signal?.aborted) throw new Error("application provider loading was cancelled");
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

const factoryKeys = [
  "isInternalImport",
  "selectProviderKind",
  "validateManifest",
  "validateProviderMetadata",
  "validateRequest",
];
const requestKeys = ["applicationProviders", "manifest", "platformProviders", "request"];

export function createCapabilityBindingValidator(options) {
  assertExactDataObject(options, factoryKeys, "capability binding validator options");
  const {
    isInternalImport,
    selectProviderKind,
    validateManifest,
    validateProviderMetadata,
    validateRequest,
  } = options;
  if (typeof isInternalImport !== "function"
      || typeof selectProviderKind !== "function"
      || typeof validateManifest !== "function"
      || typeof validateProviderMetadata !== "function"
      || typeof validateRequest !== "function") {
    throw new TypeError("internal-import policy, provider-kind selector, and validation actions are required");
  }

  return Object.freeze(function validateCapabilityBinding(value) {
    assertExactDataObject(value, requestKeys, "capability binding request");
    const manifest = validateManifest(value.manifest);
    const request = validateRequest(value.request);
    if (manifest.buildFingerprint !== request.buildFingerprint) {
      throw new TypeError("execution request does not target the deployment build");
    }
    if (!Array.isArray(value.platformProviders)
        || !Array.isArray(value.applicationProviders)) {
      throw new TypeError("selected provider collections must be explicit");
    }

    const requiredModules = new Set(manifest.requiredImportModules.filter(
      module => !readInternalImport(isInternalImport, module)));
    const providerModules = new Set();
    const platformProviders = validatePlatformProviders(
      value.platformProviders,
      requiredModules,
      providerModules,
      request.grants,
      selectProviderKind,
      validateProviderMetadata);
    const applicationProviders = validateApplicationProviders(
      value.applicationProviders,
      requiredModules,
      providerModules,
      selectProviderKind,
      validateProviderMetadata);
    validateApplicationBindings(manifest, request, requiredModules, applicationProviders);
    validateRequiredImports(
      manifest.requiredImports,
      platformProviders,
      applicationProviders,
      isInternalImport,
      selectProviderKind);
    if (providerModules.size !== requiredModules.size) {
      throw new TypeError("every required import module must have one selected provider");
    }
    return Object.freeze({
      manifest,
      request,
      platformProviders: platformProviders.values,
      applicationProviders: applicationProviders.values,
    });
  });
}

function validatePlatformProviders(
  values,
  requiredModules,
  providerModules,
  grants,
  selectProviderKind,
  validateProviderMetadata) {
  const providers = new Map();
  const snapshots = values.map(value => {
    const snapshot = validateProviderMetadata({ kind: "platform", provider: value });
    if (readProviderKind(selectProviderKind, snapshot.module) !== "platform") {
      throw new TypeError("platform provider module must be reserved");
    }
    validateRequiredProvider(snapshot.module, requiredModules, providerModules);
    if (!isGranted(snapshot.capability, grants)) {
      throw new TypeError(`platform capability '${snapshot.capability}' was not granted`);
    }
    providers.set(snapshot.module, snapshot);
    return snapshot;
  });
  return Object.freeze({ byModule: providers, values: Object.freeze(snapshots) });
}

function validateApplicationProviders(
  values,
  requiredModules,
  providerModules,
  selectProviderKind,
  validateProviderMetadata) {
  const providers = new Map();
  const snapshots = values.map(value => {
    const snapshot = validateProviderMetadata({ kind: "application", provider: value });
    if (readProviderKind(selectProviderKind, snapshot.module) !== "application") {
      throw new TypeError("application provider module must not be reserved");
    }
    validateRequiredProvider(snapshot.module, requiredModules, providerModules);
    providers.set(snapshot.module, snapshot);
    return snapshot;
  });
  return Object.freeze({ byModule: providers, values: Object.freeze(snapshots) });
}

function validateRequiredProvider(module, requiredModules, providerModules) {
  if (!requiredModules.has(module)) {
    throw new TypeError(`selected provider module '${module}' is not required`);
  }
  if (providerModules.has(module)) {
    throw new TypeError(`selected provider module '${module}' is duplicated`);
  }
  providerModules.add(module);
}

function validateApplicationBindings(manifest, request, requiredModules, applications) {
  const artifacts = new Map(manifest.artifacts.map(item => [item.relativePath, item]));
  const bindings = new Map(request.applicationImports.map(item => [item.module, item]));
  for (const binding of request.applicationImports) {
    if (!requiredModules.has(binding.module)) {
      throw new TypeError(`application import module '${binding.module}' is not required`);
    }
    if (!applications.byModule.has(binding.module)) {
      throw new TypeError(`application import module '${binding.module}' has no selected provider`);
    }
    const artifact = artifacts.get(binding.artifactPath);
    if (artifact === undefined) {
      throw new TypeError(`application import artifact '${binding.artifactPath}' is missing`);
    }
    if (artifact.role !== "application-import"
        || artifact.mediaType !== "text/javascript"
        || artifact.sha256 !== binding.sha256) {
      throw new TypeError(`application import module '${binding.module}' does not match its artifact`);
    }
  }
  for (const provider of applications.values) {
    if (!bindings.has(provider.module)) {
      throw new TypeError(`application provider module '${provider.module}' has no request binding`);
    }
  }
}

function validateRequiredImports(
  requiredImports,
  platforms,
  applications,
  isInternalImport,
  selectProviderKind) {
  for (const required of requiredImports) {
    if (readInternalImport(isInternalImport, required.interface)) {
      continue;
    }
    const providers = readProviderKind(selectProviderKind, required.interface) === "platform"
      ? platforms
      : applications;
    const provider = providers.byModule.get(required.interface);
    if (provider === undefined) {
      throw new TypeError(`required import module '${required.interface}' has no selected provider`);
    }
    const provided = provider.functions.find(item => item.name === required.name);
    if (provided === undefined) {
      throw new TypeError(`required import '${required.interface}.${required.name}' is missing`);
    }
    if (!sameStrings(provided.parameters, required.parameters)
        || !sameStrings(provided.results, required.results)) {
      throw new TypeError(`required import '${required.interface}.${required.name}' has an incompatible signature`);
    }
  }
}

function readInternalImport(isInternalImport, module) {
  const internal = isInternalImport({ module });
  if (typeof internal !== "boolean") {
    throw new TypeError("internal-import policy returned an unsupported result");
  }
  return internal;
}

function isGranted(capability, grants) {
  switch (capability) {
    case "baseline":
    case "environment":
    case "preopenedDirectories":
      return true;
    case "network": return grants.network === "allowAll";
    case "wallClock": return grants.clocks.includes("wall");
    case "monotonicClock": return grants.clocks.includes("monotonic");
    case "randomness": return grants.randomness;
  }
}

function sameStrings(left, right) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function readProviderKind(selectProviderKind, module) {
  const kind = selectProviderKind({ module });
  if (kind !== "platform" && kind !== "application") {
    throw new TypeError("provider-kind selector returned an unsupported kind");
  }
  return kind;
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

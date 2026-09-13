const factoryKeys = ["isInternalImport", "resolvePlatformProvider", "selectProviderKind"];
const requestKeys = ["manifest", "request"];
const functionKeys = ["interface", "name", "parameters", "results"];
const applicationImportKeys = ["artifactPath", "module", "sha256"];
const registrationKeys = ["createSource", "provider"];
const platformProviderKeys = ["capability", "functions", "module"];
const providerKinds = new Set(["application", "platform"]);

export function createProviderMetadataSelector(options) {
  assertExactDataObject(options, factoryKeys, "provider metadata selector options");
  if (typeof options.isInternalImport !== "function"
      || typeof options.resolvePlatformProvider !== "function"
      || typeof options.selectProviderKind !== "function") {
    throw new TypeError("internal-import policy, platform provider resolver, and provider-kind selector are required");
  }

  return Object.freeze(function selectProviderMetadata(request) {
    assertExactDataObject(request, requestKeys, "provider metadata selection request");
    const requiredImports = readArrayProperty(
      request.manifest,
      "requiredImports",
      "deployment manifest required imports");
    const requiredImportModules = readArrayProperty(
      request.manifest,
      "requiredImportModules",
      "deployment manifest required import modules");
    const applicationImports = readArrayProperty(
      request.request,
      "applicationImports",
      "execution request application imports");
    const applicationBindings = readApplicationBindings(applicationImports);
    const modules = groupRequiredFunctions(
      requiredImportModules,
      requiredImports,
      options.isInternalImport,
      options.selectProviderKind);
    const platformProviders = [];
    const applicationProviders = [];

    for (const entry of modules.values()) {
      if (entry.kind === "platform") {
        const registration = options.resolvePlatformProvider({ module: entry.module });
        platformProviders.push(readPlatformProvider(registration, entry.module));
      } else if (applicationBindings.has(entry.module)) {
        applicationProviders.push(Object.freeze({
          module: entry.module,
          functions: Object.freeze(entry.functions),
        }));
      }
    }

    return Object.freeze({
      platformProviders: Object.freeze(platformProviders),
      applicationProviders: Object.freeze(applicationProviders),
    });
  });
}

function groupRequiredFunctions(
  requiredImportModules,
  requiredImports,
  isInternalImport,
  selectProviderKind) {
  const modules = new Map();
  const identities = new Map();
  for (const module of requiredImportModules) {
    if (typeof module !== "string" || module.length === 0 || identities.has(module)) {
      throw new TypeError("required import module inventory is invalid or duplicated");
    }
    const internal = readInternalImport(isInternalImport, module);
    identities.set(module, internal);
    if (internal) {
      continue;
    }
    modules.set(module, {
      module,
      kind: readProviderKind(selectProviderKind, module),
      functions: [],
    });
  }
  for (const value of requiredImports) {
    assertExactDataObject(value, functionKeys, "required import function");
    if (typeof value.interface !== "string" || value.interface.length === 0
        || typeof value.name !== "string" || value.name.length === 0
        || !Array.isArray(value.parameters) || !Array.isArray(value.results)) {
      throw new TypeError("required import function is incomplete");
    }
    if (!identities.has(value.interface)) {
      throw new TypeError("required import function has no required module");
    }
    if (identities.get(value.interface)) {
      continue;
    }
    const kind = readProviderKind(selectProviderKind, value.interface);
    let entry = modules.get(value.interface);
    if (entry.kind !== kind) {
      throw new TypeError("provider-kind selector returned inconsistent ownership");
    }
    entry.functions.push(snapshotFunction(value));
  }
  return modules;
}

function readInternalImport(isInternalImport, module) {
  const internal = isInternalImport({ module });
  if (typeof internal !== "boolean") {
    throw new TypeError("internal-import policy returned an unsupported result");
  }
  return internal;
}

function readApplicationBindings(values) {
  const modules = new Set();
  for (const value of values) {
    assertExactDataObject(value, applicationImportKeys, "application import binding");
    if (typeof value.module !== "string" || value.module.length === 0) {
      throw new TypeError("application import binding module is required");
    }
    modules.add(value.module);
  }
  return modules;
}

function readPlatformProvider(registration, module) {
  assertExactDataObject(registration, registrationKeys, "resolved platform provider registration");
  if (!Object.isFrozen(registration)) {
    throw new TypeError("resolved platform provider registration must be immutable");
  }
  if (typeof registration.createSource !== "function") {
    throw new TypeError("resolved platform provider source factory is required");
  }
  assertExactDataObject(
    registration.provider,
    platformProviderKeys,
    "resolved platform provider metadata");
  if (!Object.isFrozen(registration.provider)
      || registration.provider.module !== module
      || !Array.isArray(registration.provider.functions)
      || !Object.isFrozen(registration.provider.functions)
      || registration.provider.functions.some(value => value === null
        || typeof value !== "object" || !Object.isFrozen(value))) {
    throw new TypeError("resolved platform provider metadata is invalid");
  }
  return registration.provider;
}

function snapshotFunction(value) {
  return Object.freeze({
    interface: value.interface,
    name: value.name,
    parameters: Object.freeze([...value.parameters]),
    results: Object.freeze([...value.results]),
  });
}

function readProviderKind(selectProviderKind, module) {
  const kind = selectProviderKind({ module });
  if (!providerKinds.has(kind)) {
    throw new TypeError("provider-kind selector returned an unsupported kind");
  }
  return kind;
}

function readArrayProperty(value, key, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} owner is invalid`);
  }
  const descriptor = Object.getOwnPropertyDescriptor(value, key);
  if (descriptor === undefined || !descriptor.enumerable || !("value" in descriptor)
      || !Array.isArray(descriptor.value)) {
    throw new TypeError(`${label} must be explicit`);
  }
  return descriptor.value;
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

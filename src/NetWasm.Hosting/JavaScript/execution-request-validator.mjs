const validatorKeys = ["isAbsoluteHostPath", "selectProviderKind"];
const requestKeys = [
  "applicationImports",
  "arguments",
  "buildFingerprint",
  "deploymentManifestSha256",
  "environment",
  "grants",
  "schemaVersion",
];
const grantKeys = ["clocks", "environment", "network", "preopens", "randomness"];
const environmentKeys = ["name", "value"];
const preopenKeys = ["access", "guestPath", "hostPath"];
const applicationImportKeys = ["artifactPath", "module", "sha256"];
const networkPolicies = new Set(["allowAll", "denyAll"]);
const preopenAccesses = new Set(["readOnly", "readWrite"]);
const supportedClocks = new Set(["monotonic", "wall"]);
const digestPattern = /^[0-9a-f]{64}$/u;
const versionedIdentifierPattern = /^[a-z0-9][a-z0-9.\-:/]*@[0-9]+\.[0-9]+\.[0-9]+$/u;

export function createExecutionRequestValidator(options) {
  assertExactDataObject(options, validatorKeys, "execution request validator options");
  const { isAbsoluteHostPath, selectProviderKind } = options;
  if (typeof isAbsoluteHostPath !== "function" || typeof selectProviderKind !== "function") {
    throw new TypeError("absolute host-path predicate and provider-kind selector are required");
  }

  return Object.freeze(function validateExecutionRequest(request) {
    assertExactDataObject(request, requestKeys, "execution request");
    if (request.schemaVersion !== 1) {
      throw new TypeError("execution request schema is unsupported");
    }
    validateDigest(request.buildFingerprint, "build fingerprint");
    validateDigest(request.deploymentManifestSha256, "deployment manifest digest");
    const arguments_ = validateArguments(request.arguments);
    const grants = validateGrants(request.grants, isAbsoluteHostPath);
    const environment = validateEnvironment(request.environment, grants.environment);
    const applicationImports = validateApplicationImports(
      request.applicationImports,
      selectProviderKind);
    return Object.freeze({
      schemaVersion: request.schemaVersion,
      buildFingerprint: request.buildFingerprint,
      deploymentManifestSha256: request.deploymentManifestSha256,
      arguments: arguments_,
      environment,
      grants,
      applicationImports,
    });
  });
}

function validateArguments(value) {
  if (!Array.isArray(value)) {
    throw new TypeError("execution arguments must be explicit");
  }
  return Object.freeze(value.map(argument => {
    if (typeof argument !== "string" || argument.includes("\0")) {
      throw new TypeError("execution argument must be text without NUL");
    }
    return argument;
  }));
}

function validateGrants(value, isAbsoluteHostPath) {
  assertExactDataObject(value, grantKeys, "capability grants");
  if (!networkPolicies.has(value.network)) {
    throw new TypeError("network policy is unsupported");
  }
  if (typeof value.randomness !== "boolean") {
    throw new TypeError("randomness grant must be a Boolean");
  }
  if (!Array.isArray(value.environment)
      || !Array.isArray(value.preopens)
      || !Array.isArray(value.clocks)) {
    throw new TypeError("capability grant collections must be explicit");
  }

  const environment = snapshotUniqueStrings(
    value.environment,
    validateEnvironmentName,
    "granted environment name");
  const guestPaths = new Set();
  const preopens = Object.freeze(value.preopens.map(preopen => {
    assertExactDataObject(preopen, preopenKeys, "preopen grant");
    validateHostPath(preopen.hostPath, isAbsoluteHostPath);
    validateGuestPath(preopen.guestPath);
    if (!preopenAccesses.has(preopen.access)) {
      throw new TypeError("preopen access is unsupported");
    }
    if (guestPaths.has(preopen.guestPath)) {
      throw new TypeError(`guest preopen path '${preopen.guestPath}' is duplicated`);
    }
    guestPaths.add(preopen.guestPath);
    return Object.freeze({
      hostPath: preopen.hostPath,
      guestPath: preopen.guestPath,
      access: preopen.access,
    });
  }));
  const clocks = snapshotUniqueStrings(
    value.clocks,
    validateClock,
    "clock grant");
  return Object.freeze({
    environment,
    preopens,
    network: value.network,
    clocks,
    randomness: value.randomness,
  });
}

function validateEnvironment(value, grantedNames) {
  if (!Array.isArray(value)) {
    throw new TypeError("execution environment must be explicit");
  }
  const grants = new Set(grantedNames);
  const names = new Set();
  return Object.freeze(value.map(variable => {
    assertExactDataObject(variable, environmentKeys, "environment variable");
    validateEnvironmentName(variable.name);
    if (typeof variable.value !== "string" || variable.value.includes("\0")) {
      throw new TypeError("environment value must be text without NUL");
    }
    if (names.has(variable.name)) {
      throw new TypeError(`environment name '${variable.name}' is duplicated`);
    }
    names.add(variable.name);
    if (!grants.has(variable.name)) {
      throw new TypeError(`environment name '${variable.name}' was not granted`);
    }
    return Object.freeze({ name: variable.name, value: variable.value });
  }));
}

function validateApplicationImports(value, selectProviderKind) {
  if (!Array.isArray(value)) {
    throw new TypeError("application import bindings must be explicit");
  }
  const modules = new Set();
  return Object.freeze(value.map(binding => {
    assertExactDataObject(binding, applicationImportKeys, "application import binding");
    validateVersionedIdentifier(binding.module);
    if (readProviderKind(selectProviderKind, binding.module) !== "application") {
      throw new TypeError(`application import module '${binding.module}' is reserved`);
    }
    if (modules.has(binding.module)) {
      throw new TypeError(`application import module '${binding.module}' is duplicated`);
    }
    modules.add(binding.module);
    validateArtifactPath(binding.artifactPath);
    validateDigest(binding.sha256, "application import digest");
    return Object.freeze({
      module: binding.module,
      artifactPath: binding.artifactPath,
      sha256: binding.sha256,
    });
  }));
}

function readProviderKind(selectProviderKind, module) {
  const kind = selectProviderKind({ module });
  if (kind !== "platform" && kind !== "application") {
    throw new TypeError("provider-kind selector returned an unsupported kind");
  }
  return kind;
}

function snapshotUniqueStrings(values, validate, label) {
  const identities = new Set();
  const result = values.map(value => {
    validate(value);
    if (identities.has(value)) {
      throw new TypeError(`${label} '${value}' is duplicated`);
    }
    identities.add(value);
    return value;
  });
  return Object.freeze(result);
}

function validateEnvironmentName(value) {
  if (typeof value !== "string" || value.length === 0
      || value.includes("=") || value.includes("\0")) {
    throw new TypeError("environment name must be non-empty and contain no equals or NUL");
  }
}

function validateHostPath(value, isAbsoluteHostPath) {
  if (typeof value !== "string" || value.trim().length === 0
      || value.includes("\0") || !isAbsoluteHostPath(value)) {
    throw new TypeError("preopen host path must be an absolute local path without NUL");
  }
}

function validateGuestPath(value) {
  if (typeof value !== "string" || value.length === 0 || !value.startsWith("/")
      || value.includes("\\") || value.includes("\0") || value.trim() !== value) {
    throw new TypeError("guest preopen path must use canonical absolute slash syntax");
  }
  if (value.length > 1 && value.slice(1).split("/")
    .some(segment => segment.length === 0 || segment === "." || segment === "..")) {
    throw new TypeError("guest preopen path contains an empty or traversal segment");
  }
}

function validateClock(value) {
  if (!supportedClocks.has(value)) {
    throw new TypeError("clock grant is unsupported");
  }
}

function validateArtifactPath(value) {
  if (typeof value !== "string" || value.length === 0 || value.startsWith("/")
      || value.includes("\\") || value.includes(":") || value.includes("\0")
      || value.trim() !== value) {
    throw new TypeError("application import artifact path must use canonical relative slash syntax");
  }
  if (value.split("/").some(segment => segment.length === 0 || segment === "." || segment === "..")) {
    throw new TypeError("application import artifact path contains an empty or traversal segment");
  }
}

function validateVersionedIdentifier(value) {
  if (typeof value !== "string" || !versionedIdentifierPattern.test(value)) {
    throw new TypeError("application import module must be a canonical exact versioned identifier");
  }
}

function validateDigest(value, label) {
  if (typeof value !== "string" || !digestPattern.test(value)) {
    throw new TypeError(`${label} must be a lowercase SHA-256 digest`);
  }
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

import { isCanonicalWitFunctionName } from "./canonical-wit-function-name.mjs";

const rootKeys = [
  "artifacts",
  "buildFingerprint",
  "deploymentKind",
  "executionContract",
  "exports",
  "featureSet",
  "profile",
  "requiredImportModules",
  "requiredImports",
  "runtimeFeatures",
  "schemaVersion",
  "semanticBuildId",
  "target",
  "versions",
];
const versionKeys = ["compiler", "hosting", "runtime", "runtimeAbi", "sdk", "toolchain"];
const artifactKeys = ["mediaType", "relativePath", "role", "schemaVersion", "sha256"];
const functionKeys = ["interface", "name", "parameters", "results"];
const deploymentKinds = new Set(["browser", "component", "raw"]);
const targets = new Set(["wasm32", "wasm64"]);
const supportedRuntimeFeatures = new Set(["local-time"]);
const digestPattern = /^[0-9a-f]{64}$/u;
const tokenPattern = /^[a-z0-9][a-z0-9.-]*$/u;
const versionedIdentifierPattern = /^[a-z0-9][a-z0-9.\-:/]*@[0-9]+\.[0-9]+\.[0-9]+$/u;

export function validateDeploymentManifest(manifest) {
  assertExactDataObject(manifest, rootKeys, "deployment manifest");
  if (manifest.schemaVersion !== 1) {
    throw new TypeError("deployment manifest schema is unsupported");
  }
  validateDigest(manifest.semanticBuildId, "semantic build identity");
  validateDigest(manifest.buildFingerprint, "build fingerprint");
  if (!deploymentKinds.has(manifest.deploymentKind)) {
    throw new TypeError("deployment kind is unsupported");
  }
  if (manifest.profile !== "netwasm0.1") {
    throw new TypeError("managed profile is unsupported");
  }
  if (!targets.has(manifest.target)) {
    throw new TypeError("deployment target is unsupported");
  }
  validateText(manifest.featureSet, "feature set");
  validateVersionedIdentifier(manifest.executionContract, "execution contract");

  const versions = validateVersions(manifest.versions);
  const runtimeFeatures = validateRuntimeFeatures(manifest.runtimeFeatures);
  const artifacts = validateArtifacts(
    manifest.artifacts,
    runtimeFeatures.includes("local-time"));
  const requiredImportModules = validateImportModules(manifest.requiredImportModules);
  const requiredImports = validateFunctions(manifest.requiredImports, "required imports");
  for (const required of requiredImports) {
    if (!requiredImportModules.includes(required.interface)) {
      throw new TypeError(
        `required import '${required.interface}.${required.name}' has no required module`);
    }
  }
  const exports = validateFunctions(manifest.exports, "exports");

  return Object.freeze({
    schemaVersion: manifest.schemaVersion,
    semanticBuildId: manifest.semanticBuildId,
    deploymentKind: manifest.deploymentKind,
    profile: manifest.profile,
    target: manifest.target,
    featureSet: manifest.featureSet,
    executionContract: manifest.executionContract,
    versions,
    buildFingerprint: manifest.buildFingerprint,
    runtimeFeatures,
    artifacts,
    requiredImportModules,
    requiredImports,
    exports,
  });
}

function validateImportModules(value) {
  if (!Array.isArray(value)) {
    throw new TypeError("deployment required import modules must be explicit");
  }
  const identities = new Set();
  const modules = value.map(module => {
    validateVersionedIdentifier(module, "required import module");
    if (identities.has(module)) {
      throw new TypeError(`required import module '${module}' is duplicated`);
    }
    identities.add(module);
    return module;
  });
  return Object.freeze(modules);
}

function validateVersions(value) {
  assertExactDataObject(value, versionKeys, "deployment versions");
  const result = {};
  for (const key of versionKeys) {
    validateText(value[key], `deployment version '${key}'`);
    result[key] = value[key];
  }
  return Object.freeze(result);
}

function validateRuntimeFeatures(value) {
  if (!Array.isArray(value)) {
    throw new TypeError("deployment runtime features must be explicit");
  }
  const identities = new Set();
  const result = value.map(feature => {
    validateToken(feature, "deployment runtime feature");
    if (!supportedRuntimeFeatures.has(feature)) {
      throw new TypeError(`deployment runtime feature '${feature}' is unsupported`);
    }
    if (identities.has(feature)) {
      throw new TypeError(`deployment runtime feature '${feature}' is duplicated`);
    }
    identities.add(feature);
    return feature;
  });
  return Object.freeze(result);
}

function validateArtifacts(value, hasLocalTime) {
  if (!Array.isArray(value) || value.length === 0) {
    throw new TypeError("deployment artifacts must contain an application");
  }
  const paths = new Set();
  let application;
  let applicationCount = 0;
  let timeZone;
  const artifacts = value.map(artifact => {
    assertExactDataObject(artifact, artifactKeys, "deployment artifact");
    validateRelativePath(artifact.relativePath);
    if (paths.has(artifact.relativePath)) {
      throw new TypeError(`deployment artifact path '${artifact.relativePath}' is duplicated`);
    }
    paths.add(artifact.relativePath);
    validateToken(artifact.role, "deployment artifact role");
    validateMediaType(artifact.mediaType);
    validateDigest(artifact.sha256, "deployment artifact digest");
    if (artifact.schemaVersion !== null
        && (!Number.isInteger(artifact.schemaVersion) || artifact.schemaVersion <= 0)) {
      throw new TypeError("deployment artifact schema version must be null or positive");
    }
    const snapshot = Object.freeze({
      relativePath: artifact.relativePath,
      role: artifact.role,
      mediaType: artifact.mediaType,
      sha256: artifact.sha256,
      schemaVersion: artifact.schemaVersion,
    });
    if (artifact.role === "application") {
      applicationCount++;
      application = snapshot;
    } else if (artifact.role === "timezone-data") {
      if (timeZone !== undefined) {
        throw new TypeError("deployment contains multiple timezone artifacts");
      }
      timeZone = snapshot;
    }
    return snapshot;
  });
  if (applicationCount !== 1) {
    throw new TypeError("deployment must identify exactly one application artifact");
  }
  if (timeZone !== undefined) {
    if (!hasLocalTime) {
      throw new TypeError("timezone artifact requires the local-time runtime feature");
    }
    if (timeZone.relativePath !== `${application.relativePath}.tz-info`
        || timeZone.mediaType !== "application/octet-stream"
        || timeZone.schemaVersion !== 1) {
      throw new TypeError("timezone artifact does not satisfy the adjacent schema-1 contract");
    }
  }
  return Object.freeze(artifacts);
}

function validateFunctions(value, label) {
  if (!Array.isArray(value)) {
    throw new TypeError(`deployment ${label} must be explicit`);
  }
  const identities = new Set();
  const functions = value.map(func => {
    assertExactDataObject(func, functionKeys, "deployment function");
    validateVersionedIdentifier(func.interface, "deployment function interface");
    validateFunctionName(func.name);
    const identity = `${func.interface}\u0000${func.name}`;
    if (identities.has(identity)) {
      throw new TypeError(`deployment function '${func.interface}.${func.name}' is duplicated`);
    }
    identities.add(identity);
    const parameters = validateSignature(func.parameters);
    const results = validateSignature(func.results);
    return Object.freeze({
      interface: func.interface,
      name: func.name,
      parameters,
      results,
    });
  });
  return Object.freeze(functions);
}

function validateSignature(value) {
  if (!Array.isArray(value)) {
    throw new TypeError("deployment function signature must be explicit");
  }
  const result = value.map(item => {
    validateText(item, "deployment function signature value");
    return item;
  });
  return Object.freeze(result);
}

function validateRelativePath(value) {
  validateText(value, "deployment artifact path");
  if (value.startsWith("/") || value.includes("\\") || value.includes(":")) {
    throw new TypeError("deployment artifact path must use canonical relative slash syntax");
  }
  if (value.split("/").some(segment => segment.length === 0 || segment === "." || segment === "..")) {
    throw new TypeError("deployment artifact path contains an empty or traversal segment");
  }
}

function validateMediaType(value) {
  validateText(value, "deployment artifact media type");
  if (!value.includes("/")) {
    throw new TypeError("deployment artifact media type must be explicit");
  }
}

function validateDigest(value, label) {
  if (typeof value !== "string" || !digestPattern.test(value)) {
    throw new TypeError(`${label} must be a lowercase SHA-256 digest`);
  }
}

function validateVersionedIdentifier(value, label) {
  if (typeof value !== "string" || !versionedIdentifierPattern.test(value)) {
    throw new TypeError(`${label} must be a canonical exact versioned identifier`);
  }
}

function validateToken(value, label) {
  if (typeof value !== "string" || !tokenPattern.test(value)) {
    throw new TypeError(`${label} must use canonical lowercase syntax`);
  }
}

function validateFunctionName(value) {
  if (!isCanonicalWitFunctionName(value)) {
    throw new TypeError("deployment function name must use canonical WIT identity syntax");
  }
}

function validateText(value, label) {
  if (typeof value !== "string" || value.length === 0 || value.trim() !== value
      || /[\u0000-\u001f\u007f]/u.test(value)) {
    throw new TypeError(`${label} must be canonical non-empty text`);
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

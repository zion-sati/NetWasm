const factoryKeys = ["isAbsoluteHostPath"];
const descriptorKeys = [
  "buildFingerprint",
  "deploymentManifestPath",
  "deploymentManifestSha256",
  "hostExecutablePath",
  "hostingVersion",
  "launcherPath",
  "schemaVersion",
  "toolPackages",
];
const packageKeys = ["id", "rootPath", "sha256", "version"];
const digestPattern = /^[0-9a-f]{64}$/u;

export function createExecutionDescriptorValidator(options) {
  assertExactDataObject(options, factoryKeys, "execution descriptor validator options");
  if (typeof options.isAbsoluteHostPath !== "function") {
    throw new TypeError("execution descriptor absolute host-path predicate is required");
  }

  return Object.freeze(function validateExecutionDescriptor(descriptor) {
    assertExactDataObject(descriptor, descriptorKeys, "execution descriptor");
    if (descriptor.schemaVersion !== 1) {
      throw new TypeError("execution descriptor schema is unsupported");
    }
    validateDigest(descriptor.buildFingerprint, "execution build fingerprint");
    validateDigest(
      descriptor.deploymentManifestSha256,
      "execution deployment manifest digest");
    validateText(descriptor.hostingVersion, "execution Hosting version");
    validatePath(
      descriptor.deploymentManifestPath,
      options.isAbsoluteHostPath,
      "execution deployment manifest path");
    validatePath(
      descriptor.hostExecutablePath,
      options.isAbsoluteHostPath,
      "execution host executable path");
    validatePath(
      descriptor.launcherPath,
      options.isAbsoluteHostPath,
      "execution launcher path");

    if (!Array.isArray(descriptor.toolPackages) || descriptor.toolPackages.length === 0) {
      throw new TypeError("execution tool packages must be a non-empty array");
    }
    const identities = new Set();
    const toolPackages = Object.freeze(descriptor.toolPackages.map(package_ => {
      assertExactDataObject(package_, packageKeys, "execution tool package");
      validateText(package_.id, "execution tool package ID");
      validateText(package_.version, "execution tool package version");
      validatePath(
        package_.rootPath,
        options.isAbsoluteHostPath,
        "execution tool package root");
      validateDigest(package_.sha256, "execution tool package digest");
      const identity = package_.id.toUpperCase();
      if (identities.has(identity)) {
        throw new TypeError(`execution tool package '${package_.id}' is duplicated`);
      }
      identities.add(identity);
      return Object.freeze({
        id: package_.id,
        version: package_.version,
        rootPath: package_.rootPath,
        sha256: package_.sha256,
      });
    }));

    return Object.freeze({
      schemaVersion: descriptor.schemaVersion,
      buildFingerprint: descriptor.buildFingerprint,
      deploymentManifestPath: descriptor.deploymentManifestPath,
      deploymentManifestSha256: descriptor.deploymentManifestSha256,
      hostingVersion: descriptor.hostingVersion,
      hostExecutablePath: descriptor.hostExecutablePath,
      launcherPath: descriptor.launcherPath,
      toolPackages,
    });
  });
}

function validateText(value, label) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${label} is required`);
  }
}

function validatePath(value, isAbsoluteHostPath, label) {
  if (typeof value !== "string" || value.trim().length === 0
      || value.includes("\0") || !isAbsoluteHostPath(value)) {
    throw new TypeError(`${label} must be an absolute local path without NUL`);
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
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

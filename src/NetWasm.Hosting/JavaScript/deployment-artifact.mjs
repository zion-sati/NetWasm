const artifactKeys = ["mediaType", "relativePath", "role", "schemaVersion", "sha256"];
const tokenPattern = /^[a-z0-9][a-z0-9.-]*$/u;
const digestPattern = /^[0-9a-f]{64}$/u;

export function snapshotDeploymentArtifact(value) {
  assertExactDataObject(value, artifactKeys, "deployment artifact");
  validateRelativePath(value.relativePath);
  if (typeof value.role !== "string" || !tokenPattern.test(value.role)) {
    throw new TypeError("deployment artifact role is invalid");
  }
  if (typeof value.mediaType !== "string" || value.mediaType.length === 0
      || value.mediaType.trim() !== value.mediaType || !value.mediaType.includes("/")
      || containsControl(value.mediaType)) {
    throw new TypeError("deployment artifact media type is invalid");
  }
  if (typeof value.sha256 !== "string" || !digestPattern.test(value.sha256)) {
    throw new TypeError("deployment artifact digest is invalid");
  }
  if (value.schemaVersion !== null
      && (!Number.isInteger(value.schemaVersion) || value.schemaVersion <= 0)) {
    throw new TypeError("deployment artifact schema version is invalid");
  }
  return Object.freeze({
    relativePath: value.relativePath,
    role: value.role,
    mediaType: value.mediaType,
    sha256: value.sha256,
    schemaVersion: value.schemaVersion,
  });
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

function validateRelativePath(path) {
  if (typeof path !== "string" || path.length === 0 || path.trim() !== path
      || path.startsWith("/") || path.includes("\\") || path.includes(":")
      || containsControl(path)) {
    throw new TypeError("deployment artifact path is invalid");
  }
  const segments = path.split("/");
  if (segments.some(segment => segment.length === 0 || segment === "." || segment === "..")) {
    throw new TypeError("deployment artifact path is invalid");
  }
}

function containsControl(value) {
  for (const character of value) {
    if (/\p{Cc}/u.test(character)) return true;
  }
  return false;
}

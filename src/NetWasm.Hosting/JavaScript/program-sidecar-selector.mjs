import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";

const requestKeys = ["artifacts", "environment", "runtimeFeatures"];
const environmentKeys = ["name", "value"];
const featurePattern = /^[a-z0-9][a-z0-9.-]*$/u;

const symbolSidecars = Object.freeze({
  "type-symbols": "application/vnd.netwasm.type-symbols;version=1",
  "method-symbols": "application/vnd.netwasm.method-symbols;version=1",
});

// The program's sidecars to mount under /netwasm-sidecars (issue 225): its type-symbols and method-symbols files
// when the deployment has them, and its time zone data when a non-UTC TZ needs it; null when there are none.
export function selectProgramSidecars(request = {}) {
  assertExactDataObject(request, requestKeys, "program sidecar selection request");
  const hasLocalTime = validateRuntimeFeatures(request.runtimeFeatures);
  const { timeZoneArtifact, symbols } = validateArtifacts(request.artifacts, hasLocalTime);
  const timeZone = selectExplicitTimeZone(request.environment);

  if (timeZone === "") {
    throw new TypeError("TZ must not be empty");
  }
  const needsTimeZone = hasLocalTime && timeZone !== undefined && timeZone !== "UTC" && timeZone !== "Etc/UTC";
  if (needsTimeZone && timeZoneArtifact === null) {
    throw new TypeError("a non-UTC TZ requires the manifested timezone sidecar");
  }
  const artifacts = needsTimeZone ? [...symbols, timeZoneArtifact] : symbols;
  return artifacts.length === 0
    ? null
    : Object.freeze({ timeZone: needsTimeZone ? timeZone : null, artifacts: Object.freeze(artifacts) });
}

function validateRuntimeFeatures(features) {
  if (!Array.isArray(features)) {
    throw new TypeError("deployment runtime features must be explicit");
  }

  const identities = new Set();
  for (const feature of features) {
    if (typeof feature !== "string" || !featurePattern.test(feature)) {
      throw new TypeError("deployment runtime feature is invalid");
    }
    if (feature !== "local-time") {
      throw new TypeError("deployment runtime feature is unsupported");
    }
    if (identities.has(feature)) {
      throw new TypeError("deployment runtime features must be unique");
    }
    identities.add(feature);
  }
  return identities.has("local-time");
}

function validateArtifacts(artifacts, hasLocalTime) {
  if (!Array.isArray(artifacts) || artifacts.length === 0) {
    throw new TypeError("deployment artifacts must contain one application");
  }

  const paths = new Set();
  let application = null;
  let applicationCount = 0;
  let timeZone = null;
  const symbols = [];
  for (const value of artifacts) {
    const artifact = snapshotDeploymentArtifact(value);
    if (paths.has(artifact.relativePath)) {
      throw new TypeError("deployment artifact paths must be unique");
    }
    paths.add(artifact.relativePath);

    if (artifact.role === "application") {
      application = artifact;
      applicationCount++;
    } else if (artifact.role === "timezone-data") {
      if (timeZone !== null) {
        throw new TypeError("a deployment may contain only one timezone artifact");
      }
      timeZone = artifact;
    } else if (Object.hasOwn(symbolSidecars, artifact.role)) {
      if (symbols.some(symbol => symbol.role === artifact.role)) {
        throw new TypeError(`a deployment may contain only one ${artifact.role} artifact`);
      }
      symbols.push(artifact);
    }
  }

  if (applicationCount !== 1) {
    throw new TypeError("a deployment must identify exactly one application artifact");
  }
  for (const symbol of symbols) {
    if (symbol.relativePath !== `${application.relativePath}.${symbol.role}`
        || symbol.mediaType !== symbolSidecars[symbol.role]
        || symbol.schemaVersion !== 1) {
      throw new TypeError(`the ${symbol.role} artifact must use the adjacent schema-1 deployment contract`);
    }
  }
  if (timeZone === null) {
    return Object.freeze({ timeZoneArtifact: null, symbols });
  }
  if (!hasLocalTime) {
    throw new TypeError("a timezone artifact requires the local-time runtime feature");
  }
  if (timeZone.relativePath !== `${application.relativePath}.tz-info`
      || timeZone.mediaType !== "application/octet-stream"
      || timeZone.schemaVersion !== 1) {
    throw new TypeError("the timezone artifact must use the adjacent schema-1 deployment contract");
  }
  return Object.freeze({ timeZoneArtifact: timeZone, symbols });
}

function selectExplicitTimeZone(environment) {
  if (!Array.isArray(environment)) {
    throw new TypeError("execution environment must be explicit");
  }

  const names = new Set();
  let timeZone;
  for (const entry of environment) {
    assertExactDataObject(entry, environmentKeys, "environment entry");
    if (typeof entry.name !== "string" || entry.name.length === 0
        || entry.name.includes("=") || entry.name.includes("\0")) {
      throw new TypeError("environment name is invalid");
    }
    if (typeof entry.value !== "string" || entry.value.includes("\0")) {
      throw new TypeError("environment value is invalid");
    }
    if (names.has(entry.name)) {
      throw new TypeError("environment names must be unique");
    }
    names.add(entry.name);
    if (entry.name === "TZ") timeZone = entry.value;
  }
  return timeZone;
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

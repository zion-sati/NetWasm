const requestKeys = [
  "applicationImports",
  "arguments",
  "buildFingerprint",
  "deploymentManifestSha256",
  "environment",
  "grants",
  "schemaVersion",
];
const environmentKeys = ["name", "value"];

export function overlayExecutionEnvironment(request, inheritedEnvironment) {
  assertExactDataObject(request, requestKeys, "validated execution request");
  if (!Object.isFrozen(request)
      || !Array.isArray(request.environment)
      || !Object.isFrozen(request.environment)
      || request.grants === null
      || typeof request.grants !== "object"
      || !Object.isFrozen(request.grants)
      || !Array.isArray(request.grants.environment)
      || !Object.isFrozen(request.grants.environment)) {
    throw new TypeError("validated execution request environment is not immutable");
  }
  if (!Array.isArray(inheritedEnvironment)) {
    throw new TypeError("inherited process environment must be an array");
  }

  const variables = new Map();
  for (const variable of inheritedEnvironment) {
    const snapshot = snapshotVariable(variable, "inherited environment variable");
    if (variables.has(snapshot.name)) {
      throw new TypeError(`inherited environment name '${snapshot.name}' is duplicated`);
    }
    variables.set(snapshot.name, snapshot);
  }
  for (const variable of request.environment) {
    const snapshot = snapshotVariable(variable, "execution environment variable");
    variables.set(snapshot.name, snapshot);
  }

  const environment = Object.freeze(
    [...variables.keys()].sort().map(name => variables.get(name)));
  const grantedNames = new Set(request.grants.environment);
  for (const { name } of environment) grantedNames.add(name);
  const grants = Object.freeze({
    ...request.grants,
    environment: Object.freeze([...grantedNames].sort()),
  });
  return Object.freeze({ ...request, environment, grants });
}

function snapshotVariable(variable, label) {
  assertExactDataObject(variable, environmentKeys, label);
  if (typeof variable.name !== "string" || variable.name.length === 0
      || variable.name.includes("=") || variable.name.includes("\0")) {
    throw new TypeError(`${label} name must be non-empty and contain no equals or NUL`);
  }
  if (typeof variable.value !== "string" || variable.value.includes("\0")) {
    throw new TypeError(`${label} value must be text without NUL`);
  }
  return Object.freeze({ name: variable.name, value: variable.value });
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

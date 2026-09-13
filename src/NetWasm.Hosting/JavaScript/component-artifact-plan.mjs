import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";

const supportedKinds = new Set(["component", "browser"]);
const requestKeys = ["artifacts", "deploymentKind"];
const roleMediaTypes = Object.freeze({
  application: "application/wasm",
  "component-adapter": "text/javascript",
  "component-javascript": "text/javascript",
  "component-core-module": "application/wasm",
});
const singletonRoles = Object.freeze([
  "application",
  "component-adapter",
  "component-javascript",
]);

export function createComponentArtifactPlan(request = {}) {
  assertExactDataObject(request, requestKeys, "component artifact plan request");
  if (!supportedKinds.has(request.deploymentKind)) {
    throw new TypeError("component artifact deployment kind is unsupported");
  }
  if (!Array.isArray(request.artifacts) || request.artifacts.length === 0) {
    throw new TypeError("component deployment artifacts are required");
  }

  const paths = new Set();
  const byRole = new Map();
  for (const value of request.artifacts) {
    const artifact = snapshotDeploymentArtifact(value);
    if (paths.has(artifact.relativePath)) {
      throw new TypeError("component deployment artifact path is duplicated");
    }
    paths.add(artifact.relativePath);
    if (!Object.hasOwn(roleMediaTypes, artifact.role)) continue;
    if (artifact.mediaType !== roleMediaTypes[artifact.role]) {
      throw new TypeError(`component artifact '${artifact.role}' has an invalid media type`);
    }
    const values = byRole.get(artifact.role) ?? [];
    values.push(artifact);
    byRole.set(artifact.role, values);
  }

  for (const role of singletonRoles) {
    if (byRole.get(role)?.length !== 1) {
      throw new TypeError(`component artifact role '${role}' must occur exactly once`);
    }
  }
  const coreModules = byRole.get("component-core-module");
  if (coreModules === undefined || coreModules.length === 0) {
    throw new TypeError("component deployment requires at least one core module");
  }
  coreModules.sort(compareArtifacts);

  const coreModulesByName = new Map();
  for (const artifact of coreModules) {
    const name = finalSegment(artifact.relativePath);
    if (coreModulesByName.has(name)) {
      throw new TypeError(`component core-module name '${name}' is duplicated`);
    }
    coreModulesByName.set(name, artifact);
  }

  return Object.freeze({
    application: byRole.get("application")[0],
    adapter: byRole.get("component-adapter")[0],
    generatedModule: byRole.get("component-javascript")[0],
    coreModules: Object.freeze([...coreModules]),
    resolveCoreModule(name) {
      validateLeafName(name);
      const artifact = coreModulesByName.get(name);
      if (artifact === undefined) {
        throw new TypeError(`component core module '${name}' is unavailable`);
      }
      return artifact;
    },
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

function validateLeafName(name) {
  if (typeof name !== "string" || name.length === 0 || name.includes("/")
      || name.includes("\\") || name.includes(":") || name === "." || name === ".."
      || containsControl(name)) {
    throw new TypeError("component core-module lookup name is invalid");
  }
}

function finalSegment(path) {
  return path.slice(path.lastIndexOf("/") + 1);
}

function compareArtifacts(left, right) {
  if (left.relativePath < right.relativePath) return -1;
  return 1;
}

function containsControl(value) {
  for (const character of value) {
    if (/\p{Cc}/u.test(character)) return true;
  }
  return false;
}

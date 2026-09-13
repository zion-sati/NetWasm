import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";

const requestKeys = ["artifacts", "deploymentKind"];
const roleMediaTypes = Object.freeze({
  application: "application/wasm",
  "interop-manifest": "application/json",
  "raw-adapter": "text/javascript",
  "runtime-layout": "application/json",
});
const singletonRoles = Object.freeze(Object.keys(roleMediaTypes).sort());

export function createRawArtifactPlan(request = {}) {
  assertExactDataObject(request, requestKeys, "raw artifact plan request");
  if (request.deploymentKind !== "raw") {
    throw new TypeError("raw artifact deployment kind is unsupported");
  }
  if (!Array.isArray(request.artifacts) || request.artifacts.length === 0) {
    throw new TypeError("raw deployment artifacts are required");
  }

  const paths = new Set();
  const byRole = new Map();
  for (const value of request.artifacts) {
    const artifact = snapshotDeploymentArtifact(value);
    if (paths.has(artifact.relativePath)) {
      throw new TypeError("raw deployment artifact path is duplicated");
    }
    paths.add(artifact.relativePath);
    if (!Object.hasOwn(roleMediaTypes, artifact.role)) continue;
    if (artifact.mediaType !== roleMediaTypes[artifact.role]) {
      throw new TypeError(`raw artifact '${artifact.role}' has an invalid media type`);
    }
    const values = byRole.get(artifact.role) ?? [];
    values.push(artifact);
    byRole.set(artifact.role, values);
  }

  for (const role of singletonRoles) {
    if (byRole.get(role)?.length !== 1) {
      throw new TypeError(`raw artifact role '${role}' must occur exactly once`);
    }
  }

  return Object.freeze({
    application: byRole.get("application")[0],
    adapter: byRole.get("raw-adapter")[0],
    interopManifest: byRole.get("interop-manifest")[0],
    runtimeLayout: byRole.get("runtime-layout")[0],
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

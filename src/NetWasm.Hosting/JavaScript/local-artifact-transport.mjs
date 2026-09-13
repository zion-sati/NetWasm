import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, resolve } from "node:path";

const factoryKeys = Object.freeze(["manifestPath"]);

export function createLocalArtifactTransport(options) {
  assertExactDataObject(options, factoryKeys, "local artifact transport options");
  const { manifestPath } = options;
  validateManifestPath(manifestPath);
  const deploymentRoot = dirname(manifestPath);

  return Object.freeze({
    readArtifact(artifact, signal) {
      return readFile(
        resolve(deploymentRoot, ...artifact.relativePath.split("/")),
        { signal: signal ?? undefined });
    },
    hashBytes(bytes) {
      return createHash("sha256").update(bytes).digest("hex");
    },
  });
}

function validateManifestPath(path) {
  if (typeof path !== "string" || path.length === 0 || path.includes("\0")
      || !isAbsolute(path) || resolve(path) !== path) {
    throw new TypeError("local deployment manifest path must be canonical and absolute");
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

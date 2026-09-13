import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { isAbsolute, resolve } from "node:path";

import { createDeploymentManifestReader } from "./deployment-manifest-reader.mjs";
import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";
import {
  createVerifiedDeploymentManifestLoader,
} from "./verified-deployment-manifest-loader.mjs";

const factoryKeys = ["manifestPath"];

export function createLocalDeploymentManifestLoader(options) {
  assertExactDataObject(options, factoryKeys, "local deployment manifest loader options");
  const { manifestPath } = options;
  if (typeof manifestPath !== "string" || manifestPath.length === 0
      || manifestPath.includes("\0") || !isAbsolute(manifestPath)
      || resolve(manifestPath) !== manifestPath) {
    throw new TypeError("local deployment manifest path must be canonical and absolute");
  }
  return createVerifiedDeploymentManifestLoader({
    readManifestBytes: signal => readFile(manifestPath, { signal: signal ?? undefined }),
    hashBytes: bytes => createHash("sha256").update(bytes).digest("hex"),
    readManifest: createDeploymentManifestReader({
      parseJson: parseStrictJson,
      validate: validateDeploymentManifest,
    }),
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
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

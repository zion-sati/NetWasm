import { createDeploymentManifestReader } from "./deployment-manifest-reader.mjs";
import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";
import {
  createVerifiedDeploymentManifestLoader,
} from "./verified-deployment-manifest-loader.mjs";

const factoryKeys = ["manifestUrl", "platform"];
const platformKeys = ["digest", "fetch"];

export function createBrowserDeploymentManifestLoader(options) {
  assertExactDataObject(options, factoryKeys, "browser deployment manifest loader options");
  const manifestUrl = validateManifestUrl(options.manifestUrl);
  assertExactDataObject(options.platform, platformKeys, "browser deployment manifest platform");
  for (const [name, value] of Object.entries(options.platform)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser deployment manifest platform '${name}' action is required`);
    }
  }
  return createVerifiedDeploymentManifestLoader({
    async readManifestBytes(signal) {
      const response = await options.platform.fetch(manifestUrl, { signal: signal ?? undefined });
      if (response === null || typeof response !== "object"
          || typeof response.ok !== "boolean" || typeof response.arrayBuffer !== "function") {
        throw new TypeError("browser deployment manifest fetch returned an invalid response");
      }
      if (!response.ok) throw new Error("browser deployment manifest fetch was unsuccessful");
      const buffer = await response.arrayBuffer();
      if (!(buffer instanceof ArrayBuffer)) {
        throw new TypeError("browser deployment manifest response returned invalid bytes");
      }
      return new Uint8Array(buffer);
    },
    async hashBytes(bytes) {
      const digest = await options.platform.digest("SHA-256", bytes);
      if (!(digest instanceof ArrayBuffer) || digest.byteLength !== 32) {
        throw new TypeError("browser deployment manifest digest is invalid");
      }
      return [...new Uint8Array(digest)]
        .map(byte => byte.toString(16).padStart(2, "0"))
        .join("");
    },
    readManifest: createDeploymentManifestReader({
      parseJson: parseStrictJson,
      validate: validateDeploymentManifest,
    }),
  });
}

function validateManifestUrl(value) {
  if (typeof value !== "string" || value.length === 0) {
    throw new TypeError("browser deployment manifest URL is required");
  }
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new TypeError("browser deployment manifest URL is invalid");
  }
  if (url.href !== value || url.protocol !== "https:" && url.protocol !== "http:"
      || url.username.length !== 0 || url.password.length !== 0
      || url.hash.length !== 0 || url.pathname.endsWith("/")) {
    throw new TypeError("browser deployment manifest URL must be canonical absolute HTTP(S)");
  }
  return url.href;
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

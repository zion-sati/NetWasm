const factoryKeys = ["hashBytes", "readManifest", "readManifestBytes"];
const requestKeys = ["expectedSha256", "signal"];
const digestPattern = /^[0-9a-f]{64}$/u;
const decoder = new TextDecoder("utf-8", { fatal: true });

export function createVerifiedDeploymentManifestLoader(options) {
  assertExactDataObject(options, factoryKeys, "verified deployment manifest loader options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`verified deployment manifest loader '${name}' action is required`);
    }
  }

  return Object.freeze(async function loadVerifiedDeploymentManifest(request) {
    assertExactDataObject(request, requestKeys, "verified deployment manifest load request");
    if (typeof request.expectedSha256 !== "string"
        || !digestPattern.test(request.expectedSha256)) {
      throw createContractError(
        "expected deployment manifest digest must be lowercase SHA-256");
    }
    validateSignal(request.signal);
    throwIfAborted(request.signal);
    const transportBytes = await options.readManifestBytes(request.signal);
    throwIfAborted(request.signal);
    if (!(transportBytes instanceof Uint8Array)) {
      throw new TypeError("deployment manifest transport returned invalid bytes");
    }
    const bytes = new Uint8Array(transportBytes);
    const digest = await options.hashBytes(new Uint8Array(bytes));
    throwIfAborted(request.signal);
    if (typeof digest !== "string" || !digestPattern.test(digest)) {
      throw new TypeError("deployment manifest hasher returned an invalid digest");
    }
    if (digest !== request.expectedSha256) {
      throw createContractError("deployment manifest failed integrity validation");
    }
    let text;
    try {
      text = decoder.decode(bytes);
    } catch {
      throw createContractError("deployment manifest is not valid UTF-8");
    }
    let manifest;
    try {
      manifest = options.readManifest(text);
    } catch {
      throw createContractError();
    }
    if (manifest === null || typeof manifest !== "object" || Array.isArray(manifest)
        || !Object.isFrozen(manifest)) {
      throw new TypeError("deployment manifest reader returned an invalid snapshot");
    }
    return manifest;
  });
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("deployment manifest signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Deployment manifest loading was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
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
import { createContractError } from "./contract-error.mjs";

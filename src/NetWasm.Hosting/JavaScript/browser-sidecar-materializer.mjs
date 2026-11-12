import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { materializeProgramSidecars } from "./program-sidecar-materializer.mjs";

const factoryKeys = ["manifestUrl", "platform"];
const platformKeys = ["digest", "fetch", "mountReadOnlyFile"];
const requestKeys = new Set(["selection", "signal"]);
const requiredRequestKeys = Object.freeze(["selection"]);

export function createBrowserSidecarMaterializer(options) {
  assertExactDataObject(options, factoryKeys, "browser sidecar materializer options");
  assertExactDataObject(options.platform, platformKeys, "browser sidecar platform");
  for (const key of platformKeys) {
    if (typeof options.platform[key] !== "function") {
      throw new TypeError(`browser sidecar platform '${key}' is required`);
    }
  }
  const transport = createBrowserArtifactTransport({
    manifestUrl: options.manifestUrl,
    fetch: options.platform.fetch,
    digest: options.platform.digest,
  });

  return Object.freeze(function materializeBrowserSidecars(request = {}) {
    assertRequest(request, "browser sidecar materialization request");
    return materializeProgramSidecars({
      selection: request.selection,
      signal: request.signal ?? null,
      readArtifact: transport.readArtifact,
      hashBytes: transport.hashBytes,
      mountReadOnlyFile: options.platform.mountReadOnlyFile,
    });
  });
}

function assertRequest(request, label) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
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

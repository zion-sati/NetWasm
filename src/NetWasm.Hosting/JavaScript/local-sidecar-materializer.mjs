import { createLocalArtifactTransport } from "./local-artifact-transport.mjs";
import { materializeProgramSidecars } from "./program-sidecar-materializer.mjs";

const factoryKeys = ["manifestPath", "mountReadOnlyFile"];
const requestKeys = new Set(["selection", "signal"]);
const requiredRequestKeys = Object.freeze(["selection"]);

export function createLocalSidecarMaterializer(options) {
  assertExactDataObject(options, factoryKeys, "local sidecar materializer options");
  if (typeof options.mountReadOnlyFile !== "function") {
    throw new TypeError("local sidecar read-only mount act is required");
  }
  const transport = createLocalArtifactTransport({ manifestPath: options.manifestPath });

  return Object.freeze(function materializeLocalSidecars(request = {}) {
    assertRequest(request, "local sidecar materialization request");
    return materializeProgramSidecars({
      selection: request.selection,
      signal: request.signal ?? null,
      readArtifact: transport.readArtifact,
      hashBytes: transport.hashBytes,
      mountReadOnlyFile: options.mountReadOnlyFile,
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

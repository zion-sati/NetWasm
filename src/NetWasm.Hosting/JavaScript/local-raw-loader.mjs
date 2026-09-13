import { createLocalArtifactTransport } from "./local-artifact-transport.mjs";
import { importLocalVerifiedModule } from "./local-verified-module-importer.mjs";
import { loadRawArtifacts } from "./raw-artifact-loader.mjs";

const factoryKeys = Object.freeze(["manifestPath"]);
const requestKeys = new Set(["artifacts", "signal"]);
const requiredRequestKeys = Object.freeze(["artifacts"]);

export function createLocalRawLoader(options) {
  assertExactDataObject(options, factoryKeys, "local raw loader options");
  const transport = createLocalArtifactTransport(options);

  return Object.freeze(function loadLocalRaw(request = {}) {
    assertRequest(request);
    const { artifacts, signal = null } = request;
    return loadRawArtifacts({
      deploymentKind: "raw",
      artifacts,
      signal,
      readArtifact: transport.readArtifact,
      hashBytes: transport.hashBytes,
      importModule: importLocalVerifiedModule,
      compileModule: bytes => WebAssembly.compile(bytes),
    });
  });
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("local raw loading request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("local raw loading request shape is invalid");
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

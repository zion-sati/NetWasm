import { loadComponentArtifacts } from "./component-artifact-loader.mjs";
import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { createBrowserVerifiedModuleImporter } from "./browser-verified-module-importer.mjs";

const platformKeys = [
  "compileCoreModule",
  "createModuleUrl",
  "digest",
  "fetch",
  "importModule",
  "revokeModuleUrl",
];
const factoryKeys = Object.freeze(["manifestUrl", "platform"]);
const requestKeys = new Set(["artifacts", "signal"]);
const requiredRequestKeys = Object.freeze(["artifacts"]);

export function createBrowserComponentLoader(options) {
  assertExactDataObject(options, factoryKeys, "browser component loader options");
  const { manifestUrl, platform } = options;
  assertExactDataObject(platform, platformKeys, "browser component platform");
  for (const key of platformKeys) {
    if (typeof platform[key] !== "function") {
      throw new TypeError(`browser component platform '${key}' is required`);
    }
  }
  const { createModuleUrl, revokeModuleUrl, importModule, compileCoreModule } = platform;
  const importVerifiedModule = createBrowserVerifiedModuleImporter({
    createModuleUrl,
    revokeModuleUrl,
    importModule,
  });
  const transport = createBrowserArtifactTransport({
    manifestUrl,
    fetch: platform.fetch,
    digest: platform.digest,
  });

  return Object.freeze(function loadBrowserComponent(request = {}) {
    assertRequest(request);
    const { artifacts, signal = null } = request;
    return loadComponentArtifacts({
      deploymentKind: "browser",
      artifacts,
      signal,
      readArtifact: transport.readArtifact,
      hashBytes: transport.hashBytes,
      importModule: importVerifiedModule,
      compileCoreModule,
    });
  });
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("browser component loading request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("browser component loading request shape is invalid");
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

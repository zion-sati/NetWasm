const factoryKeys = ["manifestUrl", "preview2", "web"];
const preview2Keys = ["createFilesystem", "createShim"];
const webKeys = [
  "compileCoreModule",
  "createModuleUrl",
  "digest",
  "fetch",
  "importModule",
  "revokeModuleUrl",
];

export function createBrowserBootstrapComposition(options, createExecution) {
  assertExactDataObject(options, factoryKeys, "browser bootstrap options");
  assertExactDataObject(options.preview2, preview2Keys, "browser bootstrap Preview 2 platform");
  assertExactDataObject(options.web, webKeys, "browser bootstrap Web platform");
  if (typeof createExecution !== "function") {
    throw new TypeError("browser bootstrap execution factory is required");
  }
  for (const [name, value] of Object.entries(options.preview2)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser bootstrap Preview 2 '${name}' action is required`);
    }
  }
  for (const [name, value] of Object.entries(options.web)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser bootstrap Web '${name}' action is required`);
    }
  }
  return createExecution({
    manifestUrl: options.manifestUrl,
    platform: Object.freeze({
      compileCoreModule: options.web.compileCoreModule,
      createFilesystem: options.preview2.createFilesystem,
      createModuleUrl: options.web.createModuleUrl,
      createShim: options.preview2.createShim,
      digest: options.web.digest,
      fetch: options.web.fetch,
      importModule: options.web.importModule,
      revokeModuleUrl: options.web.revokeModuleUrl,
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

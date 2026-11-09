const factoryKeys = ["manifestUrl", "wasi", "web"];
const wasiKeys = ["createFilesystem", "createImports"];
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
  assertExactDataObject(options.wasi, wasiKeys, "browser bootstrap WASI platform");
  assertExactDataObject(options.web, webKeys, "browser bootstrap Web platform");
  if (typeof createExecution !== "function") {
    throw new TypeError("browser bootstrap execution factory is required");
  }
  for (const [name, value] of Object.entries(options.wasi)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser bootstrap WASI '${name}' action is required`);
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
      createFilesystem: options.wasi.createFilesystem,
      createImports: options.wasi.createImports,
      createModuleUrl: options.web.createModuleUrl,
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

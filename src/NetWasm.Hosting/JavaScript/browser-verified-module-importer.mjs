const factoryKeys = ["createModuleUrl", "importModule", "revokeModuleUrl"];

export function createBrowserVerifiedModuleImporter(options) {
  assertExactDataObject(options, factoryKeys, "browser verified-module importer options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`browser verified-module importer '${name}' is required`);
    }
  }

  return Object.freeze(async function importBrowserVerifiedModule(bytes, artifact) {
    if (!(bytes instanceof Uint8Array)) {
      throw new TypeError("verified browser module bytes are required");
    }
    if (artifact === null || typeof artifact !== "object"
        || typeof artifact.mediaType !== "string" || artifact.mediaType.length === 0) {
      throw new TypeError("verified browser module artifact is required");
    }
    const url = options.createModuleUrl(bytes, artifact.mediaType);
    if (typeof url !== "string" || !url.startsWith("blob:")) {
      throw new TypeError("browser module URL factory returned an invalid URL");
    }
    try {
      return await options.importModule(url);
    } finally {
      options.revokeModuleUrl(url);
    }
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
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

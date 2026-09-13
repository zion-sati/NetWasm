import { platformProviderCatalog } from "./platform-provider-catalog.generated.mjs";
import {
  createPreview2PlatformProviderRegistrations,
} from "./preview2-platform-provider-registrations.mjs";
import { createPlatformProviderRegistry } from "./platform-provider-registry.mjs";

const factoryKeys = ["createShim", "validateProviderMetadata"];

export function createPreview2PlatformProviderRegistry(options) {
  assertExactDataObject(options, factoryKeys, "Preview 2 provider registry options");
  if (typeof options.createShim !== "function"
      || typeof options.validateProviderMetadata !== "function") {
    throw new TypeError(
      "Preview 2 shim creator and provider metadata validator are required");
  }
  return createPlatformProviderRegistry({
    registrations: createPreview2PlatformProviderRegistrations({
      catalog: platformProviderCatalog,
      createShim: options.createShim,
    }),
    validateProviderMetadata: options.validateProviderMetadata,
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

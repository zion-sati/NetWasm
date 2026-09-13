const factoryKeys = ["registrations", "validateProviderMetadata"];
const registrationKeys = ["createSource", "provider"];
const requestKeys = ["module"];
const platformProviderKeys = ["capability", "functions", "module"];

export function createPlatformProviderRegistry(options) {
  assertExactDataObject(options, factoryKeys, "platform provider registry options");
  if (!Array.isArray(options.registrations)) {
    throw new TypeError("platform provider registrations are required");
  }
  if (typeof options.validateProviderMetadata !== "function") {
    throw new TypeError("platform provider metadata validation action is required");
  }

  const registrations = new Map();
  for (const registration of options.registrations) {
    assertExactDataObject(registration, registrationKeys, "platform provider registration");
    if (typeof registration.createSource !== "function") {
      throw new TypeError("platform provider source factory is required");
    }
    const provider = options.validateProviderMetadata({
      kind: "platform",
      provider: registration.provider,
    });
    validateProviderProduct(provider);
    if (registrations.has(provider.module)) {
      throw new TypeError(`platform provider module '${provider.module}' is duplicated`);
    }
    registrations.set(provider.module, Object.freeze({
      createSource: registration.createSource,
      provider,
    }));
  }
  if (registrations.size === 0) {
    throw new TypeError("platform provider registry must not be empty");
  }

  return Object.freeze({
    resolve(request) {
      assertExactDataObject(request, requestKeys, "platform provider lookup request");
      if (typeof request.module !== "string" || request.module.length === 0) {
        throw new TypeError("platform provider module is required");
      }
      const registration = registrations.get(request.module);
      if (registration === undefined) {
        throw new TypeError(`platform provider module '${request.module}' is unsupported`);
      }
      return registration;
    },
  });
}

function validateProviderProduct(provider) {
  assertExactDataObject(provider, platformProviderKeys, "validated platform provider metadata");
  if (!Object.isFrozen(provider)
      || typeof provider.module !== "string" || provider.module.length === 0
      || typeof provider.capability !== "string" || provider.capability.length === 0
      || !Array.isArray(provider.functions) || !Object.isFrozen(provider.functions)
      || provider.functions.some(value => value === null || typeof value !== "object"
        || !Object.isFrozen(value))) {
    throw new TypeError("validated platform provider metadata is incomplete");
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

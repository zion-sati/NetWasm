const supportedKinds = new Set(["raw", "component", "browser"]);
const registrationKeys = ["execute", "kind"];

export function createArtifactStrategyRegistry(registrations) {
  if (!Array.isArray(registrations)) {
    throw new TypeError("artifact strategy registrations are required");
  }
  const strategies = new Map();
  for (const registration of registrations) {
    assertRegistration(registration);
    if (strategies.has(registration.kind)) {
      throw new TypeError(`artifact strategy '${registration.kind}' is duplicated`);
    }
    strategies.set(registration.kind, registration.execute);
  }
  if (strategies.size === 0) {
    throw new TypeError("artifact strategy registry must not be empty");
  }

  return Object.freeze({
    resolve(kind) {
      if (typeof kind !== "string" || kind.length === 0) {
        throw new TypeError("artifact strategy kind is required");
      }
      const strategy = strategies.get(kind);
      if (strategy === undefined) {
        throw new TypeError(`artifact strategy '${kind}' is unsupported`);
      }
      return strategy;
    },
  });
}

function assertRegistration(registration) {
  if (registration === null || typeof registration !== "object" || Array.isArray(registration)
      || Object.getOwnPropertySymbols(registration).length !== 0) {
    throw new TypeError("artifact strategy registration is invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(registration);
  const keys = Object.keys(descriptors).sort();
  if (keys.length !== registrationKeys.length
      || keys.some((key, index) => key !== registrationKeys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("artifact strategy registration shape is invalid");
  }
  if (typeof registration.kind !== "string" || registration.kind.length === 0
      || typeof registration.execute !== "function") {
    throw new TypeError("artifact strategy registration is incomplete");
  }
  if (!supportedKinds.has(registration.kind)) {
    throw new TypeError(`artifact strategy '${registration.kind}' is unsupported`);
  }
}

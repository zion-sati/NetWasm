const requestKeys = ["module"];

export function selectProviderKind(request) {
  assertExactDataObject(request, requestKeys, "provider kind selection request");
  if (typeof request.module !== "string" || request.module.length === 0) {
    throw new TypeError("provider module identity is required");
  }
  return request.module.startsWith("wasi:")
      || request.module.startsWith("netwasm:platform/")
    ? "platform"
    : "application";
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

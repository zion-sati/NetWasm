const requestKeys = ["module"];
const internalModules = new Set([
  "netwasm:runtime/reactor-host",
  "netwasm:runtime/reactor-host@1.0.0",
]);

export function isInternalImport(request) {
  assertExactDataObject(request, requestKeys, "internal import policy request");
  if (typeof request.module !== "string" || request.module.length === 0) {
    throw new TypeError("import module identity is required");
  }
  return internalModules.has(request.module);
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

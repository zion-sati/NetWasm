import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const requestKeys = ["type", "value"];

export function releaseRawProviderResource(request = {}) {
  assertExactObject(request, requestKeys, "raw provider-resource release");
  if (!Number.isSafeInteger(request.type) || request.type < 0) {
    throw new TypeError("raw provider-resource type is invalid");
  }
  if (request.value === null
      || typeof request.value !== "object" && typeof request.value !== "function") {
    throw new TypeError("raw provider resource is invalid");
  }
  const release = readInheritedDataProperty(request.value, resourceDisposeSymbol);
  if (release === undefined) return;
  if (typeof release !== "function") {
    throw new TypeError("raw provider-resource release hook is invalid");
  }
  release.call(request.value);
}

function readInheritedDataProperty(value, name) {
  let current = value;
  while (current !== null) {
    const descriptor = Object.getOwnPropertyDescriptor(current, name);
    if (descriptor !== undefined) {
      if (!("value" in descriptor)) {
        throw new TypeError("raw provider-resource release hook must be a data property");
      }
      return descriptor.value;
    }
    current = Object.getPrototypeOf(current);
  }
  return undefined;
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

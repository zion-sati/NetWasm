const keys = ["message", "stackTrace", "typeId", "typeName"];

// A clone-safe value shared by local export errors and worker transport.
export function readManagedExceptionDetails(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("managed exception details are invalid");
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actual = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actual.length !== keys.length || actual.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))
      || !Number.isSafeInteger(value.typeId) || value.typeId <= 0 || value.typeId > 0x7fffffff
      || value.typeName !== null && (typeof value.typeName !== "string" || value.typeName.length === 0)
      || value.message !== null && typeof value.message !== "string"
      || value.stackTrace !== null && typeof value.stackTrace !== "string") {
    throw new TypeError("managed exception details shape is invalid");
  }
  return Object.freeze({
    typeId: value.typeId, typeName: value.typeName,
    message: value.message, stackTrace: value.stackTrace,
  });
}

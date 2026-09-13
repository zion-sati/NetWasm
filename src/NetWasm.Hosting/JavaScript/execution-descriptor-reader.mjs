const readerKeys = ["parseJson", "validate"];

export function createExecutionDescriptorReader(options) {
  assertExactDataObject(options, readerKeys, "execution descriptor reader options");
  if (typeof options.parseJson !== "function") {
    throw new TypeError("execution descriptor JSON reader is required");
  }
  if (typeof options.validate !== "function") {
    throw new TypeError("execution descriptor validation action is required");
  }
  return Object.freeze(function readExecutionDescriptor(text) {
    return options.validate(options.parseJson(text));
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

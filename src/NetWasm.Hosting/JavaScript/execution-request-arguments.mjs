const requestKeys = [
  "applicationImports",
  "arguments",
  "buildFingerprint",
  "deploymentManifestSha256",
  "environment",
  "grants",
  "schemaVersion",
];

export function appendExecutionArguments(request, additionalArguments) {
  assertExactDataObject(request, requestKeys, "validated execution request");
  if (!Object.isFrozen(request)
      || !Array.isArray(request.arguments)
      || !Object.isFrozen(request.arguments)) {
    throw new TypeError("validated execution request arguments are not immutable");
  }
  if (!Array.isArray(additionalArguments)) {
    throw new TypeError("execution arguments must be explicit");
  }
  const additional = additionalArguments.map(argument => {
    if (typeof argument !== "string" || argument.includes("\0")) {
      throw new TypeError("execution argument must be text without NUL");
    }
    return argument;
  });
  return Object.freeze({
    ...request,
    arguments: Object.freeze([...request.arguments, ...additional]),
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

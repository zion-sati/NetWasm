import { isCanonicalWitFunctionName } from "./canonical-wit-function-name.mjs";

const factoryKeys = ["selectProviderKind"];
const requestKeys = ["kind", "provider"];
const platformProviderKeys = ["capability", "functions", "module"];
const applicationProviderKeys = ["functions", "module"];
const functionKeys = ["interface", "name", "parameters", "results"];
const kinds = new Set(["application", "platform"]);
const capabilities = new Set([
  "baseline",
  "environment",
  "monotonicClock",
  "network",
  "preopenedDirectories",
  "randomness",
  "wallClock",
]);
const tokenPattern = /^[a-z0-9][a-z0-9.-]*$/u;
const versionNumber = "(?:0|[1-9][0-9]*)";
const modulePattern = new RegExp(
  `^[a-z0-9][a-z0-9.-]*:[a-z0-9][a-z0-9.-]*/`
  + `[a-z0-9][a-z0-9.-]*@${versionNumber}\.${versionNumber}\.${versionNumber}$`,
  "u");

export function createProviderMetadataValidator(options) {
  assertExactDataObject(options, factoryKeys, "provider metadata validator options");
  if (typeof options.selectProviderKind !== "function") {
    throw new TypeError("provider-kind selector is required");
  }

  return Object.freeze(function validateProviderMetadata(request) {
    assertExactDataObject(request, requestKeys, "provider metadata validation request");
    if (!kinds.has(request.kind)) {
      throw new TypeError("provider metadata kind is unsupported");
    }
    const keys = request.kind === "platform" ? platformProviderKeys : applicationProviderKeys;
    assertExactDataObject(request.provider, keys, `${request.kind} provider`);
    validateModule(request.provider.module);
    if (readProviderKind(options.selectProviderKind, request.provider.module) !== request.kind) {
      throw new TypeError(request.kind === "platform"
        ? "platform provider module must be reserved"
        : "application provider module must not be reserved");
    }
    if (request.kind === "platform" && !capabilities.has(request.provider.capability)) {
      throw new TypeError("platform provider capability is unsupported");
    }
    const functions = validateFunctions(request.provider.module, request.provider.functions);
    return request.kind === "platform"
      ? Object.freeze({
        module: request.provider.module,
        capability: request.provider.capability,
        functions,
      })
      : Object.freeze({ module: request.provider.module, functions });
  });
}

function validateFunctions(module, values) {
  if (!Array.isArray(values)) {
    throw new TypeError("selected provider functions must be explicit");
  }
  const names = new Set();
  return Object.freeze(values.map(value => {
    assertExactDataObject(value, functionKeys, "provider function");
    if (value.interface !== module) {
      throw new TypeError("provider function does not belong to its module");
    }
    if (!isCanonicalWitFunctionName(value.name)) {
      throw new TypeError("provider function name must use canonical WIT identity syntax");
    }
    if (names.has(value.name)) {
      throw new TypeError(`provider function '${value.name}' is duplicated`);
    }
    names.add(value.name);
    return Object.freeze({
      interface: value.interface,
      name: value.name,
      parameters: validateSignature(value.parameters),
      results: validateSignature(value.results),
    });
  }));
}

function validateSignature(value) {
  if (!Array.isArray(value)) {
    throw new TypeError("provider function signature must be explicit");
  }
  return Object.freeze(value.map(item => {
    if (typeof item !== "string" || item.length === 0 || item.trim() !== item
        || /[\u0000-\u001f\u007f]/u.test(item)) {
      throw new TypeError("provider function signature value must be canonical text");
    }
    return item;
  }));
}

function readProviderKind(selectProviderKind, module) {
  const kind = selectProviderKind({ module });
  if (!kinds.has(kind)) {
    throw new TypeError("provider-kind selector returned an unsupported kind");
  }
  return kind;
}

function validateModule(value) {
  if (typeof value !== "string" || !modulePattern.test(value)) {
    throw new TypeError("provider module must be a canonical exact versioned identifier");
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

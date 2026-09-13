import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = ["resolvePlatformProvider"];
const loadRequestKeys = ["binding", "filesystem", "signal", "stderr", "stdout"];
const bindingKeys = ["applicationProviders", "manifest", "platformProviders", "request"];
const executionRequestKeys = [
  "applicationImports",
  "arguments",
  "buildFingerprint",
  "deploymentManifestSha256",
  "environment",
  "grants",
  "schemaVersion",
];
const platformProviderKeys = ["capability", "functions", "module"];
const functionKeys = ["interface", "name", "parameters", "results"];
const registrationKeys = ["createSource", "provider"];

export function createPlatformProviderLoader(options) {
  assertExactDataObject(options, factoryKeys, "platform provider loader options");
  if (typeof options.resolvePlatformProvider !== "function") {
    throw new TypeError("platform provider resolver is required");
  }

  return Object.freeze(async function loadPlatformProviders(request) {
    assertExactDataObject(request, loadRequestKeys, "platform provider load request");
    validateSignal(request.signal);
    validateOutputSink(request.stdout, "platform provider stdout output sink");
    validateOutputSink(request.stderr, "platform provider stderr output sink");
    if (request.filesystem !== null
        && (typeof request.filesystem !== "object" || Array.isArray(request.filesystem))) {
      throw new TypeError("platform provider filesystem must be an object or null");
    }
    const selectedProviders = readBinding(request.binding);
    const factoryRequest = createFactoryRequest(
      request.binding.request,
      request.filesystem,
      request.signal,
      request.stderr,
      request.stdout);
    throwIfAborted(request.signal);

    const sources = [];
    for (const selectedProvider of selectedProviders) {
      const registration = options.resolvePlatformProvider({
        module: selectedProvider.module,
      });
      const createSource = readRegistration(registration, selectedProvider);
      throwIfAborted(request.signal);
      const source = await createSource(factoryRequest);
      if (source === null || typeof source !== "object" || Array.isArray(source)) {
        throw new TypeError(`platform provider module '${selectedProvider.module}' returned an invalid source`);
      }
      sources.push(Object.freeze({ module: selectedProvider.module, value: source }));
      throwIfAborted(request.signal);
    }
    return Object.freeze(sources);
  });
}

function readBinding(binding) {
  assertExactDataObject(binding, bindingKeys, "validated capability binding");
  if (!Object.isFrozen(binding)
      || !Array.isArray(binding.platformProviders)
      || !Object.isFrozen(binding.platformProviders)) {
    throw new TypeError("validated capability binding is incomplete");
  }
  assertExactDataObject(binding.request, executionRequestKeys, "validated execution request");
  if (!Object.isFrozen(binding.request)
      || !Array.isArray(binding.request.arguments) || !Object.isFrozen(binding.request.arguments)
      || !Array.isArray(binding.request.environment) || !Object.isFrozen(binding.request.environment)
      || binding.request.grants === null || typeof binding.request.grants !== "object"
      || !Object.isFrozen(binding.request.grants)) {
    throw new TypeError("validated execution request is incomplete");
  }

  const modules = new Set();
  return binding.platformProviders.map(provider => {
    readPlatformProvider(provider, "selected platform provider metadata");
    if (modules.has(provider.module)) {
      throw new TypeError(`selected platform provider module '${provider.module}' is duplicated`);
    }
    modules.add(provider.module);
    return provider;
  });
}

function createFactoryRequest(request, filesystem, signal, stderr, stdout) {
  return Object.freeze({
    arguments: request.arguments,
    environment: request.environment,
    filesystem,
    grants: request.grants,
    signal,
    stderr,
    stdout,
  });
}

function readRegistration(registration, selectedProvider) {
  assertExactDataObject(registration, registrationKeys, "resolved platform provider registration");
  if (!Object.isFrozen(registration) || typeof registration.createSource !== "function") {
    throw new TypeError("resolved platform provider registration is incomplete");
  }
  readPlatformProvider(registration.provider, "resolved platform provider metadata");
  if (!sameProvider(registration.provider, selectedProvider)) {
    throw new TypeError(`platform provider module '${selectedProvider.module}' metadata changed after validation`);
  }
  return registration.createSource;
}

function readPlatformProvider(provider, label) {
  assertExactDataObject(provider, platformProviderKeys, label);
  if (!Object.isFrozen(provider)
      || typeof provider.module !== "string" || provider.module.length === 0
      || typeof provider.capability !== "string" || provider.capability.length === 0
      || !Array.isArray(provider.functions) || !Object.isFrozen(provider.functions)) {
    throw new TypeError(`${label} is incomplete`);
  }
  for (const value of provider.functions) {
    assertExactDataObject(value, functionKeys, `${label} function`);
    if (!Object.isFrozen(value)
        || value.interface !== provider.module
        || typeof value.name !== "string" || value.name.length === 0
        || !Array.isArray(value.parameters) || !Object.isFrozen(value.parameters)
        || !Array.isArray(value.results) || !Object.isFrozen(value.results)) {
      throw new TypeError(`${label} function is incomplete`);
    }
  }
}

function sameProvider(left, right) {
  return left.module === right.module
    && left.capability === right.capability
    && left.functions.length === right.functions.length
    && left.functions.every((value, index) => {
      const other = right.functions[index];
      return value.interface === other.interface
        && value.name === other.name
        && sameStrings(value.parameters, other.parameters)
        && sameStrings(value.results, other.results);
    });
}

function sameStrings(left, right) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("platform provider signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (signal?.aborted) throw new Error("platform provider loading was cancelled");
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

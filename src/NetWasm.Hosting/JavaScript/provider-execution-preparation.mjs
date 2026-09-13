import { validateOutputSink } from "./output-sink-stream.mjs";

const factoryKeys = [
  "loadApplicationProviders",
  "loadPlatformProviders",
  "projectProviderModules",
  "selectProviderMetadata",
  "validateCapabilityBinding",
  "validateManifest",
  "validateRequest",
];
const requestKeys = ["filesystem", "manifest", "request", "signal", "stderr", "stdout"];
const metadataKeys = ["applicationProviders", "platformProviders"];
const bindingKeys = ["applicationProviders", "manifest", "platformProviders", "request"];
const projectionKeys = ["componentImports", "consumerModules", "rawProviders"];

export function createProviderExecutionPreparation(options) {
  assertExactDataObject(options, factoryKeys, "provider execution preparation options");
  for (const [name, value] of Object.entries(options)) {
    if (typeof value !== "function") {
      throw new TypeError(`provider execution preparation '${name}' action is required`);
    }
  }

  return Object.freeze(async function prepareProviderExecution(request) {
    assertExactDataObject(request, requestKeys, "provider execution preparation request");
    validateFilesystem(request.filesystem);
    validateSignal(request.signal);
    validateOutputSink(request.stdout, "provider execution stdout output sink");
    validateOutputSink(request.stderr, "provider execution stderr output sink");
    throwIfAborted(request.signal);

    const manifest = validateSnapshot(options.validateManifest(request.manifest), "deployment manifest");
    const executionRequest = validateSnapshot(
      options.validateRequest(request.request),
      "execution request");
    const metadata = options.selectProviderMetadata({
      manifest,
      request: executionRequest,
    });
    assertExactDataObject(metadata, metadataKeys, "selected provider metadata");
    const binding = options.validateCapabilityBinding({
      applicationProviders: metadata.applicationProviders,
      manifest,
      platformProviders: metadata.platformProviders,
      request: executionRequest,
    });
    assertExactDataObject(binding, bindingKeys, "validated capability binding");
    throwIfAborted(request.signal);

    const applicationSources = await options.loadApplicationProviders({
      binding,
      signal: request.signal,
    });
    validateSources(applicationSources, "application");
    throwIfAborted(request.signal);
    const platformSources = await options.loadPlatformProviders({
      binding,
      filesystem: request.filesystem,
      signal: request.signal,
      stderr: request.stderr,
      stdout: request.stdout,
    });
    validateSources(platformSources, "platform");
    throwIfAborted(request.signal);

    const projection = options.projectProviderModules({
      providers: [...binding.platformProviders, ...binding.applicationProviders],
      sources: [...platformSources, ...applicationSources],
    });
    assertExactDataObject(projection, projectionKeys, "projected provider modules");
    validateProjection(projection.componentImports, "component imports");
    validateProjection(projection.consumerModules, "consumer modules");
    validateProjection(projection.rawProviders, "raw providers");
    return Object.freeze({
      binding,
      componentImports: projection.componentImports,
      consumerModules: projection.consumerModules,
      rawProviders: projection.rawProviders,
    });
  });
}

function validateSources(value, kind) {
  if (!Array.isArray(value) || !Object.isFrozen(value)) {
    throw new TypeError(`${kind} provider sources must be an immutable array`);
  }
}

function validateSnapshot(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || !Object.isFrozen(value)) {
    throw new TypeError(`validated ${label} must be an immutable object`);
  }
  return value;
}

function validateProjection(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || !Object.isFrozen(value)) {
    throw new TypeError(`projected ${label} must be an immutable object`);
  }
}

function validateFilesystem(value) {
  if (value !== null && (typeof value !== "object" || Array.isArray(value))) {
    throw new TypeError("provider execution filesystem must be an object or null");
  }
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal !== "object"
      || typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("provider execution signal must be an AbortSignal");
  }
}

function throwIfAborted(signal) {
  if (!signal?.aborted) return;
  const error = new Error("Provider preparation was cancelled by the caller.");
  error.name = "AbortError";
  throw error;
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

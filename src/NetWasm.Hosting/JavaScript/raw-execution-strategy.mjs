import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { executeRaw } from "./raw-executor.mjs";

const requestKeys = new Set([
  "artifacts",
  "contractKey",
  "instantiate",
  "prepareInterop",
  "providers",
  "releaseActions",
  "schedule",
  "signal",
]);
const requiredRequestKeys = Object.freeze([
  "artifacts",
  "contractKey",
  "prepareInterop",
  "providers",
]);
const interopKeys = ["bindInstance", "close", "imports"];
const supportedContracts = new Set([
  commandExecutionContract,
  processExecutionContract,
]);

export function createRawExecutionStrategy(loadArtifacts) {
  if (typeof loadArtifacts !== "function") {
    throw new TypeError("raw artifact loading action is required");
  }

  return Object.freeze(async function executeRawDeployment(request = {}) {
    assertRequest(request);
    const {
      artifacts,
      contractKey,
      prepareInterop,
      providers,
      instantiate = instantiateModule,
      signal = null,
      releaseActions = [],
      schedule = globalThis.queueMicrotask?.bind(globalThis),
    } = request;
    if (typeof prepareInterop !== "function") {
      throw new TypeError("raw interop preparation action is required");
    }
    if (typeof instantiate !== "function") {
      throw new TypeError("raw module instantiation action is required");
    }
    if (!supportedContracts.has(contractKey)) {
      throw new TypeError("raw execution contract is unsupported");
    }
    validateSignal(signal);
    if (typeof schedule !== "function") {
      throw new TypeError("raw execution scheduler is required");
    }
    assertPlainDataObject(providers, "raw logical providers");
    const closeCallerScope = createExecutionScopeCloser(releaseActions);
    if (signal?.aborted) {
      return closeCallerScope(callerCancellation());
    }

    let loaded;
    try {
      const product = await loadArtifacts(Object.freeze({ artifacts, signal }));
      loaded = Object.freeze({
        abi: product.abi,
        adapter: product.adapter,
        interopManifest: product.interopManifest,
        module: product.module,
      });
    } catch {
      return closeCallerScope(signal?.aborted ? callerCancellation() : rawLoadFailure());
    }
    if (signal?.aborted) {
      return closeCallerScope(callerCancellation());
    }

    let interop;
    try {
      interop = readInteropPreparation(prepareInterop(Object.freeze({
        abi: loaded.abi,
        adapter: loaded.adapter,
        manifest: loaded.interopManifest,
      })));
    } catch {
      return closeCallerScope(rawInteropFailure());
    }
    const closeInteropScope = createExecutionScopeCloser([interop.releaseAction]);
    let outcome;
    try {
      outcome = await executeRaw({
        contractKey,
        abi: loaded.abi,
        adapter: loaded.adapter,
        module: loaded.module,
        providers,
        physicalProviders: interop.imports,
        instantiate: async instantiateRequest => {
          const result = await instantiate(instantiateRequest);
          const instance = result instanceof WebAssembly.Instance
            ? result
            : result?.instance;
          if (!(instance instanceof WebAssembly.Instance)) {
            throw new TypeError("raw module instantiator returned an invalid instance");
          }
          return instance;
        },
        bindInstance: interop.bindInstance,
        signal,
        instanceReleaseActions: [interop.releaseAction],
        releaseActions: [],
        schedule,
      });
    } catch {
      outcome = await closeInteropScope(rawExecutionFailure());
    }
    return closeCallerScope(outcome);
  });
}

function readInteropPreparation(value) {
  assertExactDataObject(value, interopKeys, "raw interop preparation");
  if (typeof value.bindInstance !== "function" || typeof value.close !== "function") {
    throw new TypeError("raw interop preparation actions are required");
  }
  assertPlainDataObject(value.imports, "raw interop imports");
  const close = value.close.bind(value);
  return Object.freeze({
    imports: value.imports,
    bindInstance: value.bindInstance.bind(value),
    releaseAction: Object.freeze({
      code: "host.interop-close",
      message: "The execution host could not close managed interop.",
      release: close,
    }),
  });
}

async function instantiateModule(request) {
  return WebAssembly.instantiate(request.module, request.imports);
}

function callerCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "instantiation",
    "caller.cancelled",
    "Raw execution was cancelled by the caller before guest entry.");
}

function rawLoadFailure() {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-load",
    "The execution host could not load the raw deployment artifacts.");
}

function rawInteropFailure() {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.interop-prepare",
    "The execution host could not prepare managed interop.");
}

function rawExecutionFailure() {
  return failedExecutionResult(
    "hostFailure",
    "execution",
    "host.raw-execution",
    "The execution host could not execute the loaded raw module.");
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("raw execution Strategy request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("raw execution Strategy request shape is invalid");
  }
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} are invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== null && prototype !== Object.prototype
      || Object.values(Object.getOwnPropertyDescriptors(value))
        .some(descriptor => !("value" in descriptor))) {
    throw new TypeError(`${label} must be a plain data object`);
  }
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("raw execution signal must be an AbortSignal");
  }
}

function assertExactDataObject(value, keys, label) {
  assertPlainDataObject(value, label);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable)) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";

const requestKeys = new Set([
  "contractKey",
  "imports",
  "instantiateCore",
  "releaseActions",
  "schedule",
  "signal",
]);
const requiredRequestKeys = Object.freeze(["contractKey", "imports"]);
const supportedContracts = new Set([
  commandExecutionContract,
  processExecutionContract,
]);
const reactorHostModule = "netwasm:runtime/reactor-host";
const versionedReactorHostModule = `${reactorHostModule}@1.0.0`;

export function prepareComponentExecution(request = {}) {
  assertRequest(request);
  const {
    contractKey,
    imports,
    instantiateCore,
    signal = null,
    releaseActions = [],
    schedule = globalThis.queueMicrotask?.bind(globalThis),
  } = request;
  if (!supportedContracts.has(contractKey)) {
    throw new TypeError("component execution contract is unsupported");
  }
  if (instantiateCore !== undefined && typeof instantiateCore !== "function") {
    throw new TypeError("component core-module instantiator is invalid");
  }
  validateSignal(signal);
  if (typeof schedule !== "function") {
    throw new TypeError("component execution scheduler is required");
  }
  const projectedImports = projectImports(imports);
  const closeCallerScope = createExecutionScopeCloser(releaseActions);

  return Object.freeze({
    contractKey,
    imports: projectedImports,
    instantiateCore,
    signal,
    schedule,
    closeCallerScope,
  });
}

function projectImports(imports) {
  if (imports === null || typeof imports !== "object" || Array.isArray(imports)) {
    throw new TypeError("projected component imports are required");
  }
  const prototype = Object.getPrototypeOf(imports);
  if (prototype !== null && prototype !== Object.prototype) {
    throw new TypeError("projected component imports must be a plain object");
  }
  if (Object.getOwnPropertySymbols(imports).length !== 0) {
    throw new TypeError("projected component imports cannot contain symbols");
  }

  const projection = Object.create(null);
  for (const [name, descriptor] of Object.entries(
    Object.getOwnPropertyDescriptors(imports))) {
    if (!descriptor.enumerable || !("value" in descriptor)
        || name.length === 0 || descriptor.value === null
        || typeof descriptor.value !== "object") {
      throw new TypeError("projected component import is invalid");
    }
    if (name === reactorHostModule || name === versionedReactorHostModule) {
      throw new TypeError("component reactor host is a reserved import");
    }
    projection[name] = descriptor.value;
  }
  return Object.freeze(projection);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("component signal must be an AbortSignal");
  }
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("component execution preparation request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("component execution preparation request shape is invalid");
  }
}

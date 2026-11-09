import { prepareComponentExecution } from "./component-execution-preparation.mjs";
import { executeComponent } from "./component-executor.mjs";
import { failedExecutionResult } from "./execution-result.mjs";

const requestKeys = new Set([
  "artifacts",
  "contractKey",
  "imports",
  "instantiateCore",
  "managedExceptionReporting",
  "releaseActions",
  "signal",
]);
const requiredRequestKeys = Object.freeze(["artifacts", "contractKey", "imports"]);

export function createComponentExecutionStrategy(loadArtifacts) {
  if (typeof loadArtifacts !== "function") {
    throw new TypeError("component artifact loading action is required");
  }

  return Object.freeze(async function executeComponentDeployment(request = {}) {
    assertRequest(request);
    const {
      artifacts,
      contractKey,
      imports,
      instantiateCore,
      managedExceptionReporting = {},
      signal = null,
      releaseActions = [],
    } = request;
    const prepared = prepareComponentExecution({
      contractKey,
      imports,
      instantiateCore,
      signal,
      releaseActions,
    });
    if (prepared.signal?.aborted) {
      return prepared.closeCallerScope(callerCancellation());
    }

    let loaded;
    try {
      loaded = await loadArtifacts(Object.freeze({
        artifacts,
        signal: prepared.signal,
      }));
    } catch (error) {
      return prepared.closeCallerScope(prepared.signal?.aborted
        ? callerCancellation()
        : componentLoadFailure(error));
    }

    let outcome;
    try {
      outcome = await executeComponent({
        contractKey: prepared.contractKey,
        adapter: loaded?.adapter,
        diagnosticArtifacts: loaded?.diagnosticArtifacts,
        imports: prepared.imports,
        loadCoreModule: loaded?.loadCoreModule,
        stackTraceSymbols: loaded?.stackTraceSymbols ?? [],
        instantiateCore: prepared.instantiateCore,
        managedExceptionReporting,
        signal: prepared.signal,
        releaseActions: [],
      });
    } catch (error) {
      outcome = componentExecutionFailure(error);
    }
    return prepared.closeCallerScope(outcome);
  });
}

function callerCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "instantiation",
    "caller.cancelled",
    "Component execution was cancelled by the caller before guest entry.");
}

function componentLoadFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.component-load",
    "The execution host could not load the component artifacts.",
    cause);
}

function componentExecutionFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "execution",
    "host.component-execution",
    "The execution host could not execute the loaded component.",
    cause);
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("component execution Strategy request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("component execution Strategy request shape is invalid");
  }
}

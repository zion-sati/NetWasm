import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { assertExecutionResult, failedExecutionResult } from "./execution-result.mjs";

const stages = Object.freeze({
  filesystem: Object.freeze({
    phase: "instantiation", code: "host.filesystem-prepare",
    message: "The execution host could not prepare the filesystem provider.",
  }),
  materialization: Object.freeze({
    phase: "instantiation", code: "host.timezone-materialize",
    message: "The execution host could not prepare the timezone sidecar.",
  }),
  execution: Object.freeze({
    phase: "execution", code: "host.deployment-execute",
    message: "The execution host could not execute the deployment.",
  }),
});
const requiredKeys = ["createFilesystem", "createMaterializer", "execute", "selection"];
const requestKeys = new Set([...requiredKeys, "releaseActions", "signal"]);

export async function executeWithTimeZoneResources(request) {
  assertRequest(request);
  const { createFilesystem, createMaterializer, execute, selection,
    releaseActions = [], signal = null } = request;
  for (const action of [createFilesystem, createMaterializer, execute]) {
    if (typeof action !== "function") throw new TypeError("timezone execution actions are required");
  }
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function" || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("timezone execution requires an AbortSignal");
  }
  const closeCaller = createExecutionScopeCloser(releaseActions);
  let closeFilesystem = null;
  let closeMount = null;
  let stage = stages.filesystem;
  let outcome;
  try {
    throwIfAborted(signal);
    const filesystem = createFilesystem();
    if (filesystem === null || typeof filesystem !== "object" || typeof filesystem.dispose !== "function") {
      throw new TypeError("the filesystem factory must return an owned provider");
    }
    closeFilesystem = createExecutionScopeCloser([{
      code: "host.filesystem-cleanup",
      message: "The execution host could not release the filesystem provider.",
      release: filesystem.dispose.bind(filesystem),
    }]);
    if (typeof filesystem.types?.Descriptor !== "function"
        || typeof filesystem.preopens?.getDirectories !== "function"
        || typeof filesystem.mountReadOnlyFile !== "function") {
      throw new TypeError("the filesystem factory returned an incomplete provider");
    }
    throwIfAborted(signal);
    stage = stages.materialization;
    const materialize = createMaterializer(filesystem.mountReadOnlyFile.bind(filesystem));
    if (typeof materialize !== "function") throw new TypeError("the materializer factory must return an action");
    throwIfAborted(signal);
    const mount = await materialize(Object.freeze({ selection, signal }));
    if (selection === null ? mount !== null : mount === null) {
      throw new TypeError("the materializer result does not match the sidecar selection");
    }
    if (mount !== null) closeMount = createExecutionScopeCloser([mount]);
    throwIfAborted(signal);
    stage = stages.execution;
    outcome = await execute(Object.freeze({ filesystem, signal }));
    assertExecutionResult(outcome);
  } catch {
    outcome = signal?.aborted
      ? failedExecutionResult("callerCancellation", stage.phase, "caller.cancelled",
        "Application execution was cancelled by the caller.")
      : failedExecutionResult("hostFailure", stage.phase, stage.code, stage.message);
  }
  if (closeMount !== null) outcome = await closeMount(outcome);
  if (closeFilesystem !== null) outcome = await closeFilesystem(outcome);
  return closeCaller(outcome);
}

function throwIfAborted(signal) {
  if (signal?.aborted) throw new Error("execution was cancelled");
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("timezone execution request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError("timezone execution request shape is invalid");
  }
}

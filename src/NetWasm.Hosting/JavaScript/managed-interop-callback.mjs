import {
  NetWasmHostError,
  NetWasmManagedError,
} from "./managed-errors.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";
import { liftResult, lowerArguments } from "./managed-interop-export.mjs";

const requestKeys = [
  "callbacks",
  "descriptor",
  "exceptionReporter",
  "getInstance",
  "getMemory",
  "handle",
  "handles",
  "parameterIndex",
  "target",
  "targetLayout",
];

// A callback is entered as a synchronous export is: it takes and returns the
// same values, lowered and lifted by the same code.
export function createManagedInteropCallback(request) {
  assertExactDataObject(request, requestKeys, "managed interop callback request");
  if (!Array.isArray(request.callbacks) || !Number.isInteger(request.parameterIndex)
      || request.parameterIndex < 0 || typeof request.getInstance !== "function") {
    throw new TypeError("managed interop callback selection is invalid");
  }
  if (request.handles === null || typeof request.handles !== "object"
      || typeof request.handles.acquire !== "function"
      || typeof request.handles.release !== "function") {
    throw new TypeError("managed interop callback handle registry is invalid");
  }
  if (request.exceptionReporter === null || typeof request.exceptionReporter !== "object"
      || typeof request.exceptionReporter.consumeTerminalEvent !== "function") {
    throw new TypeError("managed interop callback exception reporter is invalid");
  }
  const callback = findCallback(
    request.callbacks, request.descriptor, request.parameterIndex);
  const handle = normalizeInteropHandle(request.handle);
  if (handle === 0) return null;

  return Object.freeze((...arguments_) => {
    const instance = request.getInstance();
    const invoke = instance?.exports?.[callback.exportName];
    if (typeof invoke !== "function") {
      throw new NetWasmHostError(
        `managed callback ${callback.exportName} is unavailable`);
    }
    const transientHandles = [];
    const objectHandles = [];
    let invoked = false;
    let failure;
    try {
      const callbackArguments = lowerArguments(
        callback.parameters, arguments_, request.handles, transientHandles, objectHandles);
      invoked = true;
      const result = invoke(handle, ...callbackArguments);
      return callback.result === "void"
        ? undefined
        : liftResult(callback.result, result, request.getMemory, request.handles, request.target,
          request.targetLayout);
    } catch (cause) {
      if (cause instanceof WebAssembly.RuntimeError) {
        const terminalEvent = request.exceptionReporter.consumeTerminalEvent();
        if (terminalEvent !== null) {
          failure = new NetWasmManagedError(callback.exportName, {
            cause,
            managedType: terminalEvent.typeId,
          });
        } else {
          failure = cause;
        }
      } else {
        failure = new NetWasmManagedError(callback.exportName, { cause });
      }
    } finally {
      for (const transientHandle of transientHandles) {
        try { request.handles.release(transientHandle); } catch { }
      }
      // A JSObject argument's handle belongs to its managed wrapper once the call starts.
      if (!invoked) for (const objectHandle of objectHandles) request.handles.release(objectHandle);
    }
    throw failure;
  });
}

export function releaseManagedInteropCallback(request) {
  assertExactDataObject(request, ["getInstance", "handle"],
    "managed interop callback release request");
  if (typeof request.getInstance !== "function") {
    throw new TypeError("managed interop callback instance reader is invalid");
  }
  const release = request.getInstance()?.exports?.handle_release;
  if (typeof release !== "function") {
    throw new NetWasmHostError("managed callback handle release is unavailable");
  }
  release(normalizeInteropHandle(request.handle));
}

function findCallback(callbacks, descriptor, parameterIndex) {
  const callback = callbacks.find(candidate =>
    candidate.module === descriptor.module && candidate.importName === descriptor.name
    && candidate.parameterIndex === parameterIndex);
  if (callback === undefined) {
    throw new NetWasmHostError(
      `missing callback thunk for ${descriptor.module}.${descriptor.name}`);
  }
  return callback;
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

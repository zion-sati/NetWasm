import { isScalarAbiType } from "./interop-abi-types.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";
import {
  liftInteropScalar,
  lowerInteropScalar,
  writeInteropScalarResult,
} from "./interop-scalar-codec.mjs";
import {
  createManagedInteropCallback,
  releaseManagedInteropCallback,
} from "./managed-interop-callback.mjs";
import {
  projectManagedInteropMemoryOffset,
  readManagedInteropBytes,
  readManagedInteropString,
} from "./managed-interop-memory-codec.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

const requestKeys = [
  "assertAsyncDeliveryAvailable",
  "callbacks",
  "descriptor",
  "exceptionReporter",
  "getInstance",
  "getMemory",
  "handles",
  "observeAsyncFailure",
  "pendingAsyncOperations",
  "service",
  "statusAbi",
  "target",
  "targetLayout",
];

export function createInteropStatusService(
  request, observeAsyncCompletion = () => {}, isImportActive = () => false) {
  assertExactDataObject(request, requestKeys, "interop status-service request");
  if (typeof request.service !== "function" || typeof request.getMemory !== "function"
      || typeof request.getInstance !== "function"
      || typeof request.assertAsyncDeliveryAvailable !== "function"
      || typeof request.observeAsyncFailure !== "function"
      || typeof observeAsyncCompletion !== "function" || typeof isImportActive !== "function") {
    throw new TypeError("interop status-service actions are invalid");
  }
  if (!(request.pendingAsyncOperations instanceof Map)) {
    throw new TypeError("interop status-service pending-operation registry is invalid");
  }

  return Object.freeze((...argumentsWithDescriptor) => {
    const descriptorAddress = argumentsWithDescriptor.pop();
    try {
      const asyncHandle = request.descriptor.asyncReturn == null
        ? 0
        : normalizeInteropHandle(argumentsWithDescriptor.pop());
      const callbackHandles = [];
      const argumentsForService = argumentsWithDescriptor.map((value, index) =>
        liftArgument(request, value, index, callbackHandles, observeAsyncCompletion, isImportActive));
      let result;
      if (request.descriptor.asyncReturn != null) {
        return startAsyncOperation(request, asyncHandle, argumentsForService, observeAsyncCompletion);
      }
      if (request.descriptor.result === "promise") {
        const promiseCallbacks = argumentsForService.filter(
          (_, index) => request.descriptor.parameters[index] === "callback");
        if (promiseCallbacks.length !== 2) {
          throw new NetWasmHostError(
            "a Promise host service requires success and failure callbacks");
        }
        let active = true;
        Promise.resolve(request.service(...argumentsForService.filter(
          (_, index) => request.descriptor.parameters[index] !== "callback"))).then(
          value => {
            if (active) settleAsyncCompletion(request, () => promiseCallbacks[0](value), observeAsyncCompletion);
          },
          () => {
            if (active) settleAsyncCompletion(request, () => promiseCallbacks[1](), observeAsyncCompletion);
          });
        result = { dispose() { active = false; } };
      } else {
        result = request.service(...argumentsForService);
      }
      writeResult(request, result, descriptorAddress, callbackHandles);
      return request.statusAbi.successStatus;
    } catch {
      return request.statusAbi.hostFailureStatus;
    }
  });
}

function liftArgument(request, value, index, callbackHandles, observeAsyncCompletion, isImportActive) {
  const type = request.descriptor.parameters[index];
  if (type === "string") {
    return readManagedInteropString({
      reference: value,
      target: request.target,
      targetLayout: request.targetLayout,
      memory: request.getMemory(),
    });
  }
  if (type === "bytes") {
    return readManagedInteropBytes({
      reference: value,
      target: request.target,
      targetLayout: request.targetLayout,
      memory: request.getMemory(),
    });
  }
  if (type === "object" || type === "subscription") {
    return value === 0 ? null : request.handles.get(value);
  }
  if (type === "callback") {
    const handle = normalizeInteropHandle(value);
    callbackHandles.push(handle);
    const callback = createManagedInteropCallback({
      callbacks: request.callbacks,
      descriptor: request.descriptor,
      exceptionReporter: request.exceptionReporter,
      getInstance: request.getInstance,
      handle,
      handles: request.handles,
      parameterIndex: index,
    });
    // Promise callbacks already notify through settleAsyncCompletion. Ordinary
    // callbacks can also complete Main without a reactor wake. Observe only
    // after the callback releases its transient handles and the outer Wasm
    // stack has returned, including synchronous guest-to-host reentrancy.
    if (callback === null || request.descriptor.result === "promise") return callback;
    return Object.freeze((...arguments_) => {
      const external = !isImportActive();
      let failure;
      let result;
      try {
        if (external) request.assertAsyncDeliveryAvailable();
        result = callback(...arguments_);
      } catch (cause) {
        // Reentrant failures return through the active import's status ABI.
        // Managed code may recover; only external delivery can fail the host
        // observation independently of the managed process status.
        if (external) failure = cause === undefined ? Object.freeze({}) : cause;
        throw cause;
      } finally {
        queueMicrotask(() => {
          try { observeAsyncCompletion(failure); } catch { }
        });
      }
      return result;
    });
  }
  return liftInteropScalar({ type, value });
}

function startAsyncOperation(request, asyncHandle, argumentsForService, observeAsyncCompletion) {
  const instance = request.getInstance();
  const resolve = instance?.exports?.[request.descriptor.resolveExport];
  const reject = instance?.exports?.[request.descriptor.rejectExport];
  const cancel = instance?.exports?.[request.descriptor.cancelExport];
  if (typeof resolve !== "function" || typeof reject !== "function"
      || typeof cancel !== "function") {
    throw new NetWasmHostError(
      `managed async completion for ${request.descriptor.module}.${request.descriptor.name} is unavailable`);
  }
  const operation = {
    active: true,
    cancel() {
      if (!operation.active) return;
      operation.active = false;
      request.pendingAsyncOperations.delete(asyncHandle);
      cancel(asyncHandle);
    },
  };
  request.pendingAsyncOperations.set(asyncHandle, operation);
  let promise;
  try {
    promise = Promise.resolve(request.service(...argumentsForService));
  } catch (cause) {
    operation.active = false;
    request.pendingAsyncOperations.delete(asyncHandle);
    throw cause;
  }
  promise.then(
    value => {
      if (!operation.active) return;
      operation.active = false;
      request.pendingAsyncOperations.delete(asyncHandle);
      let normalized;
      try {
        normalized = request.descriptor.result === "void"
          ? undefined
          : lowerInteropScalar({ type: request.descriptor.result, value });
      } catch {
        settleAsyncCompletion(request, () => reject(asyncHandle), observeAsyncCompletion);
        return;
      }
      settleAsyncCompletion(request, () => {
        if (request.descriptor.result === "void") resolve(asyncHandle);
        else resolve(asyncHandle, normalized);
      }, observeAsyncCompletion);
    },
    () => {
      if (!operation.active) return;
      operation.active = false;
      request.pendingAsyncOperations.delete(asyncHandle);
      settleAsyncCompletion(request, () => reject(asyncHandle), observeAsyncCompletion);
    });
  return request.statusAbi.successStatus;
}

function settleAsyncCompletion(request, action, observeAsyncCompletion) {
  let failure;
  try {
    request.assertAsyncDeliveryAvailable();
    action();
  } catch (cause) {
    failure = cause === undefined ? Object.freeze({}) : cause;
    try {
      request.observeAsyncFailure(cause);
    } catch {
      // A failure observer is terminal notification plumbing. The original
      // completion failure is already contained and must not escape again.
    }
  }
  try { observeAsyncCompletion(failure); } catch {
    // Observation must not reject the already-settled service Promise.
  }
}

function writeResult(request, result, descriptorAddress, callbackHandles) {
  const { descriptor, handles, statusAbi, target } = request;
  if (descriptor.result === "void") return;
  const memory = request.getMemory();
  const byteOffset = projectManagedInteropMemoryOffset({
    address: descriptorAddress, target, memory,
  }) + statusAbi.scalarResultOffset;
  if (isScalarAbiType(descriptor.result)) {
    writeInteropScalarResult({
      memory,
      byteOffset,
      type: descriptor.result,
      value: lowerInteropScalar({ type: descriptor.result, value: result }),
    });
    return;
  }
  if (descriptor.result === "string") {
    if (result !== null && typeof result !== "string") {
      throw new NetWasmHostError("a string host service must return string or null");
    }
    writeHandle(handles, result === null ? 0 : handles.acquire(result), memory, byteOffset);
    return;
  }
  if (descriptor.result === "bytes") {
    if (result !== null && !(result instanceof Uint8Array)) {
      throw new NetWasmHostError(
        "a byte-array host service must return Uint8Array or null");
    }
    writeHandle(
      handles,
      result === null ? 0 : handles.acquire(result.slice()),
      memory,
      byteOffset);
    return;
  }
  if (descriptor.result === "object") {
    if (result !== null && !isHostObject(result)) {
      throw new NetWasmHostError(
        "a JSObject host service must return an object, function, or null");
    }
    writeHandle(handles, result === null ? 0 : handles.acquire(result), memory, byteOffset);
    return;
  }
  if (descriptor.result === "subscription" || descriptor.result === "promise") {
    if (!isHostObject(result) || typeof result.dispose !== "function") {
      throw new NetWasmHostError(
        "a subscription host service must return an object with dispose()");
    }
    const releaseManagedCallbacks = callbackHandles.map(handle => () =>
      releaseManagedInteropCallback({ getInstance: request.getInstance, handle }));
    writeHandle(
      handles,
      handles.acquireSubscription(result, releaseManagedCallbacks),
      memory,
      byteOffset);
    return;
  }
  throw new NetWasmHostError(`unsupported host-service result ${descriptor.result}`);
}

function writeHandle(_handles, handle, memory, byteOffset) {
  new DataView(memory.buffer).setInt32(byteOffset, handle, true);
}

function isHostObject(value) {
  return (typeof value === "object" && value !== null) || typeof value === "function";
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

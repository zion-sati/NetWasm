// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import {
  describeValueArray,
  isHandleValueAbiType,
  isHostObject,
  isMemoryViewAbiType,
  isScalarAbiType,
  isValueArrayAbiType,
  memoryViewConstructor,
  nullableRecord,
  nullableUnderlying,
  requireHandleValue,
  toValueArray,
  valueArrayConstructor,
} from "./interop-abi-types.mjs";
import { takeHandleValue } from "./managed-interop-export.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";
import {
  liftScalar,
  lowerScalar,
  writeScalarResult,
} from "./interop-scalar-codec.mjs";
import {
  createManagedInteropCallback,
  releaseManagedInteropCallback,
} from "./managed-interop-callback.mjs";
import {
  managedInteropMemoryOffset,
  readMemoryView,
  readString,
  readValueArray,
} from "./managed-interop-memory-codec.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";
import { createManagedCallbackLifetime } from "./managed-callback-lifetime.mjs";

// Where a staged number array result's length follows its block's address (the compiler's
// ByteArrayJavaScriptImportResultMarshaller.LengthOffset).
const stagedLengthOffset = 8;

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
  "staging",
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
  if (request.staging === null || typeof request.staging !== "object"
      || typeof request.staging.stage !== "function" || typeof request.staging.discard !== "function") {
    throw new TypeError("interop status-service number-array staging is invalid");
  }

  // Each parameter's value-array or memory-view type, found once rather than on every call.
  const arrayConstructors = request.descriptor.parameters.map(
    type => isValueArrayAbiType(type) ? valueArrayConstructor(type) : null);
  const viewConstructors = request.descriptor.parameters.map(
    type => isMemoryViewAbiType(type) ? memoryViewConstructor(type) : null);
  const nullables = request.descriptor.parameters.map(nullableUnderlying);
  // A subscription or Promise-style result owns its callbacks; any other import's callbacks belong to JavaScript.
  const lifetime = request.descriptor.result !== "subscription" && request.descriptor.result !== "promise"
      && request.descriptor.parameters.includes("callback")
    ? createManagedCallbackLifetime(handle => releaseManagedInteropCallback({ getInstance: request.getInstance, handle }))
    : null;
  return Object.freeze((...argumentsWithDescriptor) => {
    const descriptorAddress = argumentsWithDescriptor.pop();
    const adopted = [];
    let owned = [];
    try {
      const asyncHandle = request.descriptor.asyncReturn == null
        ? 0
        : normalizeInteropHandle(argumentsWithDescriptor.pop());
      if (lifetime !== null) owned = ownedCallbackHandles(request.descriptor.parameters, nullables, argumentsWithDescriptor);
      const callbackHandles = [];
      // A nullable argument arrives as two Wasm values, its presence and its value.
      const argumentsForService = [];
      for (let index = 0, position = 0; index < request.descriptor.parameters.length; index++) {
        const underlying = nullables[index];
        if (underlying !== null) {
          const present = argumentsWithDescriptor[position] !== 0;
          argumentsForService.push(present ? liftScalar(underlying, argumentsWithDescriptor[position + 1]) : null);
          position += 2;
          continue;
        }
        const argument = liftArgument(request, argumentsWithDescriptor[position++], index, callbackHandles,
          observeAsyncCompletion, isImportActive, arrayConstructors[index], viewConstructors[index]);
        if (lifetime !== null && argument !== null && request.descriptor.parameters[index] === "callback") {
          const callback = lifetime.adopt(callbackHandles.at(-1), argument);
          adopted.push(callback);
          argumentsForService.push(callback.function);
          continue;
        }
        argumentsForService.push(argument);
      }
      let result;
      if (request.descriptor.asyncReturn != null) {
        const status = startAsyncOperation(request, asyncHandle, argumentsForService, observeAsyncCompletion);
        for (const callback of adopted) callback.keep();
        return status;
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
      for (const callback of adopted) callback.keep();
      return request.statusAbi.successStatus;
    } catch {
      // The failed import's callbacks end with it.
      for (const callback of adopted) callback.drop();
      for (const handle of owned) lifetime.release(handle);
      return request.statusAbi.hostFailureStatus;
    }
  });
}

// The handles of the callbacks an import passes, read before any argument can fail: a nullable argument arrives as two
// Wasm values, and a null delegate as handle 0.
function ownedCallbackHandles(parameters, nullables, values) {
  const handles = [];
  for (let index = 0, position = 0; index < parameters.length; index++) {
    if (parameters[index] === "callback") {
      const handle = normalizeInteropHandle(values[position]);
      if (handle !== 0) handles.push(handle);
    }
    position += nullables[index] === null ? 1 : 2;
  }
  return handles;
}

function liftArgument(
  request, value, index, callbackHandles, observeAsyncCompletion, isImportActive, arrayConstructor,
  viewConstructor) {
  const type = request.descriptor.parameters[index];
  if (type === "string") {
    return readString(value, request.target, request.targetLayout, request.getMemory());
  }
  if (arrayConstructor !== null) {
    return readValueArray(value, request.target, request.targetLayout, request.getMemory(), arrayConstructor);
  }
  if (viewConstructor !== null) {
    return readMemoryView(value, request.target, request.targetLayout, request.getMemory(), viewConstructor);
  }
  if (type === "object" || type === "subscription") {
    return value === 0 ? null : request.handles.get(value);
  }
  if (isHandleValueAbiType(type)) return takeHandleValue(request.handles, value);
  if (type === "callback") {
    const handle = normalizeInteropHandle(value);
    callbackHandles.push(handle);
    const callback = createManagedInteropCallback({
      callbacks: request.callbacks,
      descriptor: request.descriptor,
      exceptionReporter: request.exceptionReporter,
      getInstance: request.getInstance,
      getMemory: request.getMemory,
      handle,
      handles: request.handles,
      parameterIndex: index,
      staging: request.staging,
      target: request.target,
      targetLayout: request.targetLayout,
    });
    // Promise callbacks already notify through settleAsyncCompletion. Ordinary
    // callbacks can also complete work the program awaits, which its event loop
    // then runs. Observe only
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
  return liftScalar(type, value);
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
      let resolution;
      try {
        resolution = prepareAsyncResolution(request, asyncHandle, resolve, value);
      } catch {
        settleAsyncCompletion(request, () => reject(asyncHandle), observeAsyncCompletion);
        return;
      }
      settleAsyncCompletion(request, resolution, observeAsyncCompletion);
    },
    () => {
      if (!operation.active) return;
      operation.active = false;
      request.pendingAsyncOperations.delete(asyncHandle);
      settleAsyncCompletion(request, () => reject(asyncHandle), observeAsyncCompletion);
    });
  return request.statusAbi.successStatus;
}

function prepareAsyncResolution(request, asyncHandle, resolve, value) {
  const { descriptor: { result }, handles } = request;
  if (result === "void") return () => resolve(asyncHandle);
  if (isScalarAbiType(result)) {
    const normalized = lowerScalar(result, value);
    return () => resolve(asyncHandle, normalized);
  }
  const underlying = nullableUnderlying(result);
  if (underlying !== null) {
    const present = value !== null && value !== undefined;
    const normalized = lowerScalar(underlying, present ? value : null);
    return () => resolve(asyncHandle, present ? 1 : 0, normalized);
  }
  if (isValueArrayAbiType(result)) {
    // The resolve export builds the array from the staged block and frees it.
    const [address, length] = request.staging.stage(result, transferResult(result, value));
    return () => resolve(asyncHandle, address, length);
  }
  const normalized = transferResult(result, value);
  // The resolve export copies a string before returning, so its handle ends here.
  // A JSObject wrapper owns its handle once resolve succeeds.
  return () => {
    const handle = normalized === null || normalized === undefined ? 0 : handles.acquire(normalized);
    let transferred = false;
    try {
      resolve(asyncHandle, handle);
      transferred = result === "object";
    } finally {
      if (handle !== 0 && !transferred) handles.release(handle);
    }
  };
}

// The value a string, JSObject or array result crosses as, as .NET's browser runtime reads it: undefined is null,
// and a plain array of numbers is copied into its typed array.
function transferResult(type, result) {
  if (isHandleValueAbiType(type)) {
    requireHandleValue(type, result);
    return result;
  }
  if (result === null || result === undefined) return null;
  if (type === "string" && typeof result !== "string") {
    throw new NetWasmHostError("a string host service must return string or null");
  }
  if (isValueArrayAbiType(type)) {
    const array = toValueArray(type, result);
    if (array === null) {
      throw new NetWasmHostError(type === "bytes"
        ? "a byte-array host service must return Uint8Array, an array, or null"
        : `a host service result: ${describeValueArray(type)}`);
    }
    return array;
  }
  if (type === "object" && !isHostObject(result)) {
    throw new NetWasmHostError(
      "a JSObject host service must return an object, function, or null");
  }
  return result;
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
  const byteOffset = managedInteropMemoryOffset(descriptorAddress, target, memory) + statusAbi.scalarResultOffset;
  if (isScalarAbiType(descriptor.result)) {
    writeScalarResult(memory, byteOffset, descriptor.result, lowerScalar(descriptor.result, result));
    return;
  }
  const underlying = nullableUnderlying(descriptor.result);
  if (underlying !== null) {
    const present = result !== null && result !== undefined;
    new DataView(memory.buffer).setInt32(byteOffset + nullableRecord.hasValueOffset, present ? 1 : 0, true);
    if (present) {
      writeScalarResult(memory, byteOffset + nullableRecord.valueOffset, underlying, lowerScalar(underlying, result));
    }
    return;
  }
  // Managed code converts a handle value and releases its handle.
  if (isHandleValueAbiType(descriptor.result)) {
    requireHandleValue(descriptor.result, result);
    writeHandle(handles, result === null || result === undefined ? 0 : handles.acquire(result), memory, byteOffset);
    return;
  }
  if (isValueArrayAbiType(descriptor.result)) {
    // The managed side reads the staged block's address, then the length 8 bytes after it, and frees the block.
    const [address, length] = request.staging.stage(descriptor.result, transferResult(descriptor.result, result));
    const view = new DataView(request.getMemory().buffer);
    if (target === "wasm64") view.setBigUint64(byteOffset, address, true);
    else view.setUint32(byteOffset, address, true);
    view.setInt32(byteOffset + stagedLengthOffset, length, true);
    return;
  }
  if (descriptor.result === "string" || descriptor.result === "object") {
    const transferred = transferResult(descriptor.result, result);
    writeHandle(handles, transferred === null ? 0 : handles.acquire(transferred), memory, byteOffset);
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

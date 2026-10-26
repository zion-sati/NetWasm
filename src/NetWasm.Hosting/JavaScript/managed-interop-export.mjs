import {
  NetWasmHostError,
  NetWasmManagedError,
} from "./managed-errors.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";
import {
  describeValueArray,
  isHostObject,
  isTransferAbiType,
  isValueArrayAbiType,
  valueArrayConstructor,
} from "./interop-abi-types.mjs";
import {
  readString,
  readValueArray,
} from "./managed-interop-memory-codec.mjs";
import {
  liftScalar,
  lowerScalar,
} from "./interop-scalar-codec.mjs";
import { observeManagedAsyncExport } from "./managed-async-export-observer.mjs";

const requestKeys = [
  "descriptor", "exceptionReporter", "getMemory", "handles", "instance", "target", "targetLayout",
];
const optionalRequestKeys = new Set([
  "assertAvailable", "consumeExceptionPayload", "enterExport", "observeAsyncExport",
]);

export function createManagedExports(instance, exportNames) {
  if (!(instance instanceof WebAssembly.Instance)) {
    throw new TypeError("instance must be a WebAssembly.Instance");
  }
  if (!Array.isArray(exportNames)) {
    throw new TypeError("exportNames must be an array");
  }
  const boundary = {};
  for (const name of exportNames) {
    if (typeof instance.exports[name] !== "function") {
      throw new TypeError(`managed export '${name}' is not a function`);
    }
    boundary[name] = createLowLevelManagedExport(instance, name);
  }
  return Object.freeze(boundary);
}

export function createManagedInteropExport(request) {
  assertExactDataObject(request, requestKeys, "managed interop export request");
  const {
    descriptor,
    exceptionReporter,
    getMemory,
    handles,
    instance,
    target,
    targetLayout,
    assertAvailable = () => {},
    observeAsyncExport = observeManagedAsyncExport,
    enterExport = () => () => {},
    consumeExceptionPayload,
  } = request;
  if (typeof assertAvailable !== "function" || typeof observeAsyncExport !== "function"
      || typeof enterExport !== "function") {
    throw new TypeError("managed interop export lifetime actions are invalid");
  }
  if (descriptor === null || typeof descriptor !== "object" || Array.isArray(descriptor)
      || !Array.isArray(descriptor.parameters) || typeof descriptor.name !== "string") {
    throw new TypeError("managed interop export descriptor is invalid");
  }
  if (descriptor.asyncReturn != null && typeof consumeExceptionPayload !== "function") {
    throw new TypeError("managed async export payload consumer is required");
  }
  if (instance === null || typeof instance !== "object" || Array.isArray(instance)
      || instance.exports === null || typeof instance.exports !== "object") {
    throw new TypeError("managed interop export instance is invalid");
  }
  if (exceptionReporter === null || typeof exceptionReporter !== "object"
      || typeof exceptionReporter.consumeTerminalEvent !== "function") {
    throw new TypeError("managed interop exception reporter is invalid");
  }
  if (typeof getMemory !== "function" || handles === null || typeof handles !== "object"
      || typeof handles.acquire !== "function" || typeof handles.release !== "function") {
    throw new TypeError("managed interop export memory services are invalid");
  }
  const invoke = createLowLevelManagedExport(instance, descriptor.name);
  return Object.freeze((...arguments_) => {
    assertAvailable();
    if (arguments_.length !== descriptor.parameters.length) {
      throw new NetWasmHostError(
        `managed export ${descriptor.name} received an invalid argument count`);
    }
    // A JavaScript import may call back into an export while managed frames are live.
    const leaveExport = enterExport();
    const argumentHandles = [];
    const objectHandles = [];
    let invoked = false;
    let result;
    try {
      const wasmArguments = arguments_.map((value, index) => lowerArgument(
        descriptor.parameters[index], value, handles, argumentHandles, objectHandles));
      invoked = true;
      const value = invoke(...wasmArguments);
      if (descriptor.asyncReturn != null) {
        const normalizedHandle = normalizeInteropHandle(value);
        result = observeAsyncExport({
          name: descriptor.name,
          readStatus: () => instance.exports[descriptor.statusExport](normalizedHandle),
          readResult: () => descriptor.result === "void" ? undefined : liftScalar(
            descriptor.result, instance.exports[descriptor.resultExport](normalizedHandle)),
          complete: () => consumeExceptionPayload(instance.exports[descriptor.completeExport](normalizedHandle)),
        });
      } else {
        result = descriptor.result === "void"
          ? undefined
          : liftResult(descriptor.result, value, getMemory, handles, target, targetLayout);
      }
    } catch (cause) {
      if (cause instanceof NetWasmHostError || cause instanceof NetWasmManagedError) {
        throw cause;
      }
      if (cause instanceof WebAssembly.RuntimeError) {
        const terminalEvent = exceptionReporter.consumeTerminalEvent();
        if (terminalEvent !== null) {
          instance.exports.exception_clear_active();
          throw new NetWasmManagedError(descriptor.name, {
            cause,
            managedType: terminalEvent.typeId,
          });
        }
        throw cause;
      }
      const managedFailure = new NetWasmManagedError(descriptor.name, { cause });
      if (descriptor.asyncReturn != null) result = Promise.reject(managedFailure);
      else throw managedFailure;
    } finally {
      for (const handle of argumentHandles) handles.release(handle);
      // A JSObject argument's handle belongs to its managed wrapper once the call starts.
      if (!invoked) for (const handle of objectHandles) handles.release(handle);
      leaveExport();
    }
    return result;
  });
}

function lowerArgument(type, value, handles, argumentHandles, objectHandles) {
  if (type === "object") {
    if (value === null) return 0;
    if (!isHostObject(value)) {
      throw new NetWasmHostError("JSObject value must be an object, function, or null");
    }
    const handle = handles.acquire(value);
    objectHandles.push(handle);
    return handle;
  }
  if (!isTransferAbiType(type)) return lowerScalar(type, value);
  if (value === null) return 0;
  if (type === "string" && typeof value !== "string") {
    throw new NetWasmHostError("string value must be a string or null");
  }
  if (isValueArrayAbiType(type) && !(value instanceof valueArrayConstructor(type))) {
    throw new NetWasmHostError(describeValueArray(type));
  }
  const handle = handles.acquire(value);
  argumentHandles.push(handle);
  return handle;
}

function liftResult(type, value, getMemory, handles, target, targetLayout) {
  // The managed JSObject keeps its handle; the caller only receives the object.
  if (type === "object") return value === 0 ? null : handles.get(value);
  if (!isTransferAbiType(type)) return liftScalar(type, value);
  return type === "string"
    ? readString(value, target, targetLayout, getMemory())
    : readValueArray(value, target, targetLayout, getMemory(), valueArrayConstructor(type));
}

function createLowLevelManagedExport(instance, name) {
  return (...arguments_) => {
    try {
      return instance.exports[name](...arguments_);
    } catch (cause) {
      const exception = instance.exports.exception_get_active();
      if (!(cause instanceof WebAssembly.Exception) || exception === 0) {
        throw cause;
      }
      let managedType;
      try {
        managedType = instance.exports.exception_get_active_type_id();
      } finally {
        instance.exports.exception_clear_active();
      }
      throw new NetWasmManagedError(name, { cause, managedType });
    }
  };
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (keys.some(key => !Object.hasOwn(descriptors, key))
      || actualKeys.some(key => !keys.includes(key) && !optionalRequestKeys.has(key))
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

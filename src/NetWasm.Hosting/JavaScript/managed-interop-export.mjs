import {
  NetWasmHostError,
  NetWasmManagedError,
} from "./managed-errors.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";
import {
  liftInteropScalar,
  lowerInteropScalar,
} from "./interop-scalar-codec.mjs";

const requestKeys = ["descriptor", "exceptionReporter", "instance"];

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
  const { descriptor, exceptionReporter, instance } = request;
  if (descriptor === null || typeof descriptor !== "object" || Array.isArray(descriptor)
      || !Array.isArray(descriptor.parameters) || typeof descriptor.name !== "string") {
    throw new TypeError("managed interop export descriptor is invalid");
  }
  if (instance === null || typeof instance !== "object" || Array.isArray(instance)
      || instance.exports === null || typeof instance.exports !== "object") {
    throw new TypeError("managed interop export instance is invalid");
  }
  if (exceptionReporter === null || typeof exceptionReporter !== "object"
      || typeof exceptionReporter.consumeTerminalEvent !== "function") {
    throw new TypeError("managed interop exception reporter is invalid");
  }
  const invoke = createLowLevelManagedExport(instance, descriptor.name);
  return Object.freeze((...arguments_) => {
    if (arguments_.length !== descriptor.parameters.length) {
      throw new NetWasmHostError(
        `managed export ${descriptor.name} received an invalid argument count`);
    }
    try {
      const wasmArguments = arguments_.map((value, index) => lowerInteropScalar({
        type: descriptor.parameters[index], value,
      }));
      if (descriptor.asyncReturn != null) {
        const handle = invoke(...wasmArguments);
        return observeManagedAsyncExport(
          descriptor, instance, normalizeInteropHandle(handle));
      }
      const result = invoke(...wasmArguments);
      return descriptor.result === "void"
        ? undefined
        : liftInteropScalar({ type: descriptor.result, value: result });
    } catch (cause) {
      if (cause instanceof NetWasmHostError || cause instanceof NetWasmManagedError) {
        throw cause;
      }
      if (cause instanceof WebAssembly.RuntimeError) {
        const terminalEvent = exceptionReporter.consumeTerminalEvent();
        if (terminalEvent !== null) {
          throw new NetWasmManagedError(descriptor.name, {
            cause,
            managedType: terminalEvent.typeId,
          });
        }
        throw cause;
      }
      const managedFailure = new NetWasmManagedError(descriptor.name, { cause });
      if (descriptor.asyncReturn != null) return Promise.reject(managedFailure);
      throw managedFailure;
    }
  });
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

function observeManagedAsyncExport(descriptor, instance, handle) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const finish = () => {
      try {
        const status = instance.exports[descriptor.statusExport](handle);
        if (status === 0) {
          globalThis.setTimeout(finish, 0);
          return;
        }
        settled = true;
        if (status === 1) {
          const value = descriptor.result === "void"
            ? undefined
            : liftInteropScalar({
                type: descriptor.result,
                value: instance.exports[descriptor.resultExport](handle),
              });
          resolve(value);
        } else if (status === 3) {
          reject(new DOMException("managed operation was canceled", "AbortError"));
        } else {
          reject(new NetWasmManagedError(descriptor.name));
        }
      } catch (cause) {
        settled = true;
        reject(cause instanceof WebAssembly.RuntimeError
          ? cause
          : new NetWasmManagedError(descriptor.name, { cause }));
      } finally {
        if (settled) instance.exports[descriptor.completeExport](handle);
      }
    };
    globalThis.queueMicrotask(finish);
  });
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

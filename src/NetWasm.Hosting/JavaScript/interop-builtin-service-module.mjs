import {
  writeManagedInteropBytes,
  writeManagedInteropString,
} from "./managed-interop-memory-codec.mjs";

const builtinModule = "netwasm.host.v1";
const requestKeys = [
  "builtinServices",
  "contract",
  "createStatusService",
  "exceptionReporter",
  "handles",
  "readMemory",
];

export function createInteropBuiltinServiceModule(request) {
  assertExactDataObject(request, requestKeys, "interop built-in service request");
  if (typeof request.createStatusService !== "function"
      || typeof request.readMemory !== "function") {
    throw new TypeError("interop built-in service actions are invalid");
  }
  const builtins = {
    ...request.builtinServices,
    ...request.exceptionReporter.importObject,
    interop_string_length(handle) {
      try {
        const value = request.handles.get(handle);
        return typeof value === "string" ? value.length : -1;
      } catch {
        return -1;
      }
    },
    interop_copy_string_utf16(handle, destination) {
      try {
        const value = request.handles.get(handle);
        if (typeof value !== "string") return 1;
        writeManagedInteropString({
          value,
          reference: destination,
          target: request.contract.target,
          targetLayout: request.contract.targetLayout,
          memory: request.readMemory(),
        });
        return 0;
      } catch {
        return 1;
      }
    },
    interop_release_handle(handle) {
      try { request.handles.release(handle); } catch { }
    },
    interop_release_subscription(handle) {
      request.handles.releaseSubscription(handle);
    },
    interop_byte_length(handle) {
      try {
        const value = request.handles.get(handle);
        return value instanceof Uint8Array ? value.byteLength : -1;
      } catch {
        return -1;
      }
    },
    interop_copy_bytes(handle, destination) {
      try {
        const value = request.handles.get(handle);
        if (!(value instanceof Uint8Array)) return 1;
        writeManagedInteropBytes({
          value,
          reference: destination,
          target: request.contract.target,
          targetLayout: request.contract.targetLayout,
          memory: request.readMemory(),
        });
        return 0;
      } catch {
        return 1;
      }
    },
  };
  const generatedServices = createGeneratedHostServices();
  for (const descriptor of request.contract.imports) {
    if (descriptor.module !== builtinModule) continue;
    const generatedService = generatedServices[descriptor.name];
    if (typeof generatedService === "function") {
      builtins[descriptor.name] = request.createStatusService(descriptor, generatedService);
    }
  }
  return builtins;
}

function createGeneratedHostServices() {
  return {
    queue_microtask(callback) {
      let active = true;
      globalThis.queueMicrotask(() => {
        if (active) callback();
      });
      return { dispose() { active = false; } };
    },
  };
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

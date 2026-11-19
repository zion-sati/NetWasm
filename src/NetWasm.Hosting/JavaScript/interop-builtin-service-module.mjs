// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { resolveGlobalJavaScriptImport } from "./javascript-import-resolver.mjs";
import { ManagedMemoryView } from "./managed-memory-view.mjs";
import {
  writeManagedInteropString,
} from "./managed-interop-memory-codec.mjs";

const builtinModule = "netwasm.host.v1";
const requestKeys = [
  "builtinServices",
  "consumerModules",
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
  };
  const generatedServices = {
    ...createGeneratedHostServices(request.consumerModules),
    ...createJavaScriptValueServices(request.handles, globalThis.FinalizationRegistry, request.readMemory),
  };
  for (const descriptor of request.contract.imports) {
    if (descriptor.module !== builtinModule) continue;
    // A module-less [JSImport] named "globalThis.<path>" is .NET's way to call a global function.
    const generatedService = generatedServices[descriptor.name] ?? resolveGlobalJavaScriptImport(descriptor.name);
    if (typeof generatedService === "function") {
      builtins[descriptor.name] = request.createStatusService(descriptor, generatedService);
    }
  }
  return builtins;
}

// JSObject's property access, as .NET's browser runtime serves it. Each typed getter or setter is its own import; the
// interop boundary converts the value to and from its type.
const getProperty = (self, name) => self[name];
const setProperty = (self, name, value) => {
  self[name] = value;
};

function createGeneratedHostServices(modules) {
  return {
    // JSHost: the global object, and a module the host bound for the program's imports, which JSHost.ImportAsync
    // completes with.
    host_global_this: () => globalThis,
    host_module: name => Object.hasOwn(modules, name) ? modules[name] : null,
    object_has_property: (self, name) => name in self,
    object_type_of_property: (self, name) => typeof self[name],
    object_get_boolean: getProperty,
    object_get_int32: getProperty,
    object_get_double: getProperty,
    object_get_string: getProperty,
    object_get_object: getProperty,
    object_get_bytes: getProperty,
    object_set_boolean: setProperty,
    object_set_int32: setProperty,
    object_set_double: setProperty,
    object_set_string: setProperty,
    object_set_object: setProperty,
    object_set_bytes: setProperty,
    queue_microtask(callback) {
      let active = true;
      globalThis.queueMicrotask(() => {
        if (active) callback();
      });
      return { dispose() { active = false; } };
    },
  };
}

// A managed object JavaScript has no counterpart for. It gives the same object
// back when it crosses to managed code again.
class ManagedObject {
  toString() { return "[object ManagedObject]"; }
}

// A managed exception crossing as a value. It gives the same exception back.
class ManagedError extends Error {
  constructor(message) {
    super(message);
    this.name = "ManagedError";
  }
}

// The JavaScript values CoreLib's JSMarshal converts, each held behind a host
// handle: value_kind's numbering is JSValueKind's. A handle 0 is null; the
// receiver of a handle releases it. Managed objects and exceptions keep their
// managed GC handle, which value_collected reports once JavaScript collects them;
// without a FinalizationRegistry they stay pinned.
export function createJavaScriptValueServices(handles, Registry = globalThis.FinalizationRegistry, readMemory = null) {
  const managed = new WeakMap();
  const collected = [];
  const registry = typeof Registry === "function"
    ? new Registry(handle => collected.push(handle))
    : null;
  const value = handle => handle === 0 ? null : handles.get(handle);
  const acquire = item => item === null || item === undefined ? 0 : handles.acquire(item);
  const track = (item, handle) => {
    managed.set(item, handle);
    registry?.register(item, handle);
    return handles.acquire(item);
  };
  return {
    value_kind(handle) {
      const item = value(handle);
      if (item === null || item === undefined) return 0;
      switch (typeof item) {
        case "boolean": return 1;
        case "number": return 2;
        case "bigint": return 3;
        case "string": return 4;
      }
      if (managed.has(item)) return item instanceof Error ? 9 : 8;
      if (item instanceof Date) return 5;
      if (item instanceof Error) return 6;
      if (Array.isArray(item)) return 7;
      if (item instanceof Uint8Array) return 10;
      if (item instanceof Int32Array) return 11;
      if (item instanceof Float64Array) return 12;
      return 13;
    },
    value_number(handle) {
      const item = value(handle);
      return item instanceof Date ? item.getTime() : Number(item);
    },
    value_bigint: handle => BigInt.asIntN(64, value(handle)),
    value_string: handle => String(value(handle)),
    value_message(handle) {
      const item = value(handle);
      return item instanceof Error ? String(item.message) : String(item);
    },
    value_bytes: value,
    value_int32s: value,
    value_doubles: value,
    value_object: value,
    value_length: handle => value(handle).length,
    value_element: (handle, index) => acquire(value(handle)[index]),
    value_managed: handle => managed.get(value(handle)),
    value_copy: handle => acquire(value(handle)),
    value_release(handle) {
      if (handle !== 0) handles.release(handle);
    },
    value_from_number: acquire,
    value_from_boolean: acquire,
    value_from_date: milliseconds => handles.acquire(new Date(milliseconds)),
    value_from_string: acquire,
    value_from_bytes: acquire,
    value_from_int32s: acquire,
    value_from_doubles: acquire,
    value_from_object: acquire,
    value_new_array: length => handles.acquire(new Array(length).fill(null)),
    value_set_element(array, index, element) {
      value(array)[index] = value(element);
      if (element !== 0) handles.release(element);
    },
    value_from_managed: handle => track(Object.freeze(new ManagedObject()), handle),
    // .NET's IMemoryView over an ArraySegment C# passed: the array's managed handle ends when JavaScript disposes or
    // collects the view.
    value_from_memory_view(handle, address, length, kind) {
      const view = new ManagedMemoryView(readMemory, address, length, kind, () => {
        registry?.unregister(view);
        collected.push(handle);
      });
      registry?.register(view, handle, view);
      return handles.acquire(view);
    },
    value_from_managed_error: (message, handle) => track(new ManagedError(message), handle),
    // Any order frees them; taking the last costs the same however many JavaScript collected.
    value_collected: () => collected.pop() ?? 0n,
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

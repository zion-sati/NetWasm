import { NetWasmHostError } from "./managed-errors.mjs";

const optionKeys = ["maximumHandle"];

export function createInteropHandleTable(options = { maximumHandle: 0xffff_ffff }) {
  assertExactDataObject(options, optionKeys, "interop handle table options");
  if (!Number.isInteger(options.maximumHandle)
      || options.maximumHandle < 1 || options.maximumHandle > 0xffff_ffff) {
    throw new TypeError("interop handle maximum must fit the nonzero i32 ABI space");
  }
  const entries = new Map();
  const released = [];
  let next = 1;
  let disposed = false;

  function allocate() {
    if (disposed) throw new NetWasmHostError("NetWasm handle table is unavailable");
    if (released.length !== 0) return released.pop();
    if (next > options.maximumHandle) {
      throw new NetWasmHostError("NetWasm handle table exhausted its i32 ABI space");
    }
    return next++;
  }

  return Object.freeze({
    acquire(value) {
      const handle = allocate();
      entries.set(handle, { value, subscription: false });
      return handle;
    },
    acquireSubscription(value, releaseManagedCallbacks) {
      const handle = allocate();
      entries.set(handle, { value, subscription: true, releaseManagedCallbacks });
      return handle;
    },
    get(handle) {
      const normalized = normalizeInteropHandle(handle);
      if (disposed || !entries.has(normalized)) {
        throw new NetWasmHostError(`stale NetWasm handle ${normalized}`);
      }
      return entries.get(normalized).value;
    },
    release(handle) {
      const normalized = normalizeInteropHandle(handle);
      if (disposed || !entries.delete(normalized)) {
        throw new NetWasmHostError(`stale NetWasm handle ${normalized}`);
      }
      released.push(normalized);
    },
    releaseSubscription(handle) {
      const normalized = normalizeInteropHandle(handle);
      if (disposed || !entries.has(normalized)) return;
      const entry = entries.get(normalized);
      entries.delete(normalized);
      released.push(normalized);
      disposeSubscription(entry);
    },
    dispose() {
      for (const entry of entries.values()) {
        if (entry.subscription) {
          try { disposeSubscription(entry); } catch { }
        }
      }
      entries.clear();
      released.length = 0;
      disposed = true;
    },
    get count() { return entries.size; },
  });
}

export function normalizeInteropHandle(handle) {
  if (!Number.isInteger(handle)) {
    throw new NetWasmHostError("NetWasm handle must be an i32");
  }
  return handle >>> 0;
}

function disposeSubscription(entry) {
  try {
    if (typeof entry.value.dispose === "function") entry.value.dispose();
  } finally {
    if (Array.isArray(entry.releaseManagedCallbacks)) {
      for (const release of entry.releaseManagedCallbacks) release();
      entry.releaseManagedCallbacks = undefined;
    }
  }
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

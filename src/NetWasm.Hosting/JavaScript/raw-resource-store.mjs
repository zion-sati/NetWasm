import {
  planRawResourceHandleAllocation,
} from "./raw-resource-handle-allocation-planner.mjs";

const stores = new WeakMap();
const signedHandleMinimum = -0x8000_0000;

export function createRawResourceStore() {
  const store = Object.freeze(Object.create(null));
  stores.set(store, {
    closed: false,
    tables: new Map(),
    ownership: new Set(),
  });
  return store;
}

export function registerRawResource(request = {}) {
  const state = readRequest(
    request,
    ["release", "store", "type", "value"],
    "raw resource registration");
  const type = readType(request.type);
  if (request.release !== null && typeof request.release !== "function") {
    throw new TypeError("raw resource release action is invalid");
  }
  const table = readTable(state, type, true);
  const handle = table.free.length === 0 ? allocateHandle(table) : table.free.pop();
  const entry = { handle, release: request.release, table, value: request.value };
  table.entries.set(handle, entry);
  state.ownership.add(entry);
  return handle;
}

export function borrowRawResource(request = {}) {
  const state = readRequest(request, ["handle", "store", "type"], "raw resource borrow");
  return readEntry(state, readType(request.type), request.handle).value;
}

export function transferRawResource(request = {}) {
  const state = readRequest(request, ["handle", "store", "type"], "raw resource transfer");
  return removeEntry(state, readType(request.type), request.handle).value;
}

export function dropRawResource(request = {}) {
  const state = readRequest(request, ["handle", "store", "type"], "raw resource drop");
  const entry = removeEntry(state, readType(request.type), request.handle);
  entry.release?.(entry.value);
}

export function closeRawResourceStore(request = {}) {
  const state = readRequest(request, ["store"], "raw resource-store close", true);
  state.closed = true;
  const failures = [];
  const owned = [...state.ownership].reverse();
  for (const entry of owned) {
    releaseEntry(state, entry);
    try {
      entry.release?.(entry.value);
    } catch (error) {
      failures.push(error);
    }
  }
  if (failures.length !== 0) {
    throw new AggregateError(failures, "raw resource-store close failed");
  }
}

function readRequest(request, keys, label, allowOpen = false) {
  assertExactObject(request, keys, label);
  const state = stores.get(request.store);
  if (state === undefined) {
    throw new TypeError(`${label} store is invalid`);
  }
  if (state.closed && !allowOpen) {
    throw new TypeError("raw resource store is closed");
  }
  if (state.closed && allowOpen) {
    throw new TypeError("raw resource store is already closed");
  }
  return state;
}

function readType(value) {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new TypeError("raw resource type is invalid");
  }
  return value;
}

function readTable(state, type, create = false) {
  let table = state.tables.get(type);
  if (table === undefined && create) {
    table = { entries: new Map(), free: [], nextHandle: 1 };
    state.tables.set(type, table);
  }
  return table;
}

function allocateHandle(table) {
  const allocation = planRawResourceHandleAllocation(table.nextHandle);
  table.nextHandle = allocation.nextHandle;
  return allocation.handle;
}

function normalizeHandle(value) {
  if (!Number.isInteger(value) || value < signedHandleMinimum || value > 0xffff_ffff) {
    throw new TypeError("raw resource handle is invalid");
  }
  const handle = value >>> 0;
  if (handle === 0) {
    throw new TypeError("raw resource handle zero is reserved");
  }
  return handle;
}

function readEntry(state, type, rawHandle) {
  const handle = normalizeHandle(rawHandle);
  const entry = readTable(state, type)?.entries.get(handle);
  if (entry === undefined) {
    throw new TypeError("raw resource handle is unavailable");
  }
  return entry;
}

function removeEntry(state, type, rawHandle) {
  const entry = readEntry(state, type, rawHandle);
  releaseEntry(state, entry);
  return entry;
}

function releaseEntry(state, entry) {
  entry.table.entries.delete(entry.handle);
  entry.table.free.push(entry.handle);
  state.ownership.delete(entry);
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

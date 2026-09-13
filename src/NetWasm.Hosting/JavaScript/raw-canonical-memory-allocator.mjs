import {
  projectRawCanonicalMemoryRange,
} from "./raw-canonical-memory-range-projector.mjs";

const factoryKeys = ["reallocate"];
const allocationKeys = ["alignment", "byteLength", "memory", "target"];
const deallocationKeys = ["address", "alignment", "byteLength", "memory", "target"];
const targets = Object.freeze(Object.assign(Object.create(null), {
  wasm32: Object.freeze({ maximum: 0xffff_ffff, zero: 0 }),
  wasm64: Object.freeze({ maximum: Number.MAX_SAFE_INTEGER, zero: 0n }),
}));

export function createRawCanonicalMemoryAllocator(request = {}) {
  const reallocate = readFactory(request, "raw canonical memory allocator");
  return Object.freeze(input => allocate(input, reallocate));
}

export function createRawCanonicalMemoryDeallocator(request = {}) {
  const reallocate = readFactory(request, "raw canonical memory deallocator");
  return Object.freeze(input => deallocate(input, reallocate));
}

function allocate(request, reallocate) {
  assertExactObject(request, allocationKeys, "raw canonical memory allocation");
  const target = readTarget(request.target);
  const byteLength = readSize(request.byteLength, target, "byte length");
  const alignment = readAlignment(request.alignment, target);
  validateMemory(request.memory, request.target);
  if (byteLength === 0) return target.zero;

  const address = reallocate(
    target.zero,
    target.zero,
    targetValue(alignment, request.target),
    targetValue(byteLength, request.target));
  projectRawCanonicalMemoryRange({
    address,
    alignment,
    byteLength,
    memory: request.memory,
    target: request.target,
  });
  return address;
}

function deallocate(request, reallocate) {
  assertExactObject(request, deallocationKeys, "raw canonical memory deallocation");
  const target = readTarget(request.target);
  const byteLength = readSize(request.byteLength, target, "byte length");
  const alignment = readAlignment(request.alignment, target);
  validateMemory(request.memory, request.target);
  if (byteLength === 0) return;

  projectRawCanonicalMemoryRange({
    address: request.address,
    alignment,
    byteLength,
    memory: request.memory,
    target: request.target,
  });
  reallocate(
    request.address,
    targetValue(byteLength, request.target),
    targetValue(alignment, request.target),
    target.zero);
}

function readFactory(request, label) {
  assertExactObject(request, factoryKeys, label);
  if (typeof request.reallocate !== "function") {
    throw new TypeError(`${label} reallocation capability is invalid`);
  }
  return request.reallocate;
}

function readTarget(value) {
  const target = typeof value === "string" && Object.hasOwn(targets, value)
    ? targets[value]
    : undefined;
  if (target === undefined) throw new TypeError("raw canonical memory target is unsupported");
  return target;
}

function readSize(value, target, label) {
  if (!Number.isSafeInteger(value) || value < 0 || value > target.maximum) {
    throw new TypeError(`raw canonical memory ${label} is invalid`);
  }
  return value;
}

function readAlignment(value, target) {
  const alignment = readSize(value, target, "alignment");
  if (alignment === 0 || !Number.isInteger(Math.log2(alignment))) {
    throw new TypeError("raw canonical memory alignment is invalid");
  }
  return alignment;
}

function validateMemory(memory, target) {
  projectRawCanonicalMemoryRange({
    address: target === "wasm64" ? 0n : 0,
    alignment: 1,
    byteLength: 0,
    memory,
    target,
  });
}

function targetValue(value, target) {
  return target === "wasm64" ? BigInt(value) : value;
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

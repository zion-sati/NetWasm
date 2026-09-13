import { NetWasmHostError } from "./managed-errors.mjs";

const readKeys = ["memory", "reference", "target", "targetLayout"];
const writeKeys = ["memory", "reference", "target", "targetLayout", "value"];
const offsetKeys = ["address", "memory", "target"];

export function projectManagedInteropMemoryOffset(request) {
  assertExactDataObject(request, offsetKeys, "managed interop memory-offset projection");
  return toMemoryOffset(request.address, request.target, request.memory);
}

export function readManagedInteropString(request) {
  assertExactDataObject(request, readKeys, "managed interop string read");
  if (isNullReference(request.reference, request.target)) return null;
  const address = toMemoryOffset(request.reference, request.target, request.memory);
  const view = new DataView(request.memory.buffer);
  const length = view.getInt32(address + request.targetLayout.stringLengthOffset, true);
  const end = address + request.targetLayout.stringDataOffset + length * 2;
  if (length < 0 || !Number.isSafeInteger(end) || end > request.memory.buffer.byteLength) {
    throw new NetWasmHostError("managed string is outside linear memory");
  }
  const chunks = [];
  for (let start = 0; start < length; start += 8192) {
    const limit = Math.min(start + 8192, length);
    const codeUnits = [];
    for (let index = start; index < limit; index++) {
      codeUnits.push(view.getUint16(
        address + request.targetLayout.stringDataOffset + index * 2,
        true));
    }
    chunks.push(String.fromCharCode(...codeUnits));
  }
  return chunks.join("");
}

export function writeManagedInteropString(request) {
  assertExactDataObject(request, writeKeys, "managed interop string write");
  if (typeof request.value !== "string") {
    throw new TypeError("managed interop string value is invalid");
  }
  const address = toMemoryOffset(request.reference, request.target, request.memory);
  const view = new DataView(request.memory.buffer);
  const length = view.getInt32(address + request.targetLayout.stringLengthOffset, true);
  const end = address + request.targetLayout.stringDataOffset + length * 2;
  if (length !== request.value.length || !Number.isSafeInteger(end)
      || end > request.memory.buffer.byteLength) {
    throw new NetWasmHostError("managed string result destination is invalid");
  }
  for (let index = 0; index < length; index++) {
    view.setUint16(
      address + request.targetLayout.stringDataOffset + index * 2,
      request.value.charCodeAt(index),
      true);
  }
}

export function readManagedInteropBytes(request) {
  assertExactDataObject(request, readKeys, "managed interop byte-array read");
  if (isNullReference(request.reference, request.target)) return null;
  const { byteOffset, length } = readByteRange(request);
  return new Uint8Array(request.memory.buffer, byteOffset, length).slice();
}

export function writeManagedInteropBytes(request) {
  assertExactDataObject(request, writeKeys, "managed interop byte-array write");
  if (!(request.value instanceof Uint8Array)) {
    throw new TypeError("managed interop byte-array value is invalid");
  }
  const { byteOffset, length } = readByteRange(request);
  if (length !== request.value.byteLength) {
    throw new NetWasmHostError("managed byte-array result destination is invalid");
  }
  new Uint8Array(request.memory.buffer, byteOffset, length).set(request.value);
}

function readByteRange(request) {
  const address = toMemoryOffset(request.reference, request.target, request.memory);
  const view = new DataView(request.memory.buffer);
  const length = view.getInt32(address + request.targetLayout.arrayLengthOffset, true);
  const dataAddress = request.target === "wasm32"
    ? view.getUint32(address + request.targetLayout.arrayDataPointerOffset, true)
    : view.getBigUint64(address + request.targetLayout.arrayDataPointerOffset, true);
  const byteOffset = toMemoryOffset(dataAddress, request.target, request.memory);
  if (length < 0 || !Number.isSafeInteger(byteOffset + length)
      || byteOffset + length > request.memory.buffer.byteLength) {
    throw new NetWasmHostError("managed byte array is outside linear memory");
  }
  return { byteOffset, length };
}

function isNullReference(reference, target) {
  validateTarget(target);
  return target === "wasm32" ? reference === 0 : reference === 0n;
}

function toMemoryOffset(address, target, memory) {
  validateTarget(target);
  if (!(memory instanceof WebAssembly.Memory)) {
    throw new NetWasmHostError("host service ran before NetWasm memory was available");
  }
  const value = target === "wasm32" ? address : Number(address);
  if (!Number.isSafeInteger(value) || value < 0 || value + 4 > memory.buffer.byteLength) {
    throw new NetWasmHostError("host result descriptor is outside linear memory");
  }
  return value;
}

function validateTarget(target) {
  if (target !== "wasm32" && target !== "wasm64") {
    throw new TypeError("managed interop memory target is unsupported");
  }
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

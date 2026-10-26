import { NetWasmHostError } from "./managed-errors.mjs";

const readKeys = ["memory", "reference", "target", "targetLayout"];
const writeKeys = ["memory", "reference", "target", "targetLayout", "value"];
const offsetKeys = ["address", "memory", "target"];

// The request forms check an object a caller built. The runtime's own calls,
// one per JSImport or JSExport call, pass the values directly: checking an
// object the runtime has just built made most of a call's cost.
export function projectManagedInteropMemoryOffset(request) {
  assertExactDataObject(request, offsetKeys, "managed interop memory-offset projection");
  return toMemoryOffset(request.address, request.target, request.memory);
}

export function managedInteropMemoryOffset(address, target, memory) {
  return toMemoryOffset(address, target, memory);
}

export function readManagedInteropString(request) {
  assertExactDataObject(request, readKeys, "managed interop string read");
  return readString(request.reference, request.target, request.targetLayout, request.memory);
}

export function readString(reference, target, targetLayout, memory) {
  if (isNullReference(reference, target)) return null;
  const address = toMemoryOffset(reference, target, memory);
  const view = memoryView(memory);
  const length = view.getInt32(address + targetLayout.stringLengthOffset, true);
  const end = address + targetLayout.stringDataOffset + length * 2;
  if (length < 0 || !Number.isSafeInteger(end) || end > memory.buffer.byteLength) {
    throw new NetWasmHostError("managed string is outside linear memory");
  }
  const chunks = [];
  for (let start = 0; start < length; start += 8192) {
    const limit = Math.min(start + 8192, length);
    const codeUnits = [];
    for (let index = start; index < limit; index++) {
      codeUnits.push(view.getUint16(
        address + targetLayout.stringDataOffset + index * 2,
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
  return readManagedInteropValueArray(request, Uint8Array);
}

export function writeManagedInteropBytes(request) {
  assertExactDataObject(request, writeKeys, "managed interop byte-array write");
  if (!(request.value instanceof Uint8Array)) {
    throw new TypeError("managed interop byte-array value is invalid");
  }
  writeManagedInteropValueArray(request);
}

// Copies a managed array of primitive numbers out as a typed array.
export function readManagedInteropValueArray(request, constructor) {
  assertExactDataObject(request, readKeys, "managed interop value-array read");
  return readValueArray(request.reference, request.target, request.targetLayout, request.memory, constructor);
}

export function readValueArray(reference, target, targetLayout, memory, constructor) {
  if (typeof constructor?.BYTES_PER_ELEMENT !== "number") {
    throw new TypeError("managed interop value-array type is invalid");
  }
  if (isNullReference(reference, target)) return null;
  const { byteOffset, length } = readElementRange(
    { reference, target, targetLayout, memory }, constructor.BYTES_PER_ELEMENT);
  return new constructor(memory.buffer.slice(
    byteOffset, byteOffset + length * constructor.BYTES_PER_ELEMENT));
}

// A Span<T> argument: a typed array over the span's elements in linear memory,
// so nothing is copied and writes reach the guest. It is valid until the
// import returns or memory grows. `reference` is the span's own address.
export function readMemoryView(reference, target, targetLayout, memory, constructor) {
  if (typeof constructor?.BYTES_PER_ELEMENT !== "number") {
    throw new TypeError("managed interop memory-view type is invalid");
  }
  const address = toMemoryOffset(reference, target, memory);
  const view = memoryView(memory);
  const length = view.getInt32(address + targetLayout.spanLengthOffset, true);
  const dataAddress = target === "wasm32"
    ? view.getUint32(address + targetLayout.spanReferenceOffset, true)
    : Number(view.getBigUint64(address + targetLayout.spanReferenceOffset, true));
  if (length === 0) return new constructor(memory.buffer, 0, 0);
  const end = dataAddress + length * constructor.BYTES_PER_ELEMENT;
  if (length < 0 || !Number.isSafeInteger(end) || end > memory.buffer.byteLength) {
    throw new NetWasmHostError("managed span is outside linear memory");
  }
  if (dataAddress % constructor.BYTES_PER_ELEMENT !== 0) {
    throw new NetWasmHostError("managed span is not aligned to its element size");
  }
  return new constructor(memory.buffer, dataAddress, length);
}

// Copies a typed array into a managed array the guest allocated with its length.
export function writeManagedInteropValueArray(request) {
  assertExactDataObject(request, writeKeys, "managed interop value-array write");
  const value = request.value;
  if (!ArrayBuffer.isView(value) || value instanceof DataView) {
    throw new TypeError("managed interop value-array value is invalid");
  }
  const { byteOffset, length } = readElementRange(request, value.BYTES_PER_ELEMENT);
  if (length !== value.length) {
    throw new NetWasmHostError("managed array result destination is invalid");
  }
  new Uint8Array(request.memory.buffer, byteOffset, value.byteLength)
    .set(new Uint8Array(value.buffer, value.byteOffset, value.byteLength));
}

function readElementRange(request, elementSize) {
  const address = toMemoryOffset(request.reference, request.target, request.memory);
  const view = memoryView(request.memory);
  const length = view.getInt32(address + request.targetLayout.arrayLengthOffset, true);
  const dataAddress = request.target === "wasm32"
    ? view.getUint32(address + request.targetLayout.arrayDataPointerOffset, true)
    : view.getBigUint64(address + request.targetLayout.arrayDataPointerOffset, true);
  const byteOffset = toMemoryOffset(dataAddress, request.target, request.memory);
  const end = byteOffset + length * elementSize;
  if (length < 0 || !Number.isSafeInteger(end) || end > request.memory.buffer.byteLength) {
    throw new NetWasmHostError("managed array is outside linear memory");
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

// A view stays valid until memory grows and replaces its buffer.
let viewedBuffer;
let cachedView;

function memoryView(memory) {
  if (memory.buffer !== viewedBuffer) {
    viewedBuffer = memory.buffer;
    cachedView = new DataView(viewedBuffer);
  }
  return cachedView;
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

import { NetWasmHostError } from "./managed-errors.mjs";
// Arrays of primitive numbers cross the boundary as copied typed arrays;
// byte[] keeps its established "bytes" name.
const valueArrayConstructors = Object.freeze({
  bytes: Uint8Array,
  "i8[]": Int8Array,
  "i16[]": Int16Array,
  "u16[]": Uint16Array,
  "i32[]": Int32Array,
  "u32[]": Uint32Array,
  "i64[]": BigInt64Array,
  "u64[]": BigUint64Array,
  "f32[]": Float32Array,
  "f64[]": Float64Array,
});

// A Span<T> or ReadOnlySpan<T> import parameter is a typed array over linear
// memory, valid only during the call.
const memoryViewConstructors = Object.freeze({
  "u8-view": Uint8Array,
  "i8-view": Int8Array,
  "i16-view": Int16Array,
  "u16-view": Uint16Array,
  "i32-view": Int32Array,
  "u32-view": Uint32Array,
  "i64-view": BigInt64Array,
  "u64-view": BigUint64Array,
  "f32-view": Float32Array,
  "f64-view": Float64Array,
});

// A date crosses as its Unix milliseconds in an f64, a JavaScript Date on this side.
// An i52 is a long marked JSType.Number: a Wasm i64 that is a number here, exact
// within 2^53 - 1.
const scalarAbiTypes = new Set([
  "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
  "i64", "u64", "f32", "f64", "date", "i52",
]);

// A nullable scalar or date, "i32?", is the scalar or null here. A parameter crosses
// as two Wasm values, its presence and its value; a result as a record in memory
// with its presence as an i32 at 0 and its value at 8.
const nullableAbiTypes = new Set([...scalarAbiTypes].map(type => `${type}?`));

// An object, exception, or array of strings, JSObjects or objects: managed code
// converts it to and from a host handle to its JavaScript value, which whoever
// receives the handle releases.
const handleValueAbiTypes = new Set(["any", "error", "string[]", "object[]", "any[]"]);

const abiTypes = new Set([
  "void", ...scalarAbiTypes, ...nullableAbiTypes, ...handleValueAbiTypes, "string", "object",
  "subscription", "promise", "callback", ...Object.keys(valueArrayConstructors),
  ...Object.keys(memoryViewConstructors),
]);

export const nullableRecord = Object.freeze({ hasValueOffset: 0, valueOffset: 8 });

export function isAbiType(type) {
  return typeof type === "string" && abiTypes.has(type);
}

export function isScalarAbiType(type) {
  return typeof type === "string" && scalarAbiTypes.has(type);
}

export function isHandleValueAbiType(type) {
  return typeof type === "string" && handleValueAbiTypes.has(type);
}

// Checks a value entering managed code as a handle value. Any value is an object
// or an exception; an array's elements must suit its element type.
export function requireHandleValue(type, value) {
  if (value === null || value === undefined) return;
  const array = type.endsWith("[]");
  if (array && !Array.isArray(value)) {
    throw new NetWasmHostError(`${describeHandleValue(type)} value must be an array or null`);
  }
  if (type === "string[]" && !value.every(item => item == null || typeof item === "string")) {
    throw new NetWasmHostError("string[] value must hold strings or null");
  }
  if (type === "object[]" && !value.every(item => item == null || isHostObject(item))) {
    throw new NetWasmHostError("JSObject[] value must hold objects, functions, or null");
  }
}

function describeHandleValue(type) {
  return type === "object[]" ? "JSObject[]" : type === "any[]" ? "object[]" : type;
}

/** The scalar ABI type a nullable ABI type wraps, or null when it is not one. */
export function nullableUnderlying(type) {
  return typeof type === "string" && nullableAbiTypes.has(type) ? type.slice(0, -1) : null;
}

export function isValueArrayAbiType(type) {
  return typeof type === "string" && Object.hasOwn(valueArrayConstructors, type);
}

// Strings and value arrays are copied through a transient handle.
// A JSObject may wrap any JavaScript object or function.
export function isHostObject(value) {
  return (typeof value === "object" && value !== null) || typeof value === "function";
}

export function isTransferAbiType(type) {
  return type === "string" || isValueArrayAbiType(type);
}

export function valueArrayConstructor(type) {
  if (!isValueArrayAbiType(type)) {
    throw new TypeError(`interop ABI type '${type}' is not a value array`);
  }
  return valueArrayConstructors[type];
}

export function isMemoryViewAbiType(type) {
  return typeof type === "string" && Object.hasOwn(memoryViewConstructors, type);
}

export function memoryViewConstructor(type) {
  if (!isMemoryViewAbiType(type)) {
    throw new TypeError(`interop ABI type '${type}' is not a memory view`);
  }
  return memoryViewConstructors[type];
}

export function isValueArray(value) {
  return Object.values(valueArrayConstructors).some(constructor => value instanceof constructor);
}

// Names the JavaScript form a value-array parameter or result must take.
export function describeValueArray(type) {
  const constructor = valueArrayConstructor(type);
  return type === "bytes" ? "byte-array value must be Uint8Array or null"
    : `${type} value must be ${constructor.name} or null`;
}

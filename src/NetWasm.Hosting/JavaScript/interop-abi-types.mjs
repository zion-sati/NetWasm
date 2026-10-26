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

const abiTypes = new Set([
  "void", "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
  "i64", "u64", "f32", "f64", "string", "object",
  "subscription", "promise", "callback", ...Object.keys(valueArrayConstructors),
  ...Object.keys(memoryViewConstructors),
]);

const scalarAbiTypes = new Set([
  "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
  "i64", "u64", "f32", "f64",
]);

export function isAbiType(type) {
  return typeof type === "string" && abiTypes.has(type);
}

export function isScalarAbiType(type) {
  return typeof type === "string" && scalarAbiTypes.has(type);
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

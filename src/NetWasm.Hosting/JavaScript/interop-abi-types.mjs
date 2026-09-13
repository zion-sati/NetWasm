const abiTypes = new Set([
  "void", "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
  "i64", "u64", "f32", "f64", "string", "bytes", "object",
  "subscription", "promise", "callback",
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

import { NetWasmHostError } from "./managed-errors.mjs";

const valueKeys = ["type", "value"];
const writeKeys = ["byteOffset", "memory", "type", "value"];

// The request forms check an object a caller built. The runtime's own calls,
// one per JSImport or JSExport call, pass the values directly: checking an
// object the runtime has just built made most of a call's cost.
export function liftInteropScalar(request) {
  assertExactDataObject(request, valueKeys, "interop scalar lift");
  return liftScalar(request.type, request.value);
}

export function lowerInteropScalar(request) {
  assertExactDataObject(request, valueKeys, "interop scalar lower");
  return lowerScalar(request.type, request.value);
}

export function writeInteropScalarResult(request) {
  assertExactDataObject(request, writeKeys, "interop scalar result write");
  writeScalarResult(request.memory, request.byteOffset, request.type, request.value);
}

export function liftScalar(type, value) {
  switch (type) {
    case "bool": return value !== 0;
    case "i8": return (value << 24) >> 24;
    case "u8": return value & 0xff;
    case "char": return String.fromCharCode(value & 0xffff);
    case "i16": return (value << 16) >> 16;
    case "u16": return value & 0xffff;
    case "i32": return value | 0;
    case "u32": return value >>> 0;
    case "i64": return BigInt.asIntN(64, value);
    case "u64": return BigInt.asUintN(64, value);
    case "f32": return Math.fround(value);
    case "f64": return value;
    default: throw new NetWasmHostError(`unsupported scalar type ${type}`);
  }
}

export function lowerScalar(type, value) {
  const integer = (minimum, maximum) => {
    if (!Number.isInteger(value) || value < minimum || value > maximum) {
      throw new NetWasmHostError(`${type} value is outside its range`);
    }
    return value;
  };
  switch (type) {
    case "bool":
      if (typeof value !== "boolean") throw new NetWasmHostError("bool value must be a boolean");
      return value ? 1 : 0;
    case "i8": return integer(-0x80, 0x7f);
    case "u8": return integer(0, 0xff);
    case "char":
      if (typeof value !== "string" || value.length !== 1) {
        throw new NetWasmHostError("char value must be one UTF-16 code unit");
      }
      return value.charCodeAt(0);
    case "i16": return integer(-0x8000, 0x7fff);
    case "u16": return integer(0, 0xffff);
    case "i32": return integer(-0x8000_0000, 0x7fff_ffff);
    case "u32": return integer(0, 0xffff_ffff) >>> 0;
    case "i64":
      if (typeof value !== "bigint" || value < -0x8000_0000_0000_0000n
          || value > 0x7fff_ffff_ffff_ffffn) {
        throw new NetWasmHostError("i64 value is outside its range");
      }
      return BigInt.asIntN(64, value);
    case "u64":
      if (typeof value !== "bigint" || value < 0n || value > 0xffff_ffff_ffff_ffffn) {
        throw new NetWasmHostError("u64 value is outside its range");
      }
      return BigInt.asIntN(64, value);
    case "f32":
      if (typeof value !== "number") throw new NetWasmHostError("f32 value must be a number");
      return Math.fround(value);
    case "f64":
      if (typeof value !== "number") throw new NetWasmHostError("f64 value must be a number");
      return value;
    default: throw new NetWasmHostError(`unsupported scalar type ${type}`);
  }
}

export function writeScalarResult(memory, byteOffset, type, value) {
  if (!(memory instanceof WebAssembly.Memory)) {
    throw new NetWasmHostError("interop scalar result memory is unavailable");
  }
  const view = memoryView(memory);
  switch (type) {
    case "bool": case "i8": case "i16": case "i32":
      view.setInt32(byteOffset, value, true); break;
    case "u8": case "char": case "u16": case "u32":
      view.setUint32(byteOffset, value, true); break;
    case "i64": view.setBigInt64(byteOffset, value, true); break;
    case "u64": view.setBigUint64(byteOffset, BigInt.asUintN(64, value), true); break;
    case "f32": view.setFloat32(byteOffset, value, true); break;
    case "f64": view.setFloat64(byteOffset, value, true); break;
    default: throw new NetWasmHostError(`unsupported scalar result type ${type}`);
  }
}

// A view stays valid until memory grows and replaces its buffer.
let viewedBuffer;
let view;

function memoryView(memory) {
  if (memory.buffer !== viewedBuffer) {
    viewedBuffer = memory.buffer;
    view = new DataView(viewedBuffer);
  }
  return view;
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

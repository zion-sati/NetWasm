import {
  projectRawCanonicalMemoryRange,
} from "./raw-canonical-memory-range-projector.mjs";

const flatRequestKeys = ["kind", "value"];
const memoryRequestKeys = ["address", "kind", "memory", "target"];
const memoryWriteRequestKeys = ["address", "kind", "memory", "target", "value"];
const signedInt32Minimum = -0x8000_0000;
const signedInt32Maximum = 0x7fff_ffff;
const signedInt64Minimum = -0x8000_0000_0000_0000n;
const signedInt64Maximum = 0x7fff_ffff_ffff_ffffn;

const scalarCodecs = Object.freeze(Object.assign(Object.create(null), {
  bool: Object.freeze({
    size: 1,
    alignment: 1,
    lift: value => normalizeCoreInt32(value) !== 0,
    lower: value => {
      if (typeof value !== "boolean") throw invalidValue("bool");
      return value ? 1 : 0;
    },
    read: (view, index) => view.getUint8(index) !== 0,
    write: (view, index, value) => view.setUint8(index, value),
  }),
  s8: integerCodec(8, true, "getInt8", "setInt8"),
  u8: integerCodec(8, false, "getUint8", "setUint8"),
  s16: integerCodec(16, true, "getInt16", "setInt16"),
  u16: integerCodec(16, false, "getUint16", "setUint16"),
  s32: integerCodec(32, true, "getInt32", "setInt32"),
  u32: integerCodec(32, false, "getUint32", "setUint32"),
  s64: bigintCodec(true, "getBigInt64", "setBigInt64"),
  u64: bigintCodec(false, "getBigUint64", "setBigUint64"),
  f32: floatCodec(32, "getFloat32", "setFloat32"),
  f64: floatCodec(64, "getFloat64", "setFloat64"),
  character: Object.freeze({
    size: 4,
    alignment: 4,
    lift: value => codePointToCharacter(normalizeCoreInt32(value) >>> 0),
    lower: characterToCodePoint,
    read: (view, index) => codePointToCharacter(view.getUint32(index, true)),
    write: (view, index, value) => view.setUint32(index, value, true),
  }),
}));

export function liftRawCanonicalFlatScalar(request = {}) {
  assertExactObject(request, flatRequestKeys, "raw canonical flat-scalar lift");
  return readCodec(request.kind).lift(request.value);
}

export function lowerRawCanonicalFlatScalar(request = {}) {
  assertExactObject(request, flatRequestKeys, "raw canonical flat-scalar lower");
  return readCodec(request.kind).lower(request.value);
}

export function readRawCanonicalMemoryScalar(request = {}) {
  assertExactObject(request, memoryRequestKeys, "raw canonical memory-scalar read");
  const codec = readCodec(request.kind);
  const range = projectRawCanonicalMemoryRange({
    address: request.address,
    alignment: codec.alignment,
    byteLength: codec.size,
    memory: request.memory,
    target: request.target,
  });
  return codec.read(new DataView(range.buffer), range.index);
}

export function writeRawCanonicalMemoryScalar(request = {}) {
  assertExactObject(request, memoryWriteRequestKeys, "raw canonical memory-scalar write");
  const codec = readCodec(request.kind);
  const value = codec.lower(request.value);
  const range = projectRawCanonicalMemoryRange({
    address: request.address,
    alignment: codec.alignment,
    byteLength: codec.size,
    memory: request.memory,
    target: request.target,
  });
  codec.write(new DataView(range.buffer), range.index, value);
}

function integerCodec(bits, signed, read, write) {
  const size = bits / 8;
  const modulus = 2 ** bits;
  const sign = 2 ** (bits - 1);
  const minimum = signed ? -sign : 0;
  const maximum = signed ? sign - 1 : modulus - 1;
  return Object.freeze({
    size,
    alignment: size,
    lift(value) {
      const narrowed = (normalizeCoreInt32(value) >>> 0) % modulus;
      return signed && narrowed >= sign ? narrowed - modulus : narrowed;
    },
    lower(value) {
      if (!Number.isInteger(value) || value < minimum || value > maximum) {
        throw invalidValue(signed ? `s${bits}` : `u${bits}`);
      }
      return value;
    },
    read: (view, index) => view[read](index, true),
    write: (view, index, value) => view[write](index, value, true),
  });
}

function bigintCodec(signed, read, write) {
  const minimum = signed ? signedInt64Minimum : 0n;
  const maximum = signed ? signedInt64Maximum : 0xffff_ffff_ffff_ffffn;
  return Object.freeze({
    size: 8,
    alignment: 8,
    lift(value) {
      if (typeof value !== "bigint" || value < signedInt64Minimum
          || value > signedInt64Maximum) {
        throw new TypeError("raw canonical core i64 value is invalid");
      }
      return signed ? BigInt.asIntN(64, value) : BigInt.asUintN(64, value);
    },
    lower(value) {
      if (typeof value !== "bigint" || value < minimum || value > maximum) {
        throw invalidValue(signed ? "s64" : "u64");
      }
      return value;
    },
    read: (view, index) => view[read](index, true),
    write: (view, index, value) => view[write](index, value, true),
  });
}

function floatCodec(bits, read, write) {
  const size = bits / 8;
  return Object.freeze({
    size,
    alignment: size,
    lift(value) {
      if (typeof value !== "number") throw invalidValue(`f${bits}`);
      return bits === 32 ? Math.fround(value) : value;
    },
    lower(value) {
      if (typeof value !== "number") throw invalidValue(`f${bits}`);
      return bits === 32 ? Math.fround(value) : value;
    },
    read: (view, index) => view[read](index, true),
    write: (view, index, value) => view[write](index, value, true),
  });
}

function normalizeCoreInt32(value) {
  if (!Number.isInteger(value) || value < signedInt32Minimum || value > signedInt32Maximum) {
    throw new TypeError("raw canonical core i32 value is invalid");
  }
  return value;
}

function codePointToCharacter(value) {
  if (value > 0x10ffff || value >= 0xd800 && value <= 0xdfff) {
    throw new TypeError("raw canonical character code point is invalid");
  }
  return String.fromCodePoint(value);
}

function characterToCodePoint(value) {
  if (typeof value !== "string" || [...value].length !== 1) {
    throw invalidValue("character");
  }
  const codePoint = value.codePointAt(0);
  if (codePoint >= 0xd800 && codePoint <= 0xdfff) {
    throw invalidValue("character");
  }
  return codePoint;
}

function readCodec(kind) {
  if (typeof kind !== "string" || !Object.hasOwn(scalarCodecs, kind)) {
    throw new TypeError("raw canonical scalar kind is unsupported");
  }
  return scalarCodecs[kind];
}

function invalidValue(kind) {
  return new TypeError(`raw canonical ${kind} value is invalid`);
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

import assert from "node:assert/strict";
import test from "node:test";

import {
  describeValueArray,
  isAbiType,
  isHostObject,
  isMemoryViewAbiType,
  isHandleValueAbiType,
  isScalarAbiType,
  nullableUnderlying,
  requireHandleValue,
  isTransferAbiType,
  isValueArray,
  isValueArrayAbiType,
  memoryViewConstructor,
  valueArrayConstructor,
} from "./interop-abi-types.mjs";

const valueArrays = Object.freeze({
  bytes: Uint8Array, "i8[]": Int8Array, "i16[]": Int16Array, "u16[]": Uint16Array,
  "i32[]": Int32Array, "u32[]": Uint32Array, "i64[]": BigInt64Array, "u64[]": BigUint64Array,
  "f32[]": Float32Array, "f64[]": Float64Array,
});

test("isAbiType recognizes the complete supported ABI vocabulary", () => {
  for (const type of [
    "void", "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
    "i64", "u64", "f32", "f64", "date", "string", "bytes", "object",
    "subscription", "promise", "callback",
  ]) {
    assert.equal(isAbiType(type), true, type);
  }
  for (const type of [null, 1, "", "bogus"]) {
    assert.equal(isAbiType(type), false, String(type));
  }
});

test("nullableUnderlying recognizes a nullable scalar or date", () => {
  assert.equal(nullableUnderlying("i32?"), "i32");
  assert.equal(nullableUnderlying("date?"), "date");
  assert.equal(isAbiType("u64?"), true);
  assert.equal(isScalarAbiType("i32?"), false);
  for (const type of [null, "i32", "string?", "object?", "?", "bytes?"]) {
    assert.equal(nullableUnderlying(type), null, String(type));
  }
});

test("isScalarAbiType recognizes only scalar ABI values", () => {
  for (const type of [
    "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
    "i64", "u64", "f32", "f64", "date",
  ]) {
    assert.equal(isScalarAbiType(type), true, type);
  }
  for (const type of [
    null, 1, "", "bogus", "void", "string", "bytes", "object",
    "subscription", "promise", "callback",
  ]) {
    assert.equal(isScalarAbiType(type), false, String(type));
  }
});

test("maps every span view ABI type to its typed array", () => {
  const views = {
    "u8-view": Uint8Array, "i8-view": Int8Array, "i16-view": Int16Array, "u16-view": Uint16Array,
    "i32-view": Int32Array, "u32-view": Uint32Array, "i64-view": BigInt64Array,
    "u64-view": BigUint64Array, "f32-view": Float32Array, "f64-view": Float64Array,
  };
  for (const [type, constructor] of Object.entries(views)) {
    assert.equal(isAbiType(type), true, type);
    assert.equal(isMemoryViewAbiType(type), true, type);
    assert.equal(isTransferAbiType(type), false, type);
    assert.equal(isValueArrayAbiType(type), false, type);
    assert.equal(memoryViewConstructor(type), constructor);
  }
  for (const type of [null, "bytes", "u8", "bool-view"]) {
    assert.equal(isMemoryViewAbiType(type), false, String(type));
    assert.throws(() => memoryViewConstructor(type), /not a memory view/);
  }
});

test("maps every value-array ABI type to its typed array", () => {
  for (const [type, constructor] of Object.entries(valueArrays)) {
    assert.equal(isAbiType(type), true, type);
    assert.equal(isValueArrayAbiType(type), true, type);
    assert.equal(isTransferAbiType(type), true, type);
    assert.equal(valueArrayConstructor(type), constructor);
    assert.equal(isValueArray(new constructor(1)), true, type);
  }
  assert.equal(isTransferAbiType("string"), true);
  for (const type of [null, "i32", "string", "bool[]", "char[]"]) {
    assert.equal(isValueArrayAbiType(type), false, String(type));
    assert.throws(() => valueArrayConstructor(type), /not a value array/);
  }
  for (const value of [null, [], new DataView(new ArrayBuffer(1)), new Uint8ClampedArray(1)]) {
    assert.equal(isValueArray(value), false);
  }
  assert.equal(describeValueArray("bytes"), "byte-array value must be Uint8Array or null");
  assert.equal(describeValueArray("f64[]"), "f64[] value must be Float64Array or null");
});

test("isHostObject accepts objects and functions only", () => {
  for (const value of [{}, [], () => {}, Float64Array.of(1)]) assert.equal(isHostObject(value), true);
  for (const value of [null, undefined, 1, "text", 1n, true]) assert.equal(isHostObject(value), false);
});

// An object, exception or array of references enters managed code as a handle.
test("checks values entering managed code as handle values", () => {
  for (const type of ["any", "error", "string[]", "object[]", "any[]"]) {
    assert.equal(isHandleValueAbiType(type), true, type);
  }
  for (const type of ["object", "string", null]) assert.equal(isHandleValueAbiType(type), false, String(type));
  for (const [type, value] of [
    ["any", 1], ["error", "message"], ["string[]", ["a", null, undefined]], ["object[]", [{}, () => {}, null]],
    ["any[]", [1, "a"]], ["string[]", null], ["any", undefined],
  ]) {
    requireHandleValue(type, value);
  }
  assert.throws(() => requireHandleValue("string[]", "a"), /string\[\] value must be an array/);
  assert.throws(() => requireHandleValue("object[]", 1), /JSObject\[\] value must be an array/);
  assert.throws(() => requireHandleValue("any[]", {}), /object\[\] value must be an array/);
  assert.throws(() => requireHandleValue("string[]", [1]), /hold strings/);
  assert.throws(() => requireHandleValue("object[]", [1]), /hold objects/);
});
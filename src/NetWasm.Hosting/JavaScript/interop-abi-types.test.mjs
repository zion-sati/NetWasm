import assert from "node:assert/strict";
import test from "node:test";

import { isAbiType, isScalarAbiType } from "./interop-abi-types.mjs";

test("isAbiType recognizes the complete supported ABI vocabulary", () => {
  for (const type of [
    "void", "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
    "i64", "u64", "f32", "f64", "string", "bytes", "object",
    "subscription", "promise", "callback",
  ]) {
    assert.equal(isAbiType(type), true, type);
  }
  for (const type of [null, 1, "", "bogus"]) {
    assert.equal(isAbiType(type), false, String(type));
  }
});

test("isScalarAbiType recognizes only scalar ABI values", () => {
  for (const type of [
    "bool", "i8", "u8", "char", "i16", "u16", "i32", "u32",
    "i64", "u64", "f32", "f64",
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

import assert from "node:assert/strict";
import test from "node:test";

import {
  liftInteropScalar,
  lowerInteropScalar,
  writeInteropScalarResult,
} from "./interop-scalar-codec.mjs";

const lift = (type, value) => liftInteropScalar({ type, value });
const lower = (type, value) => lowerInteropScalar({ type, value });

test("lifts every JavaScript interop scalar ABI type", () => {
  assert.equal(lift("bool", 0), false);
  assert.equal(lift("bool", -1), true);
  assert.equal(lift("i8", 0xff), -1);
  assert.equal(lift("u8", -1), 255);
  assert.equal(lift("char", 0x1_0041), "A");
  assert.equal(lift("i16", 0xffff), -1);
  assert.equal(lift("u16", -1), 65_535);
  assert.equal(lift("i32", 0xffff_ffff), -1);
  assert.equal(lift("u32", -1), 0xffff_ffff);
  assert.equal(lift("i64", 0xffff_ffff_ffff_ffffn), -1n);
  assert.equal(lift("u64", -1n), 0xffff_ffff_ffff_ffffn);
  assert.equal(lift("f32", 1 / 3), Math.fround(1 / 3));
  assert.equal(lift("f64", Math.PI), Math.PI);
  assert.throws(() => lift("string", 0), /unsupported/);
});

test("lowers every JavaScript interop scalar ABI type", () => {
  assert.deepEqual([
    lower("bool", false), lower("bool", true),
    lower("i8", -128), lower("u8", 255),
    lower("char", "A"), lower("i16", -32_768), lower("u16", 65_535),
    lower("i32", -0x8000_0000), lower("u32", 0xffff_ffff),
    lower("i64", -0x8000_0000_0000_0000n),
    lower("u64", 0xffff_ffff_ffff_ffffn),
    lower("f32", 1 / 3), lower("f64", Math.PI),
  ], [
    0, 1, -128, 255, 65, -32_768, 65_535, -0x8000_0000, 0xffff_ffff,
    -0x8000_0000_0000_0000n, -1n, Math.fround(1 / 3), Math.PI,
  ]);
});

test("rejects values outside the JavaScript interop scalar ABI", () => {
  const invalid = [
    ["bool", 1], ["i8", -129], ["i8", 128], ["u8", -1], ["u8", 256],
    ["char", ""], ["char", "ab"], ["char", 65],
    ["i16", -32_769], ["i16", 32_768], ["u16", -1], ["u16", 65_536],
    ["i32", -0x8000_0001], ["i32", 0x8000_0000],
    ["u32", -1], ["u32", 0x1_0000_0000], ["u32", 0.5],
    ["i64", -0x8000_0000_0000_0001n], ["i64", 0x8000_0000_0000_0000n],
    ["i64", 1], ["u64", -1n], ["u64", 0x1_0000_0000_0000_0000n],
    ["u64", 1], ["f32", "1"], ["f64", 1n], ["string", "value"],
  ];
  for (const [type, value] of invalid) {
    assert.throws(() => lower(type, value), /value|unsupported/);
  }
});

test("writes every scalar result using the frozen little-endian status ABI", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const values = [
    ["bool", 0, 1], ["i8", 4, -1], ["u8", 8, 255], ["char", 12, 65],
    ["i16", 16, -2], ["u16", 20, 65_534], ["i32", 24, -3],
    ["u32", 28, 0xffff_fffd], ["i64", 32, -4n],
    ["u64", 40, -5n], ["f32", 48, Math.fround(1 / 3)], ["f64", 56, Math.PI],
  ];
  for (const [type, byteOffset, value] of values) {
    writeInteropScalarResult({ byteOffset, memory, type, value });
  }
  const view = new DataView(memory.buffer);
  assert.deepEqual([
    view.getInt32(0, true), view.getInt32(4, true), view.getUint32(8, true),
    view.getUint32(12, true), view.getInt32(16, true), view.getUint32(20, true),
    view.getInt32(24, true), view.getUint32(28, true), view.getBigInt64(32, true),
    view.getBigUint64(40, true), view.getFloat32(48, true), view.getFloat64(56, true),
  ], [1, -1, 255, 65, -2, 65_534, -3, 0xffff_fffd, -4n,
    0xffff_ffff_ffff_fffbn, Math.fround(1 / 3), Math.PI]);
  assert.throws(() => writeInteropScalarResult({
    byteOffset: 0, memory, type: "string", value: 0,
  }), /unsupported/);
  assert.throws(() => writeInteropScalarResult({
    byteOffset: 0, memory: {}, type: "i32", value: 0,
  }), /memory/);
});

test("rejects malformed scalar requests", () => {
  for (const action of [liftInteropScalar, lowerInteropScalar]) {
    for (const value of [
      null, [], {}, { type: "u8" }, { type: "u8", value: 1, extra: true },
      { type: "u8", value: 1, [Symbol("invalid")]: true },
      Object.defineProperty({ type: "u8" }, "value", { enumerable: true, get: () => 1 }),
    ]) assert.throws(() => action(value), TypeError);
  }
  const valid = {
    byteOffset: 0, memory: new WebAssembly.Memory({ initial: 1 }), type: "u8", value: 1,
  };
  for (const value of [null, [], {}, { ...valid, extra: true }, {
    ...valid, [Symbol("invalid")]: true,
  }, Object.defineProperty({ ...valid }, "value", { enumerable: true, get: () => 1 })]) {
    assert.throws(() => writeInteropScalarResult(value), TypeError);
  }
});

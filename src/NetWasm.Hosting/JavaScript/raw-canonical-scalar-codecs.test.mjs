import assert from "node:assert/strict";
import test from "node:test";
import {
  liftRawCanonicalFlatScalar,
  lowerRawCanonicalFlatScalar,
  readRawCanonicalMemoryScalar,
  writeRawCanonicalMemoryScalar,
} from "./raw-canonical-scalar-codecs.mjs";

const lift = (kind, value) => liftRawCanonicalFlatScalar({ kind, value });
const lower = (kind, value) => lowerRawCanonicalFlatScalar({ kind, value });

test("lifts every primitive from its core WebAssembly representation", () => {
  assert.equal(lift("bool", 0), false);
  assert.equal(lift("bool", -1), true);
  assert.equal(lift("s8", 127), 127);
  assert.equal(lift("s8", -1), -1);
  assert.equal(lift("u8", -1), 255);
  assert.equal(lift("s16", -1), -1);
  assert.equal(lift("u16", -1), 65_535);
  assert.equal(lift("s32", -0x8000_0000), -0x8000_0000);
  assert.equal(lift("u32", -1), 0xffff_ffff);
  assert.equal(lift("s64", -1n), -1n);
  assert.equal(lift("u64", -1n), 0xffff_ffff_ffff_ffffn);
  assert.equal(lift("f32", 1 / 3), Math.fround(1 / 3));
  assert.equal(lift("f64", Number.POSITIVE_INFINITY), Number.POSITIVE_INFINITY);
  assert.equal(Number.isNaN(lift("f32", Number.NaN)), true);
  assert.equal(Number.isNaN(lift("f64", Number.NaN)), true);
  assert.equal(lift("character", 0x1f680), "🚀");
});

test("lowers every primitive after exact Jco-facing validation", () => {
  assert.equal(lower("bool", false), 0);
  assert.equal(lower("bool", true), 1);
  assert.equal(lower("s8", -128), -128);
  assert.equal(lower("u8", 255), 255);
  assert.equal(lower("s16", -32_768), -32_768);
  assert.equal(lower("u16", 65_535), 65_535);
  assert.equal(lower("s32", -0x8000_0000), -0x8000_0000);
  assert.equal(lower("u32", 0xffff_ffff), 0xffff_ffff);
  assert.equal(lower("s64", -0x8000_0000_0000_0000n), -0x8000_0000_0000_0000n);
  assert.equal(lower("u64", 0xffff_ffff_ffff_ffffn), 0xffff_ffff_ffff_ffffn);
  assert.equal(lower("f32", 1 / 3), Math.fround(1 / 3));
  assert.equal(lower("f64", Number.NEGATIVE_INFINITY), Number.NEGATIVE_INFINITY);
  assert.equal(Number.isNaN(lower("f32", Number.NaN)), true);
  assert.equal(Number.isNaN(lower("f64", Number.NaN)), true);
  assert.equal(lower("character", "A"), 65);
  assert.equal(lower("character", "🚀"), 0x1f680);
});

test("rejects invalid core scalar representations", () => {
  for (const value of [0.5, -0x8000_0001, 0x8000_0000]) {
    assert.throws(() => lift("u32", value), /core i32/);
  }
  for (const value of [0, -0x8000_0000_0000_0001n, 0x8000_0000_0000_0000n]) {
    assert.throws(() => lift("u64", value), /core i64/);
  }
  assert.throws(() => lift("f32", "1"), /f32 value/);
  assert.throws(() => lift("f64", 1n), /f64 value/);
  assert.throws(() => lift("character", 0xd800), /code point/);
  assert.throws(() => lift("character", 0x11_0000), /code point/);
});

test("rejects out-of-domain Jco-facing scalar values", () => {
  const invalid = [
    ["bool", 1],
    ["s8", -129], ["s8", 128],
    ["u8", -1], ["u8", 256],
    ["s16", -32_769], ["s16", 32_768],
    ["u16", -1], ["u16", 65_536],
    ["s32", -0x8000_0001], ["s32", 0x8000_0000],
    ["u32", -1], ["u32", 0x1_0000_0000], ["u32", 0.5],
    ["s64", -0x8000_0000_0000_0001n], ["s64", 0x8000_0000_0000_0000n],
    ["u64", -1n], ["u64", 0x1_0000_0000_0000_0000n], ["u64", 1],
    ["f32", "1"], ["f64", 1n],
    ["character", null], ["character", ""], ["character", "ab"],
    ["character", "\ud800"],
  ];
  for (const [kind, value] of invalid) {
    assert.throws(() => lower(kind, value), /value is invalid/);
  }
});

test("reads and writes every little-endian primitive at both address widths", () => {
  const cases = [
    ["bool", 0, true],
    ["s8", 1, -127],
    ["u8", 2, 254],
    ["s16", 4, -32_767],
    ["u16", 6, 65_534],
    ["s32", 8, -0x7fff_ffff],
    ["u32", 12, 0xffff_fffe],
    ["s64", 16, -0x7fff_ffff_ffff_ffffn],
    ["u64", 24, 0xffff_ffff_ffff_fffen],
    ["f32", 32, Math.fround(-1 / 3)],
    ["f64", 40, Math.PI],
    ["character", 48, "🚀"],
  ];
  for (const target of ["wasm32", "wasm64"]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    new DataView(memory.buffer).setUint8(0, 255);
    assert.equal(readRawCanonicalMemoryScalar({
      address: target === "wasm32" ? 0 : 0n,
      kind: "bool",
      memory,
      target,
    }), true);
    for (const [kind, offset, expected] of cases) {
      const address = target === "wasm32" ? offset : BigInt(offset);
      writeRawCanonicalMemoryScalar({ address, kind, memory, target, value: expected });
      const actual = readRawCanonicalMemoryScalar({ address, kind, memory, target });
      if (typeof expected === "number" && Number.isNaN(expected)) {
        assert.equal(Number.isNaN(actual), true);
      } else {
        assert.equal(actual, expected);
      }
    }
    const bytes = new Uint8Array(memory.buffer);
    assert.deepEqual([...bytes.slice(6, 8)], [0xfe, 0xff]);
    assert.deepEqual([...bytes.slice(12, 16)], [0xfe, 0xff, 0xff, 0xff]);
    assert.deepEqual([...bytes.slice(24, 32)], [0xfe, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);
    assert.deepEqual([...bytes.slice(48, 52)], [0x80, 0xf6, 0x01, 0x00]);
    bytes.set([0x78, 0x56, 0x34, 0x12], 56);
    assert.equal(readRawCanonicalMemoryScalar({
      address: target === "wasm32" ? 56 : 56n,
      kind: "u32",
      memory,
      target,
    }), 0x1234_5678);
  }
});

test("preserves floating special values through canonical memory", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  for (const [kind, address, value] of [
    ["f32", 0, Number.NaN],
    ["f64", 8, Number.NaN],
    ["f32", 16, Number.POSITIVE_INFINITY],
    ["f64", 24, Number.NEGATIVE_INFINITY],
  ]) {
    writeRawCanonicalMemoryScalar({ address, kind, memory, target: "wasm32", value });
    const actual = readRawCanonicalMemoryScalar({ address, kind, memory, target: "wasm32" });
    assert.equal(Number.isNaN(value) ? Number.isNaN(actual) : actual === value, true);
  }
});

test("validates a scalar result before any memory mutation", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const bytes = new Uint8Array(memory.buffer, 0, 4);
  bytes.set([1, 2, 3, 4]);
  assert.throws(() => writeRawCanonicalMemoryScalar({
    address: 65_536,
    kind: "u32",
    memory,
    target: "wasm32",
    value: -1,
  }), /u32 value/);
  assert.deepEqual([...bytes], [1, 2, 3, 4]);
  assert.throws(() => writeRawCanonicalMemoryScalar({
    address: 65_536,
    kind: "u32",
    memory,
    target: "wasm32",
    value: 1,
  }), /out of bounds/);
  assert.deepEqual([...bytes], [1, 2, 3, 4]);
});

test("rejects invalid characters read from canonical memory", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const view = new DataView(memory.buffer);
  for (const codePoint of [0xdfff, 0x11_0000]) {
    view.setUint32(0, codePoint, true);
    assert.throws(() => readRawCanonicalMemoryScalar({
      address: 0,
      kind: "character",
      memory,
      target: "wasm32",
    }), /code point/);
  }
});

test("rejects malformed scalar requests and unsupported kinds", () => {
  for (const value of [null, [], {}, { kind: "u8", value: 1, extra: true },
    { kind: "u8", wrong: 1 }, { kind: "u8", value: 1, [Symbol("bad")]: true },
    Object.defineProperty({ kind: "u8", value: 1 }, "value", {
      get: () => 1,
      enumerable: true,
    })]) {
    assert.throws(() => liftRawCanonicalFlatScalar(value), /lift/);
  }
  assert.throws(() => lift("unit", 0), /unsupported/);
  assert.throws(() => lower(null, 0), /unsupported/);

  const valid = {
    address: 0,
    kind: "u8",
    memory: new WebAssembly.Memory({ initial: 1 }),
    target: "wasm32",
  };
  for (const value of [null, [], {}, { ...valid, extra: true },
    { address: 0, kind: "u8", memory: valid.memory, wrong: "wasm32" },
    { ...valid, [Symbol("bad")]: true },
    Object.defineProperty({ ...valid }, "target", {
      get: () => "wasm32",
      enumerable: true,
    })]) {
    assert.throws(() => readRawCanonicalMemoryScalar(value), /read/);
  }
  assert.throws(() => readRawCanonicalMemoryScalar({ ...valid, kind: "text" }), /unsupported/);
});

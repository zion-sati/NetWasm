import assert from "node:assert/strict";
import test from "node:test";

import {
  rcArray,
  rcBigInteger,
  rcBool,
  rcBorrowedResult,
  rcByteList,
  rcCharacter,
  rcCodePoint,
  rcConstructor,
  rcEncodeText,
  rcEnumCase,
  rcEnumIndex,
  rcErrorPayload,
  rcF32FromI32,
  rcF32FromI64,
  rcF64FromI64,
  rcFlags,
  rcFlagWords,
  rcFloat32,
  rcFloat64,
  rcFunction,
  rcI32FromI64,
  rcIndex,
  rcInteger,
  rcInvalid,
  rcLiftTransaction,
  rcListByteLength,
  rcLoweredListByteLength,
  rcLowerTransaction,
  rcMethod,
  rcNumberList,
  rcObject,
  rcReadBytes,
  rcReadText,
  rcSequenceLength,
  rcStatic,
  rcTagged,
  rcTaggedResult,
  rcUnit,
  rcView,
} from "./raw-adapter-runtime.mjs";

test("renews the memory view when memory grows", () => {
  const memory = new WebAssembly.Memory({ initial: 1, maximum: 2 });
  const first = rcView(memory);
  assert.equal(rcView(memory), first);
  memory.grow(1);
  assert.notEqual(rcView(memory), first);
  assert.equal(rcView(memory).byteLength, 2 * 65536);
});

test("checks the alignment and bounds of both address widths", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  for (const address of [8, 8n]) assert.equal(rcIndex(memory, address, 4, 8), 8);
  for (const address of [6, 6n]) assert.throws(() => rcIndex(memory, address, 4, 4), /unaligned/);
  for (const address of [65536, 65536n]) assert.equal(rcIndex(memory, address, 1, 0), 65536);
  for (const [address, length] of [[65540, 0], [65536, 1], [65540n, 0], [65536n, 1]]) {
    assert.throws(() => rcIndex(memory, address, 1, length), /out of bounds/);
  }
  assert.throws(() => rcIndex(memory, -4, 4, 4), /out of bounds/);
  assert.throws(() => rcIndex(memory, -4n, 4, 4), /out of bounds/);
});

test("bounds sequence lengths and list byte lengths", () => {
  assert.equal(rcSequenceLength(5, "list"), 5);
  assert.equal(rcSequenceLength(5n, "list"), 5);
  assert.throws(() => rcSequenceLength(0x1000_0000, "text"), /text length is too large/);
  assert.throws(() => rcSequenceLength(-1n, "list"), /list length is too large/);
  assert.equal(rcListByteLength(4, 3), 12);
  assert.equal(rcListByteLength(0, 0), 0);
  assert.throws(() => rcListByteLength(0, 1), /zero-sized/);
  assert.throws(() => rcListByteLength(16, 0x0100_0000), /byte length is too large/);
  assert.equal(rcLoweredListByteLength(16, 0x0100_0000), 0x1000_0000);
  assert.equal(rcLoweredListByteLength(0, 0), 0);
  assert.throws(() => rcLoweredListByteLength(0, 2), /zero-sized/);
});

test("reads UTF-8 text and bytes as copies", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint8Array(memory.buffer, 16, 3).set([0x61, 0xc3, 0xa9]);
  assert.equal(rcReadText(memory, 16, 3), "aé");
  new Uint8Array(memory.buffer, 32, 1).set([0xff]);
  assert.throws(() => rcReadText(memory, 32, 1), error =>
    error instanceof TypeError && /UTF-8/.test(error.message) && error.cause !== undefined);
  const bytes = rcReadBytes(memory, 16n, 3n);
  bytes[0] = 0;
  assert.equal(new Uint8Array(memory.buffer)[16], 0x61);
});

test("lifts characters, enums and flags", () => {
  assert.equal(rcCharacter(0x1f600), "\u{1f600}");
  for (const codePoint of [0x110000, 0xd800, 0xdfff]) {
    assert.throws(() => rcCharacter(codePoint), /code point is invalid/);
  }
  assert.equal(rcEnumCase(["a", "b"], 1), "b");
  assert.throws(() => rcEnumCase(["a"], 1), /enum discriminant/);
  const names = Array.from({ length: 33 }, (_, index) => `f${index}`);
  const flags = rcFlags(names, [0b101, 1]);
  assert.equal(flags.f0, true);
  assert.equal(flags.f1, false);
  assert.equal(flags.f2, true);
  assert.equal(flags.f32, true);
});

test("rereads joined variant slots as the selected case's type", () => {
  assert.equal(rcF32FromI32(0x3fc0_0000), 1.5);
  assert.equal(rcI32FromI64(0xffff_ffffn), -1);
  assert.equal(rcF32FromI64(0x3fc0_0000n), 1.5);
  assert.equal(rcF64FromI64(0x3ff8_0000_0000_0000n), 1.5);
});

test("validates scalar host values", () => {
  assert.equal(rcInvalid("x").message, "raw canonical x value is invalid");
  assert.equal(rcUnit(undefined), undefined);
  assert.throws(() => rcUnit(null), /unit value is invalid/);
  assert.throws(() => rcBorrowedResult(), /borrowed resources/);
  assert.equal(rcBool(true), 1);
  assert.equal(rcBool(false), 0);
  assert.throws(() => rcBool(1), /bool value/);
  assert.equal(rcInteger(255, 0, 255, "u8"), 255);
  for (const value of [1.5, -1, 256]) assert.throws(() => rcInteger(value, 0, 255, "u8"), /u8 value/);
  assert.equal(rcBigInteger(5n, 0n, 9n, "u64"), 5n);
  for (const value of [5, -1n, 10n]) assert.throws(() => rcBigInteger(value, 0n, 9n, "u64"), /u64 value/);
  assert.equal(rcFloat32(1.1), Math.fround(1.1));
  assert.throws(() => rcFloat32("1"), /f32 value/);
  assert.equal(rcFloat64(1.1), 1.1);
  assert.throws(() => rcFloat64(1n), /f64 value/);
  assert.equal(rcCodePoint("\u{1f600}"), 0x1f600);
  for (const value of [1, "ab", "\ud800"]) assert.throws(() => rcCodePoint(value), /character value/);
});

test("snapshots sequences and texts", () => {
  assert.deepEqual([...rcEncodeText("é")], [0xc3, 0xa9]);
  assert.throws(() => rcEncodeText(1), /text value/);
  const bytes = new Uint8Array([1, 2]);
  const copy = rcByteList(bytes);
  bytes[0] = 9;
  assert.deepEqual([...copy], [1, 2]);
  assert.throws(() => rcByteList([1, 2]), /byte list value/);
  assert.deepEqual(rcNumberList(Int16Array.of(-1, 2), Int16Array), [-1, 2]);
  assert.deepEqual(rcNumberList([3, 4], Int16Array), [3, 4]);
});

test("accepts only plain dense arrays", () => {
  assert.deepEqual(rcArray([1, 2], 2, "tuple"), [1, 2]);
  assert.deepEqual(rcArray([], undefined, "list"), []);
  const extra = [1];
  extra.name = "x";
  const hidden = [1];
  Object.defineProperty(hidden, 0, { enumerable: false, value: 1 });
  const accessor = [1];
  Object.defineProperty(accessor, 0, { enumerable: true, get: () => 1 });
  for (const value of [{}, Object.assign([1], { [Symbol("s")]: 1 }), [1, 2, 3], extra, hidden, accessor]) {
    assert.throws(() => rcArray(value, 2, "tuple"), /tuple value/);
  }
  assert.throws(() => rcArray([1], 2, "tuple"), /tuple value/);
  for (const value of [hidden, accessor]) assert.throws(() => rcArray(value, undefined, "list"), /list value/);
  assert.throws(() => rcArray(Array(2), undefined, "list"), /list value/);
});

test("accepts only plain objects with exactly the expected names", () => {
  assert.deepEqual(rcObject({ b: 2, a: 1 }, ["a", "b"], "record"), [1, 2]);
  const accessor = {};
  Object.defineProperty(accessor, "a", { enumerable: true, get: () => 1 });
  const hidden = {};
  Object.defineProperty(hidden, "a", { enumerable: false, value: 1 });
  for (const value of [null, 1, [], { [Symbol("s")]: 1, a: 1 }, { a: 1, b: 2 }, { b: 1 }, accessor, hidden]) {
    assert.throws(() => rcObject(value, ["a"], "record"), /record value/);
  }
});

test("reads a tagged value's case and payload", () => {
  assert.deepEqual(rcTagged({ tag: "b", val: 3 }, ["a", "b"], [false, true], "variant"), { index: 1, value: 3 });
  assert.deepEqual(rcTagged({ tag: "a" }, ["a", "b"], [false, true], "variant"), { index: 0, value: undefined });
  const accessorTag = {};
  Object.defineProperty(accessorTag, "tag", { enumerable: true, get: () => "a" });
  const accessorValue = { tag: "b" };
  Object.defineProperty(accessorValue, "val", { enumerable: true, get: () => 1 });
  const hiddenTag = {};
  Object.defineProperty(hiddenTag, "tag", { enumerable: false, value: "a" });
  for (const value of [
    null, 1, [], { tag: "a", [Symbol("s")]: 1 }, {}, { tag: 1 }, accessorTag, hiddenTag, { tag: "c" },
    accessorValue, { tag: "a", extra: 1 }, { tag: "a", val: 1 }, { tag: "b" },
  ]) {
    assert.throws(() => rcTagged(value, ["a", "b"], [false, true], "variant"), /variant value/);
  }
});

test("lowers enums and flags", () => {
  assert.equal(rcEnumIndex("b", ["a", "b"]), 1);
  assert.throws(() => rcEnumIndex(1, ["a"]), /enum value/);
  assert.throws(() => rcEnumIndex("c", ["a"]), /enum value/);
  const names = Array.from({ length: 33 }, (_, index) => `f${index}`);
  const value = Object.fromEntries(names.map((name, index) => [name, index === 31 || index === 32]));
  assert.deepEqual(rcFlagWords(value, names, 2), [0x8000_0000, 1]);
  assert.throws(() => rcFlagWords({ f0: 1 }, ["f0"], 1), /flags value/);
});

test("transfers lifted owned handles only on commit", () => {
  const borrowed = [];
  const transferred = [];
  const host = {
    borrow: request => { borrowed.push(request); return `value-${request.handle}`; },
    transfer: request => transferred.push(request),
  };
  const lift = rcLiftTransaction(host, "store");
  assert.equal(lift.borrow(3, 1), "value-1");
  assert.equal(lift.own(3, 2), "value-2");
  assert.throws(() => lift.own(3, 2), /owned resource is duplicated/);
  assert.equal(lift.own(4, 2), "value-2");
  assert.deepEqual(transferred, []);
  lift.commit();
  assert.deepEqual(transferred, [{ handle: 2, store: "store", type: 3 }, { handle: 2, store: "store", type: 4 }]);
  assert.equal(borrowed.length, 4);
});

function lowering({ wasm64 = false, register = () => 7, failRelease = false, failDrop = false } = {}) {
  let released = false;
  const memory = new WebAssembly.Memory({ initial: 1 });
  const calls = [];
  const host = {
    drop(request) {
      calls.push(["drop", request.handle, request.type]);
      if (failDrop) throw new Error("drop failed");
    },
    reallocate(...values) {
      calls.push(["reallocate", ...values]);
      return values[3] === 0 || values[3] === 0n ? values[0] : wasm64 ? 64n : 64;
    },
    register(request) {
      calls.push(["register", request.type]);
      // The store releases through this action when it drops the entry.
      if (!failRelease && !released) {
        released = true;
        request.release(request.value);
      }
      return register();
    },
    release(request) {
      calls.push(["release", request.type]);
      if (failRelease) throw new Error("release failed");
    },
  };
  return { calls, memory, tx: rcLowerTransaction(host, memory, "store", wasm64) };
}

test("registers acquired resources and allocates sequences", () => {
  const { calls, tx } = lowering();
  const first = tx.acquire(1, {});
  const second = tx.acquire(1, () => {});
  tx.acquire(2, {});
  assert.throws(() => tx.acquire(1, second.value), /owned resource is duplicated/);
  for (const value of [null, 1]) assert.throws(() => tx.acquire(1, value), /owned resource value/);
  tx.register();
  assert.equal(first.handle, 7);
  assert.equal(first.registered, true);
  assert.equal(tx.allocate(4, 0), 0);
  assert.equal(tx.allocate(4, 8), 64);
  tx.commit();
  assert.deepEqual(calls.filter(call => call[0] === "reallocate"), [["reallocate", 0, 0, 4, 8]]);
  const wide = lowering({ wasm64: true });
  assert.equal(wide.tx.allocate(8, 0), 0n);
  assert.equal(wide.tx.allocate(8, 16), 64n);
  assert.deepEqual(wide.calls, [["reallocate", 0n, 0n, 8n, 16n]]);
});

test("rejects handles a store cannot have issued", () => {
  for (const [handle, message] of [[-1, /u32 value/], [0, /handle zero is reserved/]]) {
    const { tx } = lowering({ register: () => handle });
    tx.acquire(1, {});
    assert.throws(() => tx.register(), message);
  }
});

test("rolls back allocations and resources and reports cleanup failures", () => {
  const original = new Error("lowering failed");
  const { calls, tx } = lowering();
  tx.acquire(1, {});
  tx.register();
  tx.acquire(2, {});
  tx.allocate(4, 8);
  assert.throws(() => tx.rollback(original), error => error === original);
  assert.deepEqual(calls.slice(-3), [["reallocate", 64, 8, 4, 0], ["release", 2], ["drop", 7, 1]]);

  const failing = lowering({ failDrop: true, failRelease: true });
  failing.tx.acquire(1, {});
  failing.tx.register();
  failing.tx.acquire(2, {});
  assert.throws(() => failing.tx.rollback(original), error =>
    error instanceof AggregateError && error.errors[0] === original && error.errors.length === 3);
});

test("tags provider results and reads a thrown error's payload", () => {
  const ok = rcTaggedResult("ok", true, 3);
  assert.equal(Object.getPrototypeOf(ok), null);
  assert.equal(Object.isFrozen(ok), true);
  assert.deepEqual({ ...ok }, { tag: "ok", val: 3 });
  assert.deepEqual({ ...rcTaggedResult("err", false, 3) }, { tag: "err" });
  assert.equal(rcErrorPayload(Object.assign(new Error("x"), { payload: "p" })), "p");
  assert.equal(rcErrorPayload(Object.assign(() => {}, { payload: 0 })), 0);
  const accessor = new Error("x");
  Object.defineProperty(accessor, "payload", { get: () => "p" });
  assert.throws(() => rcErrorPayload(accessor), /payload must be a data property/);
  const failure = new RangeError("host");
  assert.throws(() => rcErrorPayload(failure), error => error === failure);
  assert.equal(rcErrorPayload("plain"), "plain");
  assert.equal(rcErrorPayload(null), null);
  assert.deepEqual(rcErrorPayload({ code: 1 }), { code: 1 });
});

test("binds provider functions, statics, constructors and methods", () => {
  class Handle {
    constructor(name) { this.name = name; }
    static open(name) { return new Handle(name); }
    size(extra) { return this.name.length + extra; }
  }
  const api = { add(left, right) { return left + right + this.offset; }, offset: 1, Handle };
  const providers = { "test:api": api };
  assert.equal(rcFunction(providers, "test:api", "add")(2, 3), 6);
  assert.equal(rcStatic(providers, "test:api", "Handle", "open")("ab").name, "ab");
  const handle = rcConstructor(providers, "test:api", "Handle")("abc");
  assert.ok(handle instanceof Handle);
  const size = rcMethod(providers, "test:api", "Handle", "size");
  assert.equal(size(handle, 1), 4);
  assert.throws(() => size({}, 1), /receiver is invalid/);
  handle.size = 5;
  assert.throws(() => size(handle, 1), /provider method is invalid/);

  assert.throws(() => rcFunction(providers, "test:missing", "add"), /'test:missing' is unavailable/);
  assert.throws(() => rcFunction({ "test:api": null }, "test:api", "add"), /unavailable/);
  const accessor = {};
  Object.defineProperty(accessor, "test:api", { get: () => api });
  assert.throws(() => rcFunction(accessor, "test:api", "add"), /must be a data property/);
  assert.throws(() => rcFunction(providers, "test:api", "offset"), /provider function is invalid/);
  assert.throws(() => rcStatic(providers, "test:api", "Handle", "missing"), /static function is invalid/);
  assert.throws(() => rcConstructor(providers, "test:api", "offset"), /resource 'offset' is unavailable/);
  assert.throws(() => rcMethod(providers, "test:api", "Handle", "missing"), /provider method is invalid/);
  class Guarded {
    get size() { return () => 1; }
  }
  assert.throws(() => rcMethod({ "test:api": { Guarded } }, "test:api", "Guarded", "size"),
    /method must be a data property/);
});

import assert from "node:assert/strict";
import test from "node:test";

import { createInteropDirectService } from "./interop-direct-service.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

const direct = (parameters, result, service, activity = { active: 0 }) =>
  createInteropDirectService({ descriptor: { parameters, result }, service, activity });

test("lifts each scalar from its WebAssembly value", () => {
  const cases = [
    ["bool", 1, true], ["bool", 0, false],
    ["i8", 0xff, -1], ["u8", -1, 0xff], ["i16", 0xffff, -1], ["u16", -1, 0xffff],
    ["char", 0x41, "A"], ["i32", -5, -5], ["u32", -1, 0xffff_ffff],
    ["i64", -1n, -1n], ["u64", -1n, 0xffff_ffff_ffff_ffffn],
    ["f32", 1.5, 1.5], ["f64", 0.1, 0.1],
  ];
  for (const [type, value, expected] of cases) {
    let received;
    direct([type], "void", lifted => { received = lifted; })(value);
    assert.deepEqual(received, expected, type);
  }
});

test("returns each scalar result as its WebAssembly value", () => {
  const cases = [
    ["bool", true, 1], ["i8", -1, -1], ["u16", 0xffff, 0xffff], ["char", "A", 0x41],
    ["u32", 0xffff_ffff, 0xffff_ffff], ["i64", -2n, -2n], ["u64", 0xffff_ffff_ffff_ffffn, -1n],
    ["f32", 0.1, Math.fround(0.1)], ["f64", 0.1, 0.1],
  ];
  for (const [type, value, expected] of cases) {
    assert.deepEqual(direct([], type, () => value)(), expected, type);
  }
  assert.equal(direct([], "void", () => 42)(), undefined);
});

// A thrown value unwinds into the guest, which raises JSException.
test("throws a result outside its type and what the service throws", () => {
  for (const [type, value] of [["u8", 256], ["i32", 1.5], ["bool", 1], ["u64", -1n], ["char", "AB"]]) {
    assert.throws(() => direct([], type, () => value)(), NetWasmHostError, type);
  }
  const failure = new Error("service failed");
  assert.throws(() => direct(["i32"], "i32", () => { throw failure; })(1), error => error === failure);
});

test("passes every argument at each arity", () => {
  for (let arity = 0; arity <= 6; arity++) {
    const parameters = Array.from({ length: arity }, () => "i32");
    const values = Array.from({ length: arity }, (_, index) => index + 1);
    const call = direct(parameters, "i32", (...lifted) => lifted.reduce((sum, value) => sum * 10 + value, 0));
    assert.equal(call(...values), values.reduce((sum, value) => sum * 10 + value, 0), `arity ${arity}`);
  }
});

// A subscription callback the service runs sees the import as active.
test("counts the import as active while the service runs", () => {
  const activity = { active: 0 };
  const seen = [];
  const call = direct(["i32"], "void", () => { seen.push(activity.active); throw new Error("x"); }, activity);
  assert.throws(() => call(1));
  assert.deepEqual(seen, [1]);
  assert.equal(activity.active, 0);
});

test("rejects a missing service or activity", () => {
  assert.throws(() => direct([], "void", null), TypeError);
  assert.throws(() => createInteropDirectService({ descriptor: { parameters: [], result: "void" }, service() {}, activity: null }),
    TypeError);
});

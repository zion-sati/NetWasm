import assert from "node:assert/strict";
import test from "node:test";
import { createRawCanonicalCallableBinder } from "./raw-canonical-callable-binder.mjs";

const parameter = (name, type) => ({ name, javascriptName: name, type });
const memoryLayout = (size = 4, alignment = 4) => ({ size, alignment });
const provider = () => ({
  interface: "sample:raw@1/api",
  function: "add",
  javascriptName: "add",
  functionKind: "freestanding",
  resourceType: null,
  resourceName: null,
  resourceJavaScriptName: null,
});
const binding = (overrides = {}) => ({
  kind: "callable",
  target: "wasm32",
  physical: { module: "cm32p2|sample:raw/api@1", name: "add" },
  coreSignature: { parameters: ["i32", "i32"], results: ["i32"] },
  provider: provider(),
  parameters: [parameter("left", { kind: "u32" }), parameter("right", { kind: "u32" })],
  result: { kind: "u32" },
  canonicalSignature: {
    parameters: ["i32", "i32"],
    result: "i32",
    flatParameters: ["i32", "i32"],
    flatResults: ["i32"],
    indirectParameters: false,
    indirectResult: false,
  },
  parameterMemory: memoryLayout(8, 4),
  resultMemory: memoryLayout(),
  ...overrides,
});

function harness(options = {}) {
  const calls = [];
  const invoke = options.invoke ?? (values => values[0] + values[1]);
  const actions = {
    bindProvider(value) {
      calls.push(["bind", value]);
      return invoke;
    },
    liftFlatValue(request) {
      calls.push(["lift", request]);
      return Object.hasOwn(options, "lifted") ? options.lifted : [...request.values];
    },
    lowerFlatValue(request) {
      calls.push(["lower", request]);
      return Object.hasOwn(options, "lowered") ? options.lowered : [request.value];
    },
    projectMemoryRange(request) {
      calls.push(["project", request]);
      return {};
    },
    readMemory() {
      calls.push(["memory"]);
      return options.memory ?? { buffer: new ArrayBuffer(64) };
    },
    readMemoryValue(request) {
      calls.push(["read", request]);
      return Object.hasOwn(options, "lifted") ? options.lifted : [3];
    },
    readStore() {
      calls.push(["store"]);
      return options.store ?? {};
    },
    writeMemoryValue(request) {
      calls.push(["write", request]);
    },
  };
  return { bind: createRawCanonicalCallableBinder(actions), calls, actions };
}

test("binds direct parameters and a direct result through narrow actions", () => {
  const { bind, calls } = harness();
  const selected = binding();
  const callable = bind(selected);
  assert.equal(calls.length, 1);
  assert.equal(calls[0][0], "bind");
  assert.equal(callable(2, 5), 7);
  assert.deepEqual(calls.map(call => call[0]), ["bind", "memory", "store", "lift", "lower"]);
  assert.deepEqual(calls[3][1].type.fields, [
    { name: "left", type: { kind: "u32" } },
    { name: "right", type: { kind: "u32" } },
  ]);
});

test("ignores a provider return when the WIT function has no result", () => {
  const selected = binding({
    coreSignature: { parameters: [], results: [] },
    parameters: [],
    result: null,
    canonicalSignature: {
      parameters: [], result: null, flatParameters: [], flatResults: [],
      indirectParameters: false, indirectResult: false,
    },
    parameterMemory: memoryLayout(0, 1),
    resultMemory: null,
  });
  const { bind, calls } = harness({ invoke: () => 99, lifted: [] });
  assert.equal(bind(selected)(), undefined);
  assert.deepEqual(calls.map(call => call[0]), ["bind", "memory", "store", "lift"]);
});

test("returns no core value for an explicit direct unit result", () => {
  const selected = binding({
    coreSignature: { parameters: [], results: [] },
    parameters: [],
    result: { kind: "unit" },
    canonicalSignature: {
      parameters: [], result: null, flatParameters: [], flatResults: [],
      indirectParameters: false, indirectResult: false,
    },
    parameterMemory: memoryLayout(0, 1),
    resultMemory: memoryLayout(0, 1),
  });
  const { bind, calls } = harness({ invoke: () => undefined, lifted: [], lowered: [] });
  assert.equal(bind(selected)(), undefined);
  assert.equal(calls.at(-1)[0], "lower");
});

test("validates the result pointer before lifting indirect parameters and writing", () => {
  const selected = binding({
    target: "wasm64",
    coreSignature: { parameters: ["i64", "i64"], results: [] },
    parameters: [parameter("value", { kind: "u32" })],
    result: { kind: "text" },
    canonicalSignature: {
      parameters: ["i64", "i64"], result: null,
      flatParameters: Array(17).fill("i32"), flatResults: ["i64", "i64"],
      indirectParameters: true, indirectResult: true,
    },
    parameterMemory: memoryLayout(68, 4),
    resultMemory: memoryLayout(16, 8),
  });
  const memory = { buffer: new ArrayBuffer(128) };
  const store = {};
  const { bind, calls } = harness({ invoke: values => `${values[0]}!`, lifted: [3], memory, store });
  assert.equal(bind(selected)(8n, 32n), undefined);
  assert.deepEqual(calls.map(call => call[0]), ["bind", "memory", "store", "project", "read", "write"]);
  assert.deepEqual(calls[3][1], {
    address: 32n, alignment: 8, byteLength: 16, memory, target: "wasm64",
  });
  assert.equal(calls[4][1].address, 8n);
  assert.equal(calls[5][1].value, "3!");
});

test("maps top-level result returns and thrown payloads to canonical variants", () => {
  const resultType = { kind: "result", ok: { kind: "u32" }, error: { kind: "text" } };
  const selected = binding({ result: resultType });
  const successful = harness({ invoke: () => 11, lowered: [101] });
  assert.equal(successful.bind(selected)(1, 2), 101);
  assert.deepEqual({ ...successful.calls.at(-1)[1].value }, { tag: "ok", val: 11 });

  for (const thrown of ["bad", Object.assign(new Error("bad"), { payload: "payload" })]) {
    const failed = harness({ invoke: () => { throw thrown; }, lowered: [202] });
    assert.equal(failed.bind(selected)(1, 2), 202);
    assert.deepEqual({ ...failed.calls.at(-1)[1].value }, {
      tag: "err",
      val: thrown === "bad" ? "bad" : "payload",
    });
  }

  const plain = new Error("host failure");
  assert.throws(() => harness({ invoke: () => { throw plain; } }).bind(selected)(1, 2),
    error => error === plain);
  const accessor = Object.defineProperty(new Error("bad"), "payload", { get() { return "bad"; } });
  assert.throws(() => harness({ invoke: () => { throw accessor; } }).bind(selected)(1, 2),
    /data property/);
});

test("omits val for empty top-level result cases", () => {
  const resultType = { kind: "result", ok: null, error: null };
  const selected = binding({
    coreSignature: { parameters: ["i32", "i32"], results: ["i32"] },
    result: resultType,
    canonicalSignature: {
      parameters: ["i32", "i32"], result: "i32", flatParameters: ["i32", "i32"],
      flatResults: ["i32"], indirectParameters: false, indirectResult: false,
    },
    resultMemory: memoryLayout(1, 1),
  });
  const ok = harness({ invoke: () => undefined });
  ok.bind(selected)(1, 2);
  assert.deepEqual({ ...ok.calls.at(-1)[1].value }, { tag: "ok" });
  const err = harness({ invoke: () => { throw undefined; } });
  err.bind(selected)(1, 2);
  assert.deepEqual({ ...err.calls.at(-1)[1].value }, { tag: "err" });
});

test("rejects invalid factories and provider binder products", () => {
  const { actions } = harness();
  for (const request of [null, [], {}, { ...actions, bindProvider: null }, { ...actions, extra: () => {} }]) {
    assert.throws(() => createRawCanonicalCallableBinder(request), TypeError);
  }
  const invalid = { ...actions, bindProvider: () => null };
  assert.throws(() => createRawCanonicalCallableBinder(invalid)(binding()), /provider callable/);
});

test("rejects malformed binding, parameter and signature shapes", () => {
  const { bind } = harness();
  const valid = binding();
  const invalid = [
    null, [], {}, { ...valid, extra: true },
    { ...valid, kind: "resource" }, { ...valid, target: "wasm128" },
    { ...valid, coreSignature: null },
    { ...valid, coreSignature: { parameters: ["v128"], results: [] } },
    { ...valid, coreSignature: { parameters: [], results: ["i32", "i32"] } },
    { ...valid, canonicalSignature: null },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, extra: true } },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, parameters: ["v128"] } },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, result: "v128" } },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, indirectResult: 1 } },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, parameters: ["i32"] } },
    { ...valid, parameters: null },
    { ...valid, parameters: [null] },
    { ...valid, parameters: [{ ...valid.parameters[0], extra: true }] },
    { ...valid, parameters: [{ ...valid.parameters[0], name: "" }] },
    { ...valid, parameters: [{ ...valid.parameters[0], javascriptName: "" }] },
    { ...valid, parameters: [{ ...valid.parameters[0], type: null }] },
    { ...valid, parameterMemory: null },
    { ...valid, parameterMemory: memoryLayout(-1, 4) },
    { ...valid, parameterMemory: memoryLayout(4, 0) },
    { ...valid, result: null },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, flatResults: ["i32", "i32"], indirectResult: false } },
    { ...valid, canonicalSignature: { ...valid.canonicalSignature, flatResults: ["i32", "i32"], indirectResult: true } },
    { ...valid, coreSignature: { parameters: ["i32", "i32"], results: [] }, canonicalSignature: { ...valid.canonicalSignature, result: null } },
    { ...valid, resultMemory: null, canonicalSignature: { ...valid.canonicalSignature, flatResults: ["i32", "i32"], indirectResult: true, result: null }, coreSignature: { parameters: ["i32", "i32", "i32"], results: [] } },
    { ...valid, parameters: [], canonicalSignature: { ...valid.canonicalSignature, indirectParameters: true, flatParameters: Array(17).fill("i32"), parameters: ["i32", "i32"] }, coreSignature: { parameters: ["i32", "i32"], results: ["i32"] } },
  ];
  invalid.forEach((candidate, index) => assert.throws(
    () => bind(candidate), TypeError, `invalid binding case ${index}`));
});

test("rejects malformed core invocations before provider entry", () => {
  let invoked = 0;
  const { bind } = harness({ invoke: () => { invoked += 1; return 0; } });
  const callable = bind(binding());
  for (const args of [[], [1], [1, 2, 3], [1.5, 2], [-0x8000_0001, 2], [0x8000_0000, 2]]) {
    assert.throws(() => callable(...args), TypeError);
  }
  for (const [kind, value] of [["i64", 1], ["f32", 1n], ["f64", "x"]]) {
    const selected = binding({
      coreSignature: { parameters: [kind], results: ["i32"] },
      parameters: [parameter("value", { kind: "u32" })],
      canonicalSignature: {
        parameters: [kind], result: "i32", flatParameters: [kind], flatResults: ["i32"],
        indirectParameters: false, indirectResult: false,
      },
      parameterMemory: memoryLayout(),
    });
    assert.throws(() => bind(selected)(value), TypeError);
  }
  assert.equal(invoked, 0);
});

test("rejects invalid lifted parameters and lowered result products", () => {
  assert.throws(() => harness({ lifted: null }).bind(binding())(1, 2), /lifted/);
  assert.throws(() => harness({ lifted: [1] }).bind(binding())(1, 2), /lifted/);
  assert.throws(() => harness({ lowered: null }).bind(binding())(1, 2), /lowered/);
  assert.throws(() => harness({ lowered: [] }).bind(binding())(1, 2), /lowered/);
});

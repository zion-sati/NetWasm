import assert from "node:assert/strict";
import test from "node:test";
import {
  bindRawInstanceBoundary,
  createRawInstanceBoundary,
  readRawInstanceMemory,
  readRawInstanceReallocator,
  releaseRawExportedResource,
  requireRawInstanceFunction,
} from "./raw-instance-boundary.mjs";

const resource = (overrides = {}) => ({
  destructor: "cm32p2|sample:raw/api@1|file_dtor",
  interface: "sample:raw@1/api",
  intrinsic: "exported-resource-new",
  javascriptName: "File",
  name: "file",
  type: 9,
  ...overrides,
});

function instance(target, calls = []) {
  const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
  const exports = {
    [`${prefix}_memory`]: new WebAssembly.Memory({ initial: 1 }),
    [`${prefix}_realloc`](...values) { calls.push(["reallocate", this, values]); return 8; },
    [`${prefix}|sample:raw/api@1|file_dtor`](value) { calls.push(["destroy", this, value]); },
  };
  return { exports };
}

test("binds the exact wasm32 memory, reallocator and required destructor once", () => {
  const calls = [];
  const boundary = createRawInstanceBoundary({ target: "wasm32" });
  const value = instance("wasm32", calls);
  requireRawInstanceFunction({ boundary, name: resource().destructor });
  requireRawInstanceFunction({ boundary, name: resource().destructor });
  bindRawInstanceBoundary({ boundary, instance: value });
  assert.equal(readRawInstanceMemory({ boundary }), value.exports.cm32p2_memory);
  assert.equal(readRawInstanceReallocator({ boundary })(0, 0, 1, 1), 8);
  releaseRawExportedResource({ boundary, resource: resource(), value: -17 });
  assert.deepEqual(calls.map(call => [call[0], call.at(-1)]), [
    ["reallocate", [0, 0, 1, 1]],
    ["destroy", -17],
  ]);
  assert.equal(calls[0][1], value.exports);
  assert.equal(calls[1][1], value.exports);
  assert.throws(() => bindRawInstanceBoundary({ boundary, instance: value }), /already bound/);
  assert.throws(() => requireRawInstanceFunction({ boundary, name: "later" }), /closed/);
});

test("selects only the exact wasm64 canonical exports", () => {
  const boundary = createRawInstanceBoundary({ target: "wasm64" });
  const value = instance("wasm64");
  const destructor = "cm64p2|sample:raw/api@1|file_dtor";
  requireRawInstanceFunction({ boundary, name: destructor });
  bindRawInstanceBoundary({ boundary, instance: value });
  assert.equal(readRawInstanceMemory({ boundary }), value.exports.cm64p2_memory);
  assert.equal(typeof readRawInstanceReallocator({ boundary }), "function");
  releaseRawExportedResource({
    boundary,
    resource: resource({ destructor }),
    value: 0x7fff_ffff,
  });
});

test("rejects invalid factories, requirements and read requests", () => {
  for (const request of [null, [], {}, { target: "wasm128" }, { target: "wasm32", extra: true }]) {
    assert.throws(() => createRawInstanceBoundary(request), TypeError);
  }
  const boundary = createRawInstanceBoundary({ target: "wasm32" });
  for (const request of [null, [], {}, { boundary: {} }, { boundary, name: "" }, { boundary, name: null }, { boundary, name: "x", extra: true }]) {
    assert.throws(() => requireRawInstanceFunction(request), TypeError);
  }
  for (const read of [readRawInstanceMemory, readRawInstanceReallocator]) {
    assert.throws(() => read(), TypeError);
    assert.throws(() => read({ boundary: {} }), TypeError);
    assert.throws(() => read({ boundary, extra: true }), TypeError);
    assert.throws(() => read({ boundary }), /not bound/);
  }
});

test("fails instance validation atomically", () => {
  const realInstanceWithoutRequiredExports = new WebAssembly.Instance(
    new WebAssembly.Module(Buffer.from("AGFzbQEAAAA=", "base64")));
  const cases = [
    null,
    [],
    {},
    { boundary: {}, instance: {} },
  ];
  for (const request of cases) assert.throws(() => bindRawInstanceBoundary(request), TypeError);

  const attempts = [
    null,
    {},
    { exports: null },
    realInstanceWithoutRequiredExports,
    Object.defineProperty({}, "exports", { get() { return {}; } }),
    { exports: {} },
    { exports: { cm32p2_memory: {} } },
    { exports: {
      cm32p2_memory: new WebAssembly.Memory({ initial: 1 }),
      get cm32p2_realloc() { return () => 0; },
    } },
    { exports: {
      cm32p2_memory: new WebAssembly.Memory({ initial: 1 }),
      cm32p2_realloc: 1,
    } },
  ];
  for (const candidate of attempts) {
    const boundary = createRawInstanceBoundary({ target: "wasm32" });
    assert.throws(() => bindRawInstanceBoundary({ boundary, instance: candidate }), TypeError);
    assert.throws(() => readRawInstanceMemory({ boundary }), /not bound/);
  }

  for (const invalid of [undefined, 1, Object.defineProperty({}, "required", { get() { return () => {}; } })]) {
    const boundary = createRawInstanceBoundary({ target: "wasm32" });
    requireRawInstanceFunction({ boundary, name: "required" });
    const value = instance("wasm32");
    if (invalid === undefined) delete value.exports.required;
    else Object.defineProperty(value.exports, "required", Object.getOwnPropertyDescriptor(
      invalid,
      "required") ?? { value: invalid, enumerable: true });
    assert.throws(() => bindRawInstanceBoundary({ boundary, instance: value }), TypeError);
    assert.throws(() => readRawInstanceMemory({ boundary }), /not bound/);
  }
});

test("rejects invalid exported-resource release requests", () => {
  const boundary = createRawInstanceBoundary({ target: "wasm32" });
  requireRawInstanceFunction({ boundary, name: resource().destructor });
  bindRawInstanceBoundary({ boundary, instance: instance("wasm32") });
  for (const request of [
    null,
    [],
    {},
    { boundary: {}, resource: resource(), value: 1 },
    { boundary, resource: null, value: 1 },
    { boundary, resource: { ...resource(), extra: true }, value: 1 },
    { boundary, resource: resource({ destructor: null }), value: 1 },
    { boundary, resource: resource({ intrinsic: "imported-resource-drop" }), value: 1 },
    { boundary, resource: resource(), value: 1.5 },
    { boundary, resource: resource(), value: -0x8000_0001 },
    { boundary, resource: resource(), value: 0x8000_0000 },
    { boundary, resource: resource({ destructor: "other" }), value: 1 },
  ]) assert.throws(() => releaseRawExportedResource(request), TypeError);

  const unbound = createRawInstanceBoundary({ target: "wasm32" });
  assert.throws(() => releaseRawExportedResource({
    boundary: unbound,
    resource: resource(),
    value: 1,
  }), /not bound/);
});

test("preserves guest destructor failures after binding", () => {
  const boundary = createRawInstanceBoundary({ target: "wasm32" });
  const failure = new Error("guest destructor failed");
  const value = instance("wasm32");
  value.exports[resource().destructor] = () => { throw failure; };
  requireRawInstanceFunction({ boundary, name: resource().destructor });
  bindRawInstanceBoundary({ boundary, instance: value });
  assert.throws(() => releaseRawExportedResource({
    boundary,
    resource: resource(),
    value: 1,
  }), error => error === failure);
});

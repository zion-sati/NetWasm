import assert from "node:assert/strict";
import test from "node:test";
import { createRawCanonicalResourceBinder } from "./raw-canonical-resource-binder.mjs";
import {
  borrowRawResource,
  createRawResourceStore,
  dropRawResource,
  registerRawResource,
} from "./raw-resource-store.mjs";

const binding = (intrinsic, overrides = {}) => ({
  kind: "resource",
  target: "wasm32",
  physical: { module: "cm32p2|sample:raw/api@1", name: "file_drop" },
  coreSignature: {
    parameters: ["i32"],
    results: intrinsic === "exported-resource-new" || intrinsic === "exported-resource-rep"
      ? ["i32"]
      : [],
  },
  resource: {
    destructor: intrinsic === "imported-resource-drop"
      ? null
      : "cm32p2|sample:raw/api@1|file_dtor",
    interface: "sample:raw@1/api",
    type: 9,
    name: "file",
    javascriptName: "File",
    intrinsic,
  },
  ...overrides,
});

const create = (store = createRawResourceStore(), released = [], required = []) => ({
  bind: createRawCanonicalResourceBinder({
    borrowResource: borrowRawResource,
    dropResource: dropRawResource,
    readStore: () => store,
    registerResource: registerRawResource,
    requireInstanceFunction: name => required.push(name),
    releaseExportedResource: request => released.push(request),
  }),
  required,
  released,
  store,
});

test("drops imported resources through their registered release action", () => {
  const { bind, required, store } = create();
  const released = [];
  const handle = registerRawResource({ release: value => released.push(value), store, type: 9, value: "host" });
  const drop = bind(binding("imported-resource-drop"));
  assert.equal(drop(handle), undefined);
  assert.deepEqual(released, ["host"]);
  assert.deepEqual(required, []);
  assert.throws(() => borrowRawResource({ handle, store, type: 9 }), /unavailable/);
});

test("creates, observes and drops exported resource representations", () => {
  const { bind, released, required, store } = create();
  const createResource = bind(binding("exported-resource-new"));
  const representation = bind(binding("exported-resource-rep"));
  const drop = bind(binding("exported-resource-drop"));
  const handle = createResource(-17);
  assert.equal(representation(handle), -17);
  drop(handle);
  assert.deepEqual(released, [{
    resource: {
      destructor: "cm32p2|sample:raw/api@1|file_dtor",
      interface: "sample:raw@1/api",
      intrinsic: "exported-resource-new",
      javascriptName: "File",
      name: "file",
      type: 9,
    },
    value: -17,
  }]);
  assert.deepEqual(required, Array(3).fill("cm32p2|sample:raw/api@1|file_dtor"));
  assert.throws(() => borrowRawResource({ handle, store, type: 9 }), /unavailable/);
});

test("normalizes the full unsigned registered handle range to core i32", () => {
  const { released, store } = create();
  const bind = createRawCanonicalResourceBinder({
    borrowResource: borrowRawResource,
    dropResource: dropRawResource,
    readStore: () => store,
    registerResource: () => 0xffff_ffff,
    requireInstanceFunction() {},
    releaseExportedResource: request => released.push(request),
  });
  assert.equal(bind(binding("exported-resource-new"))(1), -1);
});

test("rejects invalid factories", () => {
  const action = () => {};
  const valid = {
    borrowResource: action,
    dropResource: action,
    readStore: action,
    registerResource: action,
    requireInstanceFunction: action,
    releaseExportedResource: action,
  };
  for (const request of [null, [], {}, { ...valid, borrowResource: null }, { ...valid, extra: action }]) {
    assert.throws(() => createRawCanonicalResourceBinder(request), TypeError);
  }
});

test("rejects malformed resource binding identities and signatures", () => {
  const { bind } = create();
  for (const candidate of [
    null,
    [],
    {},
    { ...binding("imported-resource-drop"), extra: true },
    binding("imported-resource-drop", { kind: "callable" }),
    binding("imported-resource-drop", { target: "wasm128" }),
    binding("imported-resource-drop", { resource: null }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, extra: true } }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, interface: null } }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, name: "" } }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, javascriptName: "" } }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, type: -1 } }),
    binding("imported-resource-drop", { resource: { ...binding("imported-resource-drop").resource, destructor: "dtor" } }),
    binding("exported-resource-new", { resource: { ...binding("exported-resource-new").resource, destructor: null } }),
    binding("unknown"),
    binding("imported-resource-drop", { coreSignature: null }),
    binding("imported-resource-drop", { coreSignature: { parameters: ["i64"], results: [] } }),
    binding("exported-resource-new", { coreSignature: { parameters: ["i32"], results: [] } }),
  ]) assert.throws(() => bind(candidate), TypeError);
});

test("rejects invalid core values and resource action products", () => {
  const { bind, store } = create();
  for (const value of [undefined, 1.5, -0x8000_0001, 0x8000_0000, 1n]) {
    assert.throws(() => bind(binding("imported-resource-drop"))(value), TypeError);
    assert.throws(() => bind(binding("exported-resource-new"))(value), TypeError);
  }
  const handle = registerRawResource({ release: null, store, type: 9, value: 1n });
  assert.throws(() => bind(binding("exported-resource-rep"))(handle), TypeError);

  for (const value of [0, -1, 0x1_0000_0000, 1.5]) {
    const invalid = createRawCanonicalResourceBinder({
      borrowResource: borrowRawResource,
      dropResource: dropRawResource,
      readStore: () => store,
      registerResource: () => value,
      requireInstanceFunction() {},
      releaseExportedResource() {},
    });
    assert.throws(() => invalid(binding("exported-resource-new"))(1), TypeError);
  }
});

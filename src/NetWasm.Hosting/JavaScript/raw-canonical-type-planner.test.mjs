import assert from "node:assert/strict";
import test from "node:test";
import {
  flattenRawCanonicalType,
  planRawCanonicalTypeLayout,
} from "./raw-canonical-type-planner.mjs";

const primitive = kind => ({ kind });
const resource = kind => ({ kind, resourceType: 7 });
const field = (name, type, javascriptName) => javascriptName === undefined
  ? ({ name, type })
  : ({ name, javascriptName, type });
const variantCase = (name, type = null) => ({ name, type });
const flag = (name, javascriptName = name) => ({ name, javascriptName });
const layout = (type, target = "wasm32") => planRawCanonicalTypeLayout({ target, type });
const flatten = (type, target = "wasm32") => flattenRawCanonicalType({ target, type });

test("plans every scalar and pointer-pair layout at both target widths", () => {
  const scalars = [
    ["unit", 0, 1], ["bool", 1, 1], ["s8", 1, 1], ["u8", 1, 1],
    ["s16", 2, 2], ["u16", 2, 2], ["s32", 4, 4], ["u32", 4, 4],
    ["s64", 8, 8], ["u64", 8, 8], ["f32", 4, 4], ["f64", 8, 8],
    ["character", 4, 4],
  ];
  for (const [kind, size, alignment] of scalars) {
    const actual = layout(primitive(kind));
    assert.deepEqual(actual, {
      size,
      alignment,
      discriminantSize: 0,
      payloadOffset: 0,
      fields: [],
    });
    assert.equal(Object.isFrozen(actual), true);
    assert.equal(Object.isFrozen(actual.fields), true);
  }
  for (const kind of ["owned-resource", "borrowed-resource"]) {
    assert.deepEqual(layout(resource(kind)), {
      size: 4,
      alignment: 4,
      discriminantSize: 0,
      payloadOffset: 0,
      fields: [],
    });
  }
  for (const kind of ["text", "list"]) {
    const type = kind === "text" ? primitive(kind) : { kind, element: primitive("u8") };
    assert.equal(layout(type, "wasm32").size, 8);
    assert.equal(layout(type, "wasm32").alignment, 4);
    assert.equal(layout(type, "wasm64").size, 16);
    assert.equal(layout(type, "wasm64").alignment, 8);
  }
  assert.equal(layout({ kind: "alias", element: primitive("u16") }).size, 2);
});

test("plans exact record and tuple field offsets", () => {
  const record = {
    kind: "record",
    fields: [
      field("small", primitive("u8"), "small"),
      field("large-value", primitive("u32"), "largeValue"),
      field("tail", primitive("u16"), "tail"),
    ],
  };
  const actual = layout(record);
  assert.equal(actual.size, 12);
  assert.equal(actual.alignment, 4);
  assert.deepEqual(actual.fields.map(item => [item.name, item.offset, item.layout.size]), [
    ["small", 0, 1], ["large-value", 4, 4], ["tail", 8, 2],
  ]);
  assert.equal(Object.isFrozen(actual.fields), true);
  assert.equal(actual.fields.every(Object.isFrozen), true);

  const tuple = {
    kind: "tuple",
    fields: [field("item0", primitive("u16")), field("item1", primitive("u64"))],
  };
  assert.deepEqual(layout(tuple), {
    size: 16,
    alignment: 8,
    discriminantSize: 0,
    payloadOffset: 0,
    fields: [
      { name: "item0", offset: 0, layout: layout(primitive("u16")) },
      { name: "item1", offset: 8, layout: layout(primitive("u64")) },
    ],
  });
  assert.equal(layout({ kind: "record", fields: [] }).size, 0);
});

test("plans option, result, variant, enum and flags layouts", () => {
  assert.deepEqual(layout({ kind: "option", element: primitive("u64") }), {
    size: 16,
    alignment: 8,
    discriminantSize: 1,
    payloadOffset: 8,
    fields: [],
  });
  assert.deepEqual(layout({ kind: "result", ok: null, error: primitive("text") }, "wasm64"), {
    size: 24,
    alignment: 8,
    discriminantSize: 1,
    payloadOffset: 8,
    fields: [],
  });
  assert.deepEqual(layout({
    kind: "variant",
    cases: [variantCase("none"), variantCase("small", primitive("u16")),
      variantCase("large", primitive("f64"))],
  }), {
    size: 16,
    alignment: 8,
    discriminantSize: 1,
    payloadOffset: 8,
    fields: [],
  });

  const enumOf = count => ({
    kind: "enum",
    cases: Array.from({ length: count }, (_, index) => variantCase(`case-${index}`)),
  });
  assert.equal(layout(enumOf(2)).size, 1);
  assert.equal(layout(enumOf(257)).size, 2);
  assert.equal(layout(enumOf(65_537)).size, 4);

  for (const [count, size, alignment] of [
    [0, 1, 1], [8, 1, 1], [9, 2, 2], [16, 2, 2], [17, 4, 4], [33, 8, 4],
  ]) {
    const flags = Array.from({ length: count }, (_, index) => flag(`flag-${index}`));
    assert.deepEqual(layout({ kind: "flags", count, flags }), {
      size,
      alignment,
      discriminantSize: 0,
      payloadOffset: 0,
      fields: [],
    });
  }
});

test("flattens every supported kind with target-aware pointers and multiword flags", () => {
  const i32Kinds = [
    "bool", "s8", "u8", "s16", "u16", "s32", "u32", "character",
  ];
  for (const kind of i32Kinds) assert.deepEqual(flatten(primitive(kind)), ["i32"]);
  for (const kind of ["owned-resource", "borrowed-resource"]) {
    assert.deepEqual(flatten(resource(kind)), ["i32"]);
  }
  assert.deepEqual(flatten(primitive("unit")), []);
  assert.deepEqual(flatten(primitive("s64")), ["i64"]);
  assert.deepEqual(flatten(primitive("u64")), ["i64"]);
  assert.deepEqual(flatten(primitive("f32")), ["f32"]);
  assert.deepEqual(flatten(primitive("f64")), ["f64"]);
  assert.deepEqual(flatten(primitive("text")), ["i32", "i32"]);
  assert.deepEqual(flatten(primitive("text"), "wasm64"), ["i64", "i64"]);
  assert.deepEqual(flatten({ kind: "list", element: primitive("u8") }), ["i32", "i32"]);
  assert.deepEqual(flatten({ kind: "alias", element: primitive("u16") }), ["i32"]);
  assert.deepEqual(flatten({
    kind: "record",
    fields: [field("left", primitive("u16"), "left"),
      field("right", primitive("f64"), "right")],
  }), ["i32", "f64"]);
  assert.deepEqual(flatten({
    kind: "tuple",
    fields: [field("item0", primitive("u64")), field("item1", primitive("f32"))],
  }), ["i64", "f32"]);
  assert.deepEqual(flatten({ kind: "option", element: primitive("u32") }), ["i32", "i32"]);
  assert.deepEqual(flatten({ kind: "result", ok: null, error: primitive("f64") }), ["i32", "f64"]);
  assert.deepEqual(flatten({ kind: "enum", cases: [variantCase("a"), variantCase("b")] }), ["i32"]);
  assert.deepEqual(flatten({ kind: "flags", count: 0, flags: [] }), ["i32"]);
  assert.deepEqual(flatten({
    kind: "flags",
    count: 33,
    flags: Array.from({ length: 33 }, (_, index) => flag(`f-${index}`)),
  }), ["i32", "i32"]);
});

test("joins variant payload slots using the canonical core-type lattice", () => {
  const tuple = types => ({
    kind: "tuple",
    fields: types.map((type, index) => field(`item${index}`, type)),
  });
  const type = {
    kind: "variant",
    cases: [
      variantCase("first", tuple([
        primitive("u32"), primitive("u64"), primitive("f32"), primitive("f64"),
      ])),
      variantCase("second", tuple([
        primitive("f32"), primitive("f32"), primitive("f64"), primitive("u32"),
      ])),
      variantCase("third", tuple([
        primitive("u32"), primitive("u64"), primitive("f32"), primitive("f64"),
      ])),
      variantCase("empty"),
    ],
  };
  assert.deepEqual(flatten(type), ["i32", "i32", "i64", "f64", "i64"]);
  assert.deepEqual(flatten({
    kind: "variant",
    cases: [variantCase("float", primitive("f32")),
      variantCase("integer", primitive("u32"))],
  }), ["i32", "i32"]);
});

test("rejects malformed requests and type descriptors without invoking accessors", () => {
  for (const request of [null, [], {}, { target: "wasm32", type: primitive("u8"), extra: true },
    { target: "wasm32", wrong: primitive("u8") },
    { target: "wasm32", type: primitive("u8"), [Symbol("bad")]: true },
    Object.defineProperty({ target: "wasm32", type: primitive("u8") }, "type", {
      get: () => primitive("u8"),
      enumerable: true,
    })]) {
    assert.throws(() => planRawCanonicalTypeLayout(request), /planning/);
  }
  assert.throws(() => layout(primitive("u8"), "wasm128"), /target/);
  for (const type of [null, [], primitive("future"), { kind: "u8", extra: true },
    { kind: "u8", [Symbol("bad")]: true },
    Object.defineProperty({}, "kind", { get() { throw new Error("observed"); }, enumerable: true })]) {
    assert.throws(() => layout(type), /type/);
  }
});

test("rejects malformed fields, cases, flags, resources and nested types", () => {
  for (const fields of [null, [null], [field("", primitive("u8"), "value")],
    [field("value", primitive("u8"), "")],
    [field("same", primitive("u8"), "one"), field("same", primitive("u16"), "two")],
    [field("one", primitive("u8"), "same"), field("two", primitive("u16"), "same")],
    [{ name: "value", javascriptName: "value", type: primitive("u8"), extra: true }]]) {
    assert.throws(() => layout({ kind: "record", fields }), /field/);
  }
  assert.throws(() => layout({
    kind: "tuple",
    fields: [{ name: "item0", javascriptName: "item0", type: primitive("u8") }],
  }), /field/);

  for (const cases of [null, [], [null], [variantCase("")],
    [variantCase("same"), variantCase("same")],
    [{ name: "value", type: null, extra: true }]]) {
    assert.throws(() => layout({ kind: "variant", cases }), /case/);
  }
  assert.throws(() => layout({
    kind: "enum",
    cases: [variantCase("value", primitive("u8"))],
  }), /enum case/);

  for (const type of [
    { kind: "flags", count: -1, flags: [] },
    { kind: "flags", count: 0.5, flags: [] },
    { kind: "flags", count: 1, flags: [] },
    { kind: "flags", count: 1, flags: [null] },
    { kind: "flags", count: 1, flags: [flag("")] },
    { kind: "flags", count: 1, flags: [flag("value", "")] },
    { kind: "flags", count: 2, flags: [flag("same", "one"), flag("same", "two")] },
    { kind: "flags", count: 2, flags: [flag("one", "same"), flag("two", "same")] },
    { kind: "flags", count: 1, flags: [{ ...flag("value"), extra: true }] },
  ]) {
    assert.throws(() => layout(type), /flag/);
  }

  for (const type of [
    { kind: "owned-resource", resourceType: -1 },
    { kind: "borrowed-resource", resourceType: 0.5 },
  ]) {
    assert.throws(() => layout(type), /resource type/);
  }
  assert.throws(() => layout({ kind: "alias", element: primitive("future") }), /unsupported/);
  assert.throws(() => flatten({ kind: "list", element: primitive("future") }), /unsupported/);
});

import assert from "node:assert/strict";
import test from "node:test";
import {
  writeRawCanonicalMemoryScalar,
} from "./raw-canonical-scalar-codecs.mjs";
import {
  planRawCanonicalTypeLayout,
} from "./raw-canonical-type-planner.mjs";
import {
  borrowRawResource,
  createRawResourceStore,
  registerRawResource,
  transferRawResource,
} from "./raw-resource-store.mjs";
import {
  createRawCanonicalFlatValueLifter,
  createRawCanonicalMemoryValueReader,
} from "./raw-canonical-value-lifter.mjs";

const capabilities = { borrowResource: borrowRawResource, transferResource: transferRawResource };
const flatLifter = createRawCanonicalFlatValueLifter(capabilities);
const memoryReader = createRawCanonicalMemoryValueReader(capabilities);
const primitive = kind => ({ kind });
const alias = element => ({ kind: "alias", element });
const field = (name, type, javascriptName) => javascriptName === undefined
  ? ({ name, type })
  : ({ name, javascriptName, type });
const variantCase = (name, type = null) => ({ name, type });
const flag = (name, javascriptName = name) => ({ name, javascriptName });
const resource = (kind, resourceType = 7) => ({ kind, resourceType });
const address = (value, target) => target === "wasm64" ? BigInt(value) : value;
const flat = (type, values, options = {}) => flatLifter({
  memory: options.memory ?? new WebAssembly.Memory({ initial: 1 }),
  store: options.store ?? createRawResourceStore(),
  target: options.target ?? "wasm32",
  type,
  values,
});
const read = (type, memory, offset = 0, options = {}) => memoryReader({
  address: address(offset, options.target ?? "wasm32"),
  memory,
  store: options.store ?? createRawResourceStore(),
  target: options.target ?? "wasm32",
  type,
});

test("creates immutable one-action lifter and reader Strategies", () => {
  assert.equal(typeof flatLifter, "function");
  assert.equal(typeof memoryReader, "function");
  assert.equal(Object.isFrozen(flatLifter), true);
  assert.equal(Object.isFrozen(memoryReader), true);
});

test("lifts every scalar, unit, alias, record and tuple from flat core values", () => {
  const cases = [
    [primitive("unit"), [], undefined],
    [primitive("bool"), [-1], true],
    [primitive("s8"), [-1], -1],
    [primitive("u8"), [-1], 255],
    [primitive("s16"), [-1], -1],
    [primitive("u16"), [-1], 65_535],
    [primitive("s32"), [-7], -7],
    [primitive("u32"), [-1], 0xffff_ffff],
    [primitive("s64"), [-7n], -7n],
    [primitive("u64"), [-1n], 0xffff_ffff_ffff_ffffn],
    [primitive("f32"), [1 / 3], Math.fround(1 / 3)],
    [primitive("f64"), [Math.PI], Math.PI],
    [primitive("character"), [0x1f680], "🚀"],
    [alias(primitive("u16")), [42], 42],
  ];
  for (const [type, values, expected] of cases) assert.equal(flat(type, values), expected);

  const record = {
    kind: "record",
    fields: [
      field("first-value", primitive("u16"), "firstValue"),
      field("__proto__", primitive("bool"), "__proto__"),
    ],
  };
  const recordValue = flat(record, [17, 1]);
  assert.equal(Object.getPrototypeOf(recordValue), Object.prototype);
  assert.deepEqual(Object.keys(recordValue), ["firstValue", "__proto__"]);
  assert.equal(recordValue.firstValue, 17);
  assert.equal(recordValue.__proto__, true);

  assert.deepEqual(flat({
    kind: "tuple",
    fields: [field("item0", primitive("s32")), field("item1", primitive("f64"))],
  }, [-3, 2.5]), [-3, 2.5]);
});

test("lifts UTF-8 text and stable list snapshots at both pointer widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    new TextEncoder().encodeInto("hello", new Uint8Array(memory.buffer, 64, 5));
    new Uint8Array(memory.buffer, 80, 4).set([1, 2, 3, 4]);
    const textValues = [address(64, target), address(5, target)];
    const listValues = [address(80, target), address(4, target)];
    assert.equal(flat(primitive("text"), textValues, { memory, target }), "hello");
    const bytes = flat({ kind: "list", element: alias(primitive("u8")) }, listValues,
      { memory, target });
    assert.equal(bytes instanceof Uint8Array, true);
    assert.deepEqual([...bytes], [1, 2, 3, 4]);
    new Uint8Array(memory.buffer, 80, 4).fill(9);
    assert.deepEqual([...bytes], [1, 2, 3, 4]);
  }
});

test("lifts flat options, results, variants, enums and multiword flags", () => {
  const option = { kind: "option", element: primitive("u32") };
  assert.equal(flat(option, [0, 99]), undefined);
  assert.equal(flat(option, [1, 42]), 42);

  const nested = { kind: "option", element: option };
  assert.deepEqual(flat(nested, [0, 0, 0]), { tag: "none" });
  assert.deepEqual(flat(nested, [1, 0, 0]), { tag: "some", val: undefined });
  assert.deepEqual(flat(nested, [1, 1, 42]), { tag: "some", val: 42 });

  const result = { kind: "result", ok: primitive("u16"), error: null };
  assert.deepEqual(flat(result, [0, 17]), { tag: "ok", val: 17 });
  assert.deepEqual(flat(result, [1, 99]), { tag: "err" });

  const variant = {
    kind: "variant",
    cases: [variantCase("empty"), variantCase("value", primitive("s32"))],
  };
  assert.deepEqual(flat(variant, [0, 99]), { tag: "empty" });
  assert.deepEqual(flat(variant, [1, -7]), { tag: "value", val: -7 });
  assert.equal(flat({
    kind: "enum",
    cases: [variantCase("first"), variantCase("second")],
  }, [1]), "second");

  const flags = {
    kind: "flags",
    count: 33,
    flags: Array.from({ length: 33 }, (_, index) => flag(`flag-${index}`, `flag${index}`)),
  };
  const liftedFlags = flat(flags, [-1, 1]);
  assert.equal(Object.keys(liftedFlags).length, 33);
  assert.equal(liftedFlags.flag0, true);
  assert.equal(liftedFlags.flag31, true);
  assert.equal(liftedFlags.flag32, true);
});

test("coerces every joined flat variant payload representation", () => {
  const bits32 = value => {
    const view = new DataView(new ArrayBuffer(4));
    view.setFloat32(0, value, true);
    return view.getInt32(0, true);
  };
  const bits64 = value => {
    const view = new DataView(new ArrayBuffer(8));
    view.setFloat64(0, value, true);
    return view.getBigInt64(0, true);
  };
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("float", primitive("f32")), variantCase("int", primitive("u32"))],
  }, [0, bits32(1.5)]).val, 1.5);
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("int", primitive("u32"))],
  }, [1, -1n]).val, 0xffff_ffff);
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("float", primitive("f32"))],
  }, [1, BigInt(bits32(-2.5) >>> 0)]).val, -2.5);
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("float", primitive("f64"))],
  }, [1, bits64(Math.PI)]).val, Math.PI);
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("double", primitive("f64")), variantCase("float", primitive("f32"))],
  }, [1, 1 / 3]).val, Math.fround(1 / 3));
  assert.equal(flat({
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("wide-too", primitive("u64"))],
  }, [0, 42n]).val, 42n);
});

test("reads every scalar and alias from canonical memory at both widths", () => {
  const cases = [
    ["bool", true], ["s8", -7], ["u8", 250], ["s16", -30_000], ["u16", 60_000],
    ["s32", -2_000_000_000], ["u32", 4_000_000_000], ["s64", -7n], ["u64", 17n],
    ["f32", Math.fround(1 / 3)], ["f64", Math.PI], ["character", "🚀"],
  ];
  for (const target of ["wasm32", "wasm64"]) {
    for (const [kind, expected] of cases) {
      const memory = new WebAssembly.Memory({ initial: 1 });
      writeRawCanonicalMemoryScalar({
        address: address(0, target), kind, memory, target, value: expected,
      });
      assert.equal(read(primitive(kind), memory, 0, { target }), expected);
    }
    const memory = new WebAssembly.Memory({ initial: 1 });
    writeRawCanonicalMemoryScalar({
      address: address(0, target), kind: "u16", memory, target, value: 1234,
    });
    assert.equal(read(alias(primitive("u16")), memory, 0, { target }), 1234);
    assert.equal(read(primitive("unit"), memory, 0, { target }), undefined);
  }
});

test("reads text, byte lists and element lists from canonical memory", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    new TextEncoder().encodeInto("world", new Uint8Array(memory.buffer, 128, 5));
    writePointerPair(memory, target, 0, 128, 5);
    assert.equal(read(primitive("text"), memory, 0, { target }), "world");

    new Uint8Array(memory.buffer, 160, 3).set([4, 5, 6]);
    writePointerPair(memory, target, 16, 160, 3);
    const bytes = read({ kind: "list", element: primitive("u8") }, memory, 16, { target });
    assert.deepEqual([...bytes], [4, 5, 6]);

    const elements = { kind: "list", element: primitive("u16") };
    const view = new DataView(memory.buffer);
    view.setUint16(192, 0x1234, true);
    view.setUint16(194, 0xabcd, true);
    writePointerPair(memory, target, 32, 192, 2);
    assert.deepEqual(read(elements, memory, 32, { target }), [0x1234, 0xabcd]);
  }
});

test("reads records, tuples and every tagged aggregate from canonical memory", () => {
  const memory = new WebAssembly.Memory({ initial: 2 });
  const record = {
    kind: "record",
    fields: [field("small", primitive("u8"), "small"),
      field("large", primitive("u32"), "large")],
  };
  writeRawCanonicalMemoryScalar({
    address: 0, kind: "u8", memory, target: "wasm32", value: 7,
  });
  writeRawCanonicalMemoryScalar({
    address: 4, kind: "u32", memory, target: "wasm32", value: 0x1234_5678,
  });
  assert.deepEqual(read(record, memory), { small: 7, large: 0x1234_5678 });

  const tuple = {
    kind: "tuple",
    fields: [field("item0", primitive("u16")), field("item1", primitive("u64"))],
  };
  writeRawCanonicalMemoryScalar({
    address: 16, kind: "u16", memory, target: "wasm32", value: 9,
  });
  writeRawCanonicalMemoryScalar({
    address: 24, kind: "u64", memory, target: "wasm32", value: 10n,
  });
  assert.deepEqual(read(tuple, memory, 16), [9, 10n]);

  const option = { kind: "option", element: primitive("u32") };
  writeVariant(memory, option, 32, 0);
  assert.equal(read(option, memory, 32), undefined);
  writeVariant(memory, option, 32, 1, ["u32", 42]);
  assert.equal(read(option, memory, 32), 42);

  const nestedOption = { kind: "option", element: option };
  writeVariant(memory, nestedOption, 48, 0);
  assert.deepEqual(read(nestedOption, memory, 48), { tag: "none" });
  writeVariant(memory, nestedOption, 48, 1);
  const nestedLayout = planRawCanonicalTypeLayout({ target: "wasm32", type: nestedOption });
  writeVariant(memory, option, 48 + nestedLayout.payloadOffset, 0);
  assert.deepEqual(read(nestedOption, memory, 48), { tag: "some", val: undefined });

  const result = { kind: "result", ok: null, error: primitive("u16") };
  writeVariant(memory, result, 64, 0);
  assert.deepEqual(read(result, memory, 64), { tag: "ok" });
  writeVariant(memory, result, 64, 1, ["u16", 17]);
  assert.deepEqual(read(result, memory, 64), { tag: "err", val: 17 });

  const variant = {
    kind: "variant",
    cases: [variantCase("empty"), variantCase("number", primitive("u64"))],
  };
  writeVariant(memory, variant, 80, 1, ["u64", 99n]);
  assert.deepEqual(read(variant, memory, 80), { tag: "number", val: 99n });
});

test("reads every discriminant and flags storage width", () => {
  const memory = new WebAssembly.Memory({ initial: 2 });
  const enumOf = count => ({
    kind: "enum",
    cases: Array.from({ length: count }, (_, index) => variantCase(`case-${index}`)),
  });
  const view = new DataView(memory.buffer);
  view.setUint8(0, 1);
  assert.equal(read(enumOf(2), memory, 0), "case-1");
  view.setUint16(2, 256, true);
  assert.equal(read(enumOf(257), memory, 2), "case-256");
  view.setUint32(4, 65_536, true);
  assert.equal(read(enumOf(65_537), memory, 4), "case-65536");

  for (const [offset, count, bytes] of [
    [16, 8, [0x81]],
    [18, 9, [0x01, 0x01]],
    [24, 33, [0x01, 0, 0, 0, 0x01, 0, 0, 0]],
  ]) {
    new Uint8Array(memory.buffer, offset, bytes.length).set(bytes);
    const type = {
      kind: "flags",
      count,
      flags: Array.from({ length: count }, (_, index) => flag(`flag-${index}`, `flag${index}`)),
    };
    const value = read(type, memory, offset);
    assert.equal(value.flag0, true);
    assert.equal(value[`flag${count - 1}`], true);
  }
});

test("commits owned resource transfers only after complete successful lifting", () => {
  const store = createRawResourceStore();
  const ownedValue = { name: "owned" };
  const borrowedValue = { name: "borrowed" };
  const ownedHandle = registerRawResource({ release: null, store, type: 7, value: ownedValue });
  const borrowedHandle = registerRawResource({ release: null, store, type: 7, value: borrowedValue });
  const type = {
    kind: "tuple",
    fields: [field("item0", resource("owned-resource")),
      field("item1", resource("borrowed-resource"))],
  };
  assert.deepEqual(flat(type, [ownedHandle, borrowedHandle], { store }), [ownedValue, borrowedValue]);
  assert.throws(() => borrowRawResource({ handle: ownedHandle, store, type: 7 }), /unavailable/);
  assert.equal(borrowRawResource({ handle: borrowedHandle, store, type: 7 }), borrowedValue);

  const retainedStore = createRawResourceStore();
  const retained = { name: "retained" };
  const retainedHandle = registerRawResource({ release: null, store: retainedStore, type: 7, value: retained });
  const invalidAfterOwned = {
    kind: "tuple",
    fields: [field("item0", resource("owned-resource")), field("item1", {
      kind: "enum", cases: [variantCase("only")],
    })],
  };
  assert.throws(() => flat(invalidAfterOwned, [retainedHandle, 1], { store: retainedStore }),
    /enum discriminant/);
  assert.equal(borrowRawResource({ handle: retainedHandle, store: retainedStore, type: 7 }), retained);
  assert.throws(() => flat({
    kind: "tuple",
    fields: [field("item0", resource("owned-resource")),
      field("item1", resource("owned-resource"))],
  }, [retainedHandle, retainedHandle], { store: retainedStore }), /duplicated/);
  assert.equal(borrowRawResource({ handle: retainedHandle, store: retainedStore, type: 7 }), retained);
});

test("reads resources transactionally from canonical memory", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const store = createRawResourceStore();
  const owned = { name: "owned" };
  const borrowed = { name: "borrowed" };
  const ownedHandle = registerRawResource({ release: null, store, type: 7, value: owned });
  const borrowedHandle = registerRawResource({ release: null, store, type: 7, value: borrowed });
  writeRawCanonicalMemoryScalar({
    address: 0, kind: "u32", memory, target: "wasm32", value: ownedHandle,
  });
  writeRawCanonicalMemoryScalar({
    address: 4, kind: "u32", memory, target: "wasm32", value: borrowedHandle,
  });
  const type = {
    kind: "record",
    fields: [field("owned", resource("owned-resource"), "owned"),
      field("borrowed", resource("borrowed-resource"), "borrowed")],
  };
  assert.deepEqual(read(type, memory, 0, { store }), { owned, borrowed });
  assert.throws(() => borrowRawResource({ handle: ownedHandle, store, type: 7 }), /unavailable/);
  assert.equal(borrowRawResource({ handle: borrowedHandle, store, type: 7 }), borrowed);

  const retainedStore = createRawResourceStore();
  const retained = { name: "retained" };
  const retainedHandle = registerRawResource({
    release: null, store: retainedStore, type: 7, value: retained,
  });
  writeRawCanonicalMemoryScalar({
    address: 0, kind: "u32", memory, target: "wasm32", value: retainedHandle,
  });
  new DataView(memory.buffer).setUint8(4, 1);
  const invalid = {
    kind: "record",
    fields: [
      field("owned", resource("owned-resource"), "owned"),
      field("state", { kind: "enum", cases: [variantCase("only")] }, "state"),
    ],
  };
  assert.throws(() => read(invalid, memory, 0, { store: retainedStore }), /enum discriminant/);
  assert.equal(borrowRawResource({ handle: retainedHandle, store: retainedStore, type: 7 }), retained);
});

test("reacquires memory grown by a resource capability during nested lifting", () => {
  const memory = new WebAssembly.Memory({ initial: 1, maximum: 2 });
  const store = createRawResourceStore();
  const value = { name: "borrowed" };
  const handle = registerRawResource({ release: null, store, type: 7, value });
  writeRawCanonicalMemoryScalar({
    address: 0, kind: "u32", memory, target: "wasm32", value: handle,
  });
  writeRawCanonicalMemoryScalar({
    address: 4, kind: "u32", memory, target: "wasm32", value: 0x1234_5678,
  });
  const reader = createRawCanonicalMemoryValueReader({
    borrowResource(request) {
      memory.grow(1);
      return borrowRawResource(request);
    },
    transferResource: transferRawResource,
  });
  const type = {
    kind: "record",
    fields: [
      field("borrowed", resource("borrowed-resource"), "borrowed"),
      field("number", primitive("u32"), "number"),
    ],
  };
  assert.deepEqual(reader({
    address: 0,
    memory,
    store,
    target: "wasm32",
    type,
  }), { borrowed: value, number: 0x1234_5678 });
  assert.equal(memory.buffer.byteLength, 2 * 65_536);
});

test("rejects invalid ranges, encodings, lengths and discriminants without partial values", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint8Array(memory.buffer, 64, 2).set([0xc3, 0x28]);
  assert.throws(() => flat(primitive("text"), [64, 2], { memory }), /UTF-8/);
  assert.throws(() => flat(primitive("text"), [64, 0x1000_0000], { memory }), /too large/);
  assert.throws(() => flat({ kind: "list", element: primitive("u64") },
    [64, 0x0fff_ffff], { memory }), /byte length/);
  assert.throws(() => flat({ kind: "list", element: primitive("unit") }, [64, 1], { memory }),
    /zero-sized/);
  assert.throws(() => flat(primitive("text"), [65_535, 2], { memory }), /out of bounds/);
  assert.throws(() => flat({
    kind: "variant", cases: [variantCase("only")],
  }, [1]), /variant discriminant/);
  assert.throws(() => flat({
    kind: "enum", cases: [variantCase("only")],
  }, [1]), /enum discriminant/);

  const option = { kind: "option", element: primitive("u32") };
  writeVariant(memory, option, 0, 2);
  assert.throws(() => read(option, memory), /variant discriminant/);
  const enumeration = { kind: "enum", cases: [variantCase("only")] };
  new DataView(memory.buffer).setUint8(0, 1);
  assert.throws(() => read(enumeration, memory), /enum discriminant/);
});

test("rejects malformed factories, action requests and dense core arrays", () => {
  for (const factory of [null, [], {}, { ...capabilities, extra: true },
    { borrowResource: borrowRawResource, wrong: transferRawResource },
    { ...capabilities, [Symbol("bad")]: true },
    Object.defineProperty({ ...capabilities }, "borrowResource", {
      get: () => borrowRawResource,
      enumerable: true,
    }),
    { borrowResource: null, transferResource: transferRawResource },
    { borrowResource: borrowRawResource, transferResource: null }]) {
    assert.throws(() => createRawCanonicalFlatValueLifter(factory), /lifter/);
  }

  const validFlat = {
    memory: new WebAssembly.Memory({ initial: 1 }),
    store: createRawResourceStore(),
    target: "wasm32",
    type: primitive("u8"),
    values: [1],
  };
  for (const request of [null, [], {}, { ...validFlat, extra: true },
    { memory: validFlat.memory, store: validFlat.store, target: "wasm32",
      type: primitive("u8"), wrong: [1] },
    { ...validFlat, [Symbol("bad")]: true },
    Object.defineProperty({ ...validFlat }, "values", { get: () => [1], enumerable: true })]) {
    assert.throws(() => flatLifter(request), /lift/);
  }
  assert.throws(() => flatLifter({ ...validFlat, memory: {} }), /memory/);

  for (const values of [null, [], [1, 2], Array(1),
    Object.assign([1], { extra: true }),
    Object.assign([1], { [Symbol("bad")]: true }),
    Object.defineProperty([1], "0", { get: () => 1, enumerable: true })]) {
    assert.throws(() => flatLifter({ ...validFlat, values }), /core value/);
  }
  for (const [type, value] of [
    [primitive("u32"), 0x8000_0000], [primitive("u64"), 0],
    [primitive("f32"), "x"], [primitive("f64"), 1n],
  ]) {
    assert.throws(() => flat(type, [value]), /core/);
  }

  const validMemory = {
    address: 0,
    memory: validFlat.memory,
    store: validFlat.store,
    target: "wasm32",
    type: primitive("u8"),
  };
  for (const request of [null, [], {}, { ...validMemory, extra: true },
    { address: 0, memory: validMemory.memory, store: validMemory.store,
      target: "wasm32", wrong: primitive("u8") },
    { ...validMemory, [Symbol("bad")]: true },
    Object.defineProperty({ ...validMemory }, "type", {
      get: () => primitive("u8"), enumerable: true,
    })]) {
    assert.throws(() => memoryReader(request), /read/);
  }
});

function writePointerPair(memory, target, offset, pointer, length) {
  const view = new DataView(memory.buffer);
  if (target === "wasm64") {
    view.setBigUint64(offset, BigInt(pointer), true);
    view.setBigUint64(offset + 8, BigInt(length), true);
  } else {
    view.setUint32(offset, pointer, true);
    view.setUint32(offset + 4, length, true);
  }
}

function writeVariant(memory, type, offset, selected, payload = null) {
  const valueLayout = planRawCanonicalTypeLayout({ target: "wasm32", type });
  const view = new DataView(memory.buffer);
  if (valueLayout.discriminantSize === 1) view.setUint8(offset, selected);
  else if (valueLayout.discriminantSize === 2) view.setUint16(offset, selected, true);
  else view.setUint32(offset, selected, true);
  if (payload !== null) {
    const [kind, value] = payload;
    writeRawCanonicalMemoryScalar({
      address: offset + valueLayout.payloadOffset,
      kind,
      memory,
      target: "wasm32",
      value,
    });
  }
}

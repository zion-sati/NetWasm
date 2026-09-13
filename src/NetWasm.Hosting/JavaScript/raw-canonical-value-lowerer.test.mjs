import assert from "node:assert/strict";
import test from "node:test";
import {
  createRawCanonicalMemoryAllocator,
  createRawCanonicalMemoryDeallocator,
} from "./raw-canonical-memory-allocator.mjs";
import {
  readRawCanonicalMemoryScalar,
} from "./raw-canonical-scalar-codecs.mjs";
import {
  createRawCanonicalFlatValueLifter,
  createRawCanonicalMemoryValueReader,
} from "./raw-canonical-value-lifter.mjs";
import {
  createRawCanonicalFlatValueLowerer,
  createRawCanonicalMemoryValueWriter,
} from "./raw-canonical-value-lowerer.mjs";
import {
  borrowRawResource,
  createRawResourceStore,
  dropRawResource,
  registerRawResource,
  transferRawResource,
} from "./raw-resource-store.mjs";

const primitive = kind => ({ kind });
const alias = element => ({ kind: "alias", element });
const field = (name, type, javascriptName = name) => ({ javascriptName, name, type });
const tupleField = (name, type) => ({ name, type });
const variantCase = (name, type = null) => ({ name, type });
const flag = (name, javascriptName = name) => ({ javascriptName, name });
const resource = (kind, resourceType = 7) => ({ kind, resourceType });
const targetAddress = (value, target) => target === "wasm64" ? BigInt(value) : value;

function createHarness(target = "wasm32", options = {}) {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const store = createRawResourceStore();
  const allocations = [];
  const deallocations = [];
  const releases = [];
  let next = options.next ?? 1024;
  let allocationCount = 0;
  const reallocate = (oldAddress, oldSize, alignment, newSize) => {
    const old = Number(oldAddress);
    const size = Number(newSize);
    if (size === 0) {
      deallocations.push([old, Number(oldSize), Number(alignment)]);
      return targetAddress(0, target);
    }
    allocationCount++;
    options.beforeAllocate?.({ allocationCount, memory });
    if (options.failAllocation === allocationCount) throw new Error("allocation failed");
    const alignmentValue = Number(alignment);
    next = Math.ceil(next / alignmentValue) * alignmentValue;
    const address = next;
    next += size;
    while (next > memory.buffer.byteLength) memory.grow(1);
    allocations.push([address, size, alignmentValue]);
    return targetAddress(address, target);
  };
  const actions = {
    allocateMemory: createRawCanonicalMemoryAllocator({ reallocate }),
    deallocateMemory: createRawCanonicalMemoryDeallocator({ reallocate }),
    dropResource: dropRawResource,
    registerResource: registerRawResource,
    releaseResource(request) {
      releases.push(request);
      options.releaseResource?.(request);
    },
  };
  return {
    actions,
    allocations,
    deallocations,
    flatLowerer: createRawCanonicalFlatValueLowerer(actions),
    memory,
    releases,
    store,
    target,
    writer: createRawCanonicalMemoryValueWriter(actions),
  };
}

function lower(harness, type, value) {
  return harness.flatLowerer({
    memory: harness.memory,
    store: harness.store,
    target: harness.target,
    type,
    value,
  });
}

function write(harness, type, value, address = 0) {
  return harness.writer({
    address: targetAddress(address, harness.target),
    memory: harness.memory,
    store: harness.store,
    target: harness.target,
    type,
    value,
  });
}

function readScalar(memory, target, kind, address) {
  return readRawCanonicalMemoryScalar({
    address: targetAddress(address, target), kind, memory, target,
  });
}

test("creates immutable one-action lowering Strategies", () => {
  const harness = createHarness();
  assert.equal(typeof harness.flatLowerer, "function");
  assert.equal(typeof harness.writer, "function");
  assert.equal(Object.isFrozen(harness.flatLowerer), true);
  assert.equal(Object.isFrozen(harness.writer), true);
});

test("lowers every scalar, unit, alias, record and tuple to flat core values", () => {
  const harness = createHarness();
  const cases = [
    [primitive("unit"), undefined, []],
    [primitive("bool"), true, [1]],
    [primitive("s8"), -1, [-1]],
    [primitive("u8"), 255, [255]],
    [primitive("s16"), -2, [-2]],
    [primitive("u16"), 65_535, [65_535]],
    [primitive("s32"), -7, [-7]],
    [primitive("u32"), 0xffff_ffff, [-1]],
    [primitive("s64"), -7n, [-7n]],
    [primitive("u64"), 0xffff_ffff_ffff_ffffn, [-1n]],
    [primitive("f32"), 1 / 3, [Math.fround(1 / 3)]],
    [primitive("f64"), Math.PI, [Math.PI]],
    [primitive("character"), "🚀", [0x1f680]],
    [alias(primitive("u16")), 42, [42]],
  ];
  for (const [type, value, expected] of cases) assert.deepEqual(lower(harness, type, value), expected);

  const record = {
    kind: "record",
    fields: [field("first-value", primitive("u16"), "firstValue"),
      field("second-value", primitive("bool"), "secondValue")],
  };
  assert.deepEqual(lower(harness, record, { firstValue: 17, secondValue: true }), [17, 1]);
  assert.deepEqual(lower(harness, {
    kind: "tuple",
    fields: [tupleField("item0", primitive("s32")), tupleField("item1", primitive("f64"))],
  }, [-3, 2.5]), [-3, 2.5]);
});

test("writes every scalar, unit and aggregate field at both widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const harness = createHarness(target);
    const type = {
      kind: "record",
      fields: [
        field("truth", primitive("bool")),
        field("count", alias(primitive("u32"))),
        field("pair", {
          kind: "tuple",
          fields: [tupleField("item0", primitive("s64")), tupleField("item1", primitive("f32"))],
        }),
      ],
    };
    write(harness, type, { truth: true, count: 0xffff_fffe, pair: [-9n, 1.5] });
    assert.equal(readScalar(harness.memory, target, "bool", 0), true);
    assert.equal(readScalar(harness.memory, target, "u32", 4), 0xffff_fffe);
    assert.equal(readScalar(harness.memory, target, "s64", 8), -9n);
    assert.equal(readScalar(harness.memory, target, "f32", 16), 1.5);
    write(harness, primitive("unit"), undefined, 32);
  }
});

test("allocates stable UTF-8 and byte-list snapshots at both widths", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const bytes = new Uint8Array([1, 2, 3, 4]);
    const harness = createHarness(target, {
      beforeAllocate({ allocationCount }) {
        if (allocationCount === 2) bytes.fill(9);
      },
    });
    const type = {
      kind: "record",
      fields: [field("text", primitive("text")),
        field("bytes", { kind: "list", element: alias(primitive("u8")) })],
    };
    const values = lower(harness, type, { text: "hello", bytes });
    const textAddress = Number(values[0]);
    const byteAddress = Number(values[2]);
    assert.deepEqual(values.map(Number), [textAddress, 5, byteAddress, 4]);
    assert.equal(new TextDecoder().decode(new Uint8Array(harness.memory.buffer, textAddress, 5)), "hello");
    assert.deepEqual([...new Uint8Array(harness.memory.buffer, byteAddress, 4)], [1, 2, 3, 4]);
    assert.deepEqual(harness.allocations.map(item => item.slice(1)), [[5, 1], [4, 1]]);

    const emptyText = lower(harness, primitive("text"), "");
    const emptyBytes = lower(harness, { kind: "list", element: primitive("u8") },
      new Uint8Array());
    assert.deepEqual(emptyText, target === "wasm64" ? [0n, 0n] : [0, 0]);
    assert.deepEqual(emptyBytes, target === "wasm64" ? [0n, 0n] : [0, 0]);

    write(harness, primitive("text"), "wide", 32);
    const pointerKind = target === "wasm64" ? "u64" : "u32";
    assert.equal(Number(readScalar(harness.memory, target, pointerKind, 32)) > 0, true);
  }
});

test("materializes nested lists before publishing canonical memory headers", () => {
  const original = ["first", "second"];
  let observedHeader;
  const harness = createHarness("wasm32", {
    beforeAllocate({ allocationCount, memory }) {
      if (allocationCount === 1) {
        observedHeader = [...new Uint8Array(memory.buffer, 0, 8)];
        original[0] = "changed";
      }
    },
  });
  new Uint8Array(harness.memory.buffer, 0, 8).fill(0xaa);
  const type = { kind: "list", element: primitive("text") };
  write(harness, type, original);

  assert.deepEqual(observedHeader, Array(8).fill(0xaa));
  const reader = createRawCanonicalMemoryValueReader({
    borrowResource: borrowRawResource,
    transferResource: transferRawResource,
  });
  assert.deepEqual(reader({
    address: 0,
    memory: harness.memory,
    store: harness.store,
    target: "wasm32",
    type,
  }), ["first", "second"]);

  assert.deepEqual(lower(harness, type, []), [0, 0]);
});

test("lowers options, results, variants, enums and flags", () => {
  const harness = createHarness();
  const option = { kind: "option", element: primitive("u32") };
  assert.deepEqual(lower(harness, option, undefined), [0, 0]);
  assert.deepEqual(lower(harness, option, 42), [1, 42]);
  const nested = { kind: "option", element: option };
  assert.deepEqual(lower(harness, nested, { tag: "none" }), [0, 0, 0]);
  assert.deepEqual(lower(harness, nested, { tag: "some", val: undefined }), [1, 0, 0]);
  assert.deepEqual(lower(harness, nested, { tag: "some", val: 7 }), [1, 1, 7]);

  assert.deepEqual(lower(harness, {
    kind: "result", ok: primitive("u16"), error: null,
  }, { tag: "ok", val: 17 }), [0, 17]);
  assert.deepEqual(lower(harness, {
    kind: "result", ok: primitive("u16"), error: null,
  }, { tag: "err" }), [1, 0]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("empty"), variantCase("value", primitive("s32"))],
  }, { tag: "value", val: -7 }), [1, -7]);
  assert.deepEqual(lower(harness, {
    kind: "enum", cases: [variantCase("first"), variantCase("second")],
  }, "second"), [1]);

  const flags = {
    kind: "flags",
    count: 33,
    flags: Array.from({ length: 33 }, (_, index) => flag(`flag-${index}`, `flag${index}`)),
  };
  const flagValue = Object.fromEntries(flags.flags.map((item, index) => [item.javascriptName,
    index === 0 || index === 31 || index === 32]));
  assert.deepEqual(lower(harness, flags, flagValue), [-0x7fff_ffff, 1]);
});

test("applies every reverse joined-variant core coercion", () => {
  const harness = createHarness();
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
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("float", primitive("f32")), variantCase("int", primitive("u32"))],
  }, { tag: "float", val: 1.5 }), [0, bits32(1.5)]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("int", primitive("u32"))],
  }, { tag: "int", val: 0xffff_ffff }), [1, 0xffff_ffffn]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("float", primitive("f32"))],
  }, { tag: "float", val: -2.5 }), [1, BigInt(bits32(-2.5) >>> 0)]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("float", primitive("f64"))],
  }, { tag: "float", val: Math.PI }), [1, bits64(Math.PI)]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("double", primitive("f64")), variantCase("float", primitive("f32"))],
  }, { tag: "float", val: 1.25 }), [1, 1.25]);
  assert.deepEqual(lower(harness, {
    kind: "variant",
    cases: [variantCase("wide", primitive("u64")), variantCase("wide-too", primitive("u64"))],
  }, { tag: "wide", val: 42n }), [0, 42n]);
});

test("writes variant, enum and all canonical flag storage widths", () => {
  const harness = createHarness();
  write(harness, {
    kind: "variant",
    cases: [variantCase("none"), variantCase("number", primitive("u64"))],
  }, { tag: "number", val: 42n }, 0);
  assert.equal(new DataView(harness.memory.buffer).getUint8(0), 1);
  assert.equal(readScalar(harness.memory, "wasm32", "u64", 8), 42n);
  write(harness, {
    kind: "enum",
    cases: Array.from({ length: 257 }, (_, index) => variantCase(`case-${index}`)),
  }, "case-256", 24);
  assert.equal(new DataView(harness.memory.buffer).getUint16(24, true), 256);
  const manyCases = Array.from({ length: 65_537 }, (_, index) => variantCase(`wide-${index}`));
  write(harness, { kind: "enum", cases: manyCases }, "wide-65536", 28);
  assert.equal(new DataView(harness.memory.buffer).getUint32(28, true), 65_536);

  for (const [count, offset] of [[0, 32], [9, 34], [33, 40]]) {
    const type = {
      kind: "flags",
      count,
      flags: Array.from({ length: count }, (_, index) => flag(`f-${index}`, `f${index}`)),
    };
    const value = Object.fromEntries(type.flags.map(item => [item.javascriptName, true]));
    write(harness, type, value, offset);
  }
  const view = new DataView(harness.memory.buffer);
  assert.equal(view.getUint8(32), 0);
  assert.equal(view.getUint16(34, true), 0x1ff);
  assert.equal(view.getUint32(40, true), 0xffff_ffff);
  assert.equal(view.getUint32(44, true), 1);
});

test("publishes owned resources and releases them through guest drop", () => {
  for (const target of ["wasm32", "wasm64"]) {
    const harness = createHarness(target);
    const value = { name: "descriptor" };
    const values = lower(harness, alias(resource("owned-resource", 3)), value);

    assert.deepEqual(values, [1]);
    assert.equal(borrowRawResource({ handle: 1, store: harness.store, type: 3 }), value);
    assert.deepEqual(harness.releases, []);
    dropRawResource({ handle: 1, store: harness.store, type: 3 });
    assert.deepEqual(harness.releases, [{ type: 3, value }]);

    const functionResource = () => {};
    write(harness, resource("owned-resource", 4), functionResource, 8);
    assert.equal(readScalar(harness.memory, target, "u32", 8), 1);
    assert.equal(borrowRawResource({ handle: 1, store: harness.store, type: 4 }), functionResource);
  }
});

test("releases acquired resources when later validation fails", () => {
  const harness = createHarness();
  const value = {};
  const type = {
    kind: "record",
    fields: [field("resource", resource("owned-resource", 4)),
      field("count", primitive("u32"))],
  };
  assert.throws(() => lower(harness, type, { resource: value, count: -1 }), /u32 value/);
  assert.deepEqual(harness.releases, [{ type: 4, value }]);
  assert.throws(() => borrowRawResource({ handle: 1, store: harness.store, type: 4 }), /unavailable/);

  assert.throws(() => lower(harness, {
    kind: "tuple",
    fields: [tupleField("first", resource("owned-resource", 4)),
      tupleField("second", resource("owned-resource", 4))],
  }, [value, value]), /duplicated/);
  assert.deepEqual(harness.releases, [{ type: 4, value }, { type: 4, value }]);
});

test("rolls back registered resources and allocations in reverse on failure", () => {
  const value = {};
  const harness = createHarness("wasm32", { failAllocation: 2 });
  const type = {
    kind: "record",
    fields: [field("resource", resource("owned-resource", 9)),
      field("first", primitive("text")), field("second", primitive("text"))],
  };
  assert.throws(() => lower(harness, type, {
    resource: value, first: "one", second: "two",
  }), /allocation failed/);
  assert.deepEqual(harness.deallocations, [[1024, 3, 1]]);
  assert.deepEqual(harness.releases, [{ type: 9, value }]);
  assert.throws(() => borrowRawResource({ handle: 1, store: harness.store, type: 9 }), /unavailable/);
});

test("attempts every cleanup and preserves the primary lowering failure", () => {
  const released = [];
  const actions = {
    allocateMemory: (() => {
      let count = 0;
      return () => {
        count++;
        if (count === 2) throw new Error("primary allocation");
        return 1024;
      };
    })(),
    deallocateMemory() { throw new Error("deallocation cleanup"); },
    dropResource(request) {
      released.push(request.type);
      throw new Error("resource cleanup");
    },
    registerResource: request => request.type,
    releaseResource(request) { released.push(request.type); },
  };
  const lowerer = createRawCanonicalFlatValueLowerer(actions);
  const type = {
    kind: "record",
    fields: [field("resource", resource("owned-resource", 5)),
      field("first", primitive("text")), field("second", primitive("text"))],
  };
  assert.throws(() => lowerer({
    memory: new WebAssembly.Memory({ initial: 1 }),
    store: {},
    target: "wasm32",
    type,
    value: { resource: {}, first: "a", second: "b" },
  }), error => error instanceof AggregateError
    && error.errors.map(item => item.message).join(",")
      === "primary allocation,deallocation cleanup,resource cleanup");
  assert.deepEqual(released, [5]);
});

test("releases an owned value when resource registration returns the reserved handle", () => {
  const released = [];
  const actions = {
    allocateMemory() { throw new Error("must not allocate"); },
    deallocateMemory() { throw new Error("must not deallocate"); },
    dropResource() { throw new Error("must not drop an unpublished handle"); },
    registerResource: () => 0,
    releaseResource(request) { released.push(request); },
  };
  const lowerer = createRawCanonicalFlatValueLowerer(actions);
  const value = {};
  assert.throws(() => lowerer({
    memory: new WebAssembly.Memory({ initial: 1 }),
    store: {},
    target: "wasm32",
    type: resource("owned-resource", 2),
    value,
  }), /zero is reserved/);
  assert.deepEqual(released, [{ type: 2, value }]);
});

test("validates complete values and root memory before mutation", () => {
  const harness = createHarness();
  const root = new Uint8Array(harness.memory.buffer, 0, 16);
  root.fill(0xaa);
  const type = {
    kind: "record",
    fields: [field("text", primitive("text")), field("count", primitive("u32"))],
  };
  assert.throws(() => write(harness, type, { text: "valid", count: -1 }), /u32 value/);
  assert.deepEqual([...root], Array(16).fill(0xaa));
  assert.deepEqual(harness.allocations, []);
  assert.throws(() => harness.writer({
    address: 65_536,
    memory: harness.memory,
    store: harness.store,
    target: "wasm32",
    type,
    value: { text: "valid", count: 1 },
  }), /out of bounds/);
  assert.deepEqual(harness.allocations, []);
});

test("rejects unsupported borrowed results and malformed Jco-facing values", () => {
  const harness = createHarness();
  assert.throws(() => lower(harness, resource("borrowed-resource"), {}), /cannot be lowered/);
  for (const [type, value, pattern] of [
    [primitive("unit"), null, /unit value/],
    [primitive("text"), 1, /text value/],
    [{ kind: "list", element: primitive("u8") }, [1], /byte list/],
    [{ kind: "list", element: primitive("u16") }, new Uint16Array([1]), /list value/],
    [{ kind: "list", element: primitive("unit") }, [undefined], /zero-sized/],
    [{ kind: "record", fields: [field("value", primitive("u8"))] }, {}, /record value/],
    [{ kind: "tuple", fields: [tupleField("item", primitive("u8"))] }, [], /tuple value/],
    [{ kind: "option", element: { kind: "option", element: primitive("u8") } },
      { tag: "bad" }, /option value/],
    [{ kind: "option", element: { kind: "option", element: primitive("u8") } },
      { tag: "some" }, /option value/],
    [{ kind: "result", ok: null, error: null }, { tag: "ok", val: 1 }, /result value/],
    [{ kind: "variant", cases: [variantCase("empty")] }, { tag: "empty", val: 1 }, /variant value/],
    [{ kind: "enum", cases: [variantCase("one")] }, 1, /enum value/],
    [{ kind: "enum", cases: [variantCase("one")] }, "two", /enum value/],
    [{ kind: "flags", count: 1, flags: [flag("one")] }, { one: 1 }, /flags value/],
    [resource("owned-resource"), null, /owned resource/],
  ]) {
    assert.throws(() => lower(harness, type, value), pattern);
  }
});

test("rejects hostile object, array and tagged shapes without invoking getters", () => {
  const harness = createHarness();
  const getter = Object.defineProperty({}, "value", {
    enumerable: true,
    get() { throw new Error("must not invoke"); },
  });
  const recordType = { kind: "record", fields: [field("value", primitive("u8"))] };
  for (const value of [null, [], { value: 1, extra: 2 },
    { value: 1, [Symbol("bad")]: true }, getter]) {
    assert.throws(() => lower(harness, recordType, value), /record value/);
  }
  const list = [1];
  list.extra = true;
  const sparse = Array(1);
  const accessor = [];
  Object.defineProperty(accessor, "0", { enumerable: true, get() { throw new Error("no"); } });
  accessor.length = 1;
  for (const value of [list, sparse, accessor, Object.assign([1], { [Symbol("bad")]: true })]) {
    assert.throws(() => lower(harness, { kind: "list", element: primitive("u16") }, value), /list value/);
  }
  for (const value of [null, [], { tag: "one", extra: 1 },
    { tag: "one", [Symbol("bad")]: true },
    Object.defineProperty({}, "tag", { enumerable: true, get() { throw new Error("no"); } })]) {
    assert.throws(() => lower(harness, {
      kind: "variant", cases: [variantCase("one")],
    }, value), /variant value/);
  }
  const valueAccessor = { tag: "one" };
  Object.defineProperty(valueAccessor, "val", {
    enumerable: true,
    get() { throw new Error("must not invoke"); },
  });
  assert.throws(() => lower(harness, {
    kind: "variant", cases: [variantCase("one", primitive("u8"))],
  }, valueAccessor), /variant value/);
});

test("rejects malformed factories and action requests", () => {
  const harness = createHarness();
  for (const factory of [null, [], {}, { ...harness.actions, allocateMemory: 1 },
    { ...harness.actions, extra: true }, { ...harness.actions, [Symbol("bad")]: true },
    Object.defineProperty({ ...harness.actions }, "dropResource", {
      enumerable: true, get() { throw new Error("no"); },
    })]) {
    assert.throws(() => createRawCanonicalFlatValueLowerer(factory), TypeError);
    assert.throws(() => createRawCanonicalMemoryValueWriter(factory), TypeError);
  }
  const valid = {
    memory: harness.memory,
    store: harness.store,
    target: "wasm32",
    type: primitive("u8"),
    value: 1,
  };
  for (const request of [null, [], {}, { ...valid, extra: true },
    { ...valid, [Symbol("bad")]: true },
    Object.defineProperty({ ...valid }, "value", { enumerable: true, get() { throw new Error("no"); } })]) {
    assert.throws(() => harness.flatLowerer(request), TypeError);
  }
  for (const request of [null, [], {}, { address: 0, ...valid, extra: true }]) {
    assert.throws(() => harness.writer(request), TypeError);
  }
  assert.throws(() => harness.flatLowerer({ ...valid, memory: {} }), /memory/);
});

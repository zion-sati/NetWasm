import {
  projectRawCanonicalMemoryRange,
} from "./raw-canonical-memory-range-projector.mjs";
import {
  liftRawCanonicalFlatScalar,
  readRawCanonicalMemoryScalar,
} from "./raw-canonical-scalar-codecs.mjs";
import {
  flattenRawCanonicalType,
  planRawCanonicalTypeLayout,
} from "./raw-canonical-type-planner.mjs";

const factoryKeys = ["borrowResource", "transferResource"];
const flatRequestKeys = ["memory", "store", "target", "type", "values"];
const memoryRequestKeys = ["address", "memory", "store", "target", "type"];
const maximumSequenceByteLength = 0x0fff_ffff;
const utf8Decoder = new TextDecoder("utf-8", { fatal: true });
const coercionScratch = new DataView(new ArrayBuffer(8));

const flatStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: liftFlatUnit,
  bool: liftFlatScalar,
  s8: liftFlatScalar,
  u8: liftFlatScalar,
  s16: liftFlatScalar,
  u16: liftFlatScalar,
  s32: liftFlatScalar,
  u32: liftFlatScalar,
  s64: liftFlatScalar,
  u64: liftFlatScalar,
  f32: liftFlatScalar,
  f64: liftFlatScalar,
  character: liftFlatScalar,
  text: liftFlatText,
  alias: liftFlatAlias,
  list: liftFlatList,
  record: liftFlatRecord,
  tuple: liftFlatTuple,
  option: liftFlatOption,
  result: liftFlatResult,
  variant: liftFlatVariant,
  enum: liftFlatEnum,
  flags: liftFlatFlags,
  "owned-resource": liftFlatResource,
  "borrowed-resource": liftFlatResource,
}));

const memoryStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: readMemoryUnit,
  bool: readMemoryScalar,
  s8: readMemoryScalar,
  u8: readMemoryScalar,
  s16: readMemoryScalar,
  u16: readMemoryScalar,
  s32: readMemoryScalar,
  u32: readMemoryScalar,
  s64: readMemoryScalar,
  u64: readMemoryScalar,
  f32: readMemoryScalar,
  f64: readMemoryScalar,
  character: readMemoryScalar,
  text: readMemoryText,
  alias: readMemoryAlias,
  list: readMemoryList,
  record: readMemoryRecord,
  tuple: readMemoryTuple,
  option: readMemoryOption,
  result: readMemoryResult,
  variant: readMemoryVariant,
  enum: readMemoryEnum,
  flags: readMemoryFlags,
  "owned-resource": readMemoryResource,
  "borrowed-resource": readMemoryResource,
}));

const coreValidators = Object.freeze(Object.assign(Object.create(null), {
  i32(value) {
    if (!Number.isInteger(value) || value < -0x8000_0000 || value > 0x7fff_ffff) {
      throw new TypeError("raw canonical core i32 value is invalid");
    }
  },
  i64(value) {
    if (typeof value !== "bigint" || value < -0x8000_0000_0000_0000n
        || value > 0x7fff_ffff_ffff_ffffn) {
      throw new TypeError("raw canonical core i64 value is invalid");
    }
  },
  f32(value) {
    if (typeof value !== "number") throw new TypeError("raw canonical core f32 value is invalid");
  },
  f64(value) {
    if (typeof value !== "number") throw new TypeError("raw canonical core f64 value is invalid");
  },
}));

const coercions = Object.freeze(Object.assign(Object.create(null), {
  "i32:f32": value => {
    coercionScratch.setInt32(0, value, true);
    return coercionScratch.getFloat32(0, true);
  },
  "i64:i32": value => Number(BigInt.asIntN(32, value)),
  "i64:f32": value => {
    coercionScratch.setBigInt64(0, value, true);
    return coercionScratch.getFloat32(0, true);
  },
  "i64:f64": value => {
    coercionScratch.setBigInt64(0, value, true);
    return coercionScratch.getFloat64(0, true);
  },
  "f64:f32": value => Math.fround(value),
}));

export function createRawCanonicalFlatValueLifter(request = {}) {
  const actions = readFactory(request, "raw canonical flat-value lifter");
  return Object.freeze(input => liftFlat(input, actions));
}

export function createRawCanonicalMemoryValueReader(request = {}) {
  const actions = readFactory(request, "raw canonical memory-value reader");
  return Object.freeze(input => readMemory(input, actions));
}

function liftFlat(request, actions) {
  assertExactObject(request, flatRequestKeys, "raw canonical flat-value lift");
  validateMemory(request.memory, request.target);
  const types = flattenRawCanonicalType({ target: request.target, type: request.type });
  const values = snapshotCoreValues(request.values, types);
  const transaction = createLiftTransaction(request.store, actions);
  const cursor = { index: 0, types, values };
  const value = liftFlatValue(request.type, cursor, context(request), transaction);
  transaction.commit();
  return value;
}

function readMemory(request, actions) {
  assertExactObject(request, memoryRequestKeys, "raw canonical memory-value read");
  const valueLayout = planRawCanonicalTypeLayout({ target: request.target, type: request.type });
  const range = projectRawCanonicalMemoryRange({
    address: request.address,
    alignment: valueLayout.alignment,
    byteLength: valueLayout.size,
    memory: request.memory,
    target: request.target,
  });
  const transaction = createLiftTransaction(request.store, actions);
  const value = readMemoryValue(
    request.type,
    valueLayout,
    range.index,
    context(request),
    transaction);
  transaction.commit();
  return value;
}

function context(request) {
  return { memory: request.memory, store: request.store, target: request.target };
}

function liftFlatValue(type, cursor, current, transaction) {
  return flatStrategies[type.kind](type, cursor, current, transaction);
}

function readMemoryValue(type, valueLayout, index, current, transaction) {
  return memoryStrategies[type.kind](type, valueLayout, index, current, transaction);
}

function liftFlatUnit() {
  return undefined;
}

function liftFlatScalar(type, cursor) {
  return liftRawCanonicalFlatScalar({ kind: type.kind, value: nextCoreValue(cursor) });
}

function liftFlatAlias(type, cursor, current, transaction) {
  return liftFlatValue(type.element, cursor, current, transaction);
}

function liftFlatText(_type, cursor, current) {
  const address = nextCoreValue(cursor);
  const length = nextCoreValue(cursor);
  return readTextRange(address, length, current);
}

function liftFlatList(type, cursor, current, transaction) {
  const address = nextCoreValue(cursor);
  const length = nextCoreValue(cursor);
  return readListRange(type.element, address, length, current, transaction);
}

function liftFlatRecord(type, cursor, current, transaction) {
  const value = {};
  for (const field of type.fields) {
    defineValue(value, field.javascriptName,
      liftFlatValue(field.type, cursor, current, transaction));
  }
  return value;
}

function liftFlatTuple(type, cursor, current, transaction) {
  return type.fields.map(field => liftFlatValue(field.type, cursor, current, transaction));
}

function liftFlatOption(type, cursor, current, transaction) {
  const selected = liftFlatVariantSelection(
    [null, type.element], cursor, current, transaction);
  if (isOption(type.element)) {
    return selected.index === 0
      ? taggedValue("none", null, undefined)
      : taggedValue("some", type.element, selected.value);
  }
  return selected.index === 0 ? undefined : selected.value;
}

function liftFlatResult(type, cursor, current, transaction) {
  const selected = liftFlatVariantSelection(
    [type.ok, type.error], cursor, current, transaction);
  return taggedValue(selected.index === 0 ? "ok" : "err", selected.type, selected.value);
}

function liftFlatVariant(type, cursor, current, transaction) {
  const selected = liftFlatVariantSelection(
    type.cases.map(item => item.type), cursor, current, transaction);
  return taggedValue(type.cases[selected.index].name, selected.type, selected.value);
}

function liftFlatVariantSelection(caseTypes, cursor, current, transaction) {
  const index = liftRawCanonicalFlatScalar({ kind: "u32", value: nextCoreValue(cursor) });
  if (index >= caseTypes.length) throw new TypeError("raw canonical variant discriminant is invalid");
  const synthetic = variantType(caseTypes);
  const joinedTypes = flattenRawCanonicalType({ target: current.target, type: synthetic }).slice(1);
  const joinedValues = joinedTypes.map(() => nextCoreValue(cursor));
  const selectedType = caseTypes[index];
  if (selectedType === null) return { index, type: null, value: undefined };
  const selectedTypes = flattenRawCanonicalType({ target: current.target, type: selectedType });
  const values = selectedTypes.map((type, slot) =>
    coerceCoreValue(joinedValues[slot], joinedTypes[slot], type));
  const selectedCursor = { index: 0, types: selectedTypes, values };
  const value = liftFlatValue(selectedType, selectedCursor, current, transaction);
  return { index, type: selectedType, value };
}

function liftFlatEnum(type, cursor) {
  const index = liftRawCanonicalFlatScalar({ kind: "u32", value: nextCoreValue(cursor) });
  if (index >= type.cases.length) throw new TypeError("raw canonical enum discriminant is invalid");
  return type.cases[index].name;
}

function liftFlatFlags(type, cursor) {
  const words = Array.from({ length: Math.max(1, Math.ceil(type.count / 32)) }, () =>
    liftRawCanonicalFlatScalar({ kind: "u32", value: nextCoreValue(cursor) }));
  return unpackFlags(type, words);
}

function liftFlatResource(type, cursor, current, transaction) {
  const handle = liftRawCanonicalFlatScalar({ kind: "u32", value: nextCoreValue(cursor) });
  return transaction.read(type, handle);
}

function readMemoryUnit() {
  return undefined;
}

function readMemoryScalar(type, _layout, index, current) {
  return readRawCanonicalMemoryScalar({
    address: targetAddress(index, current.target),
    kind: type.kind,
    memory: current.memory,
    target: current.target,
  });
}

function readMemoryAlias(type, valueLayout, index, current, transaction) {
  return readMemoryValue(type.element, valueLayout, index, current, transaction);
}

function readMemoryText(_type, _layout, index, current) {
  const [address, length] = readPointerPair(index, current);
  return readTextRange(address, length, current);
}

function readMemoryList(type, _layout, index, current, transaction) {
  const [address, length] = readPointerPair(index, current);
  return readListRange(type.element, address, length, current, transaction);
}

function readMemoryRecord(type, valueLayout, index, current, transaction) {
  const value = {};
  for (let fieldIndex = 0; fieldIndex < type.fields.length; fieldIndex++) {
    const field = type.fields[fieldIndex];
    const fieldLayout = valueLayout.fields[fieldIndex];
    defineValue(value, field.javascriptName,
      readMemoryValue(field.type, fieldLayout.layout,
        index + fieldLayout.offset, current, transaction));
  }
  return value;
}

function readMemoryTuple(type, valueLayout, index, current, transaction) {
  return type.fields.map((field, fieldIndex) => {
    const fieldLayout = valueLayout.fields[fieldIndex];
    return readMemoryValue(field.type, fieldLayout.layout,
      index + fieldLayout.offset, current, transaction);
  });
}

function readMemoryOption(type, valueLayout, index, current, transaction) {
  const selected = readMemoryVariantSelection(
    [null, type.element], valueLayout, index, current, transaction);
  if (isOption(type.element)) {
    return selected.index === 0
      ? taggedValue("none", null, undefined)
      : taggedValue("some", type.element, selected.value);
  }
  return selected.index === 0 ? undefined : selected.value;
}

function readMemoryResult(type, valueLayout, index, current, transaction) {
  const selected = readMemoryVariantSelection(
    [type.ok, type.error], valueLayout, index, current, transaction);
  return taggedValue(selected.index === 0 ? "ok" : "err", selected.type, selected.value);
}

function readMemoryVariant(type, valueLayout, index, current, transaction) {
  const selected = readMemoryVariantSelection(
    type.cases.map(item => item.type), valueLayout, index, current, transaction);
  return taggedValue(type.cases[selected.index].name, selected.type, selected.value);
}

function readMemoryVariantSelection(caseTypes, valueLayout, index, current, transaction) {
  const selectedIndex = readDiscriminant(
    valueLayout.discriminantSize, index, current.memory);
  if (selectedIndex >= caseTypes.length) {
    throw new TypeError("raw canonical variant discriminant is invalid");
  }
  const selectedType = caseTypes[selectedIndex];
  if (selectedType === null) return { index: selectedIndex, type: null, value: undefined };
  const selectedLayout = planRawCanonicalTypeLayout({
    target: current.target,
    type: selectedType,
  });
  return {
    index: selectedIndex,
    type: selectedType,
    value: readMemoryValue(selectedType, selectedLayout,
      index + valueLayout.payloadOffset, current, transaction),
  };
}

function readMemoryEnum(type, valueLayout, index, current) {
  const selectedIndex = readDiscriminant(valueLayout.size, index, current.memory);
  if (selectedIndex >= type.cases.length) {
    throw new TypeError("raw canonical enum discriminant is invalid");
  }
  return type.cases[selectedIndex].name;
}

function readMemoryFlags(type, valueLayout, index, current) {
  const view = new DataView(current.memory.buffer);
  let words;
  if (valueLayout.size === 1) words = [view.getUint8(index)];
  else if (valueLayout.size === 2) words = [view.getUint16(index, true)];
  else {
    words = Array.from({ length: valueLayout.size / 4 }, (_, word) =>
      view.getUint32(index + word * 4, true));
  }
  return unpackFlags(type, words);
}

function readMemoryResource(type, _layout, index, current, transaction) {
  const handle = readRawCanonicalMemoryScalar({
    address: targetAddress(index, current.target),
    kind: "u32",
    memory: current.memory,
    target: current.target,
  });
  return transaction.read(type, handle);
}

function readTextRange(address, rawLength, current) {
  const length = readSequenceLength(rawLength, current.target, "text");
  const range = projectRawCanonicalMemoryRange({
    address,
    alignment: 1,
    byteLength: length,
    memory: current.memory,
    target: current.target,
  });
  const snapshot = new Uint8Array(range.buffer, range.index, length).slice();
  try {
    return utf8Decoder.decode(snapshot);
  } catch (error) {
    throw new TypeError("raw canonical UTF-8 text is invalid", { cause: error });
  }
}

function readListRange(elementType, address, rawLength, current, transaction) {
  const length = readSequenceLength(rawLength, current.target, "list");
  const elementLayout = planRawCanonicalTypeLayout({ target: current.target, type: elementType });
  if (elementLayout.size === 0 && length !== 0) {
    throw new TypeError("raw canonical zero-sized list elements are unsupported");
  }
  const byteLength = elementLayout.size * length;
  if (byteLength > maximumSequenceByteLength) {
    throw new RangeError("raw canonical list byte length is too large");
  }
  const range = projectRawCanonicalMemoryRange({
    address,
    alignment: elementLayout.alignment,
    byteLength,
    memory: current.memory,
    target: current.target,
  });
  if (underlyingKind(elementType) === "u8") {
    return new Uint8Array(range.buffer, range.index, length).slice();
  }
  return Array.from({ length }, (_, item) => readMemoryValue(
    elementType,
    elementLayout,
    range.index + item * elementLayout.size,
    current,
    transaction));
}

function readPointerPair(index, current) {
  const kind = current.target === "wasm64" ? "u64" : "u32";
  const size = current.target === "wasm64" ? 8 : 4;
  return [
    readRawCanonicalMemoryScalar({
      address: targetAddress(index, current.target),
      kind,
      memory: current.memory,
      target: current.target,
    }),
    readRawCanonicalMemoryScalar({
      address: targetAddress(index + size, current.target),
      kind,
      memory: current.memory,
      target: current.target,
    }),
  ];
}

function readSequenceLength(value, target, label) {
  const normalized = liftRawCanonicalFlatScalar({
    kind: target === "wasm64" ? "u64" : "u32",
    value,
  });
  if (BigInt(normalized) > BigInt(maximumSequenceByteLength)) {
    throw new RangeError(`raw canonical ${label} length is too large`);
  }
  return Number(normalized);
}

function readDiscriminant(size, index, memory) {
  const view = new DataView(memory.buffer);
  if (size === 1) return view.getUint8(index);
  if (size === 2) return view.getUint16(index, true);
  return view.getUint32(index, true);
}

function unpackFlags(type, words) {
  const value = {};
  for (let index = 0; index < type.flags.length; index++) {
    defineValue(value, type.flags[index].javascriptName,
      ((words[Math.floor(index / 32)] >>> (index % 32)) & 1) === 1);
  }
  return value;
}

function createLiftTransaction(store, actions) {
  const owned = [];
  const identities = new Set();
  return Object.freeze({
    read(type, handle) {
      const request = { handle, store, type: type.resourceType };
      const value = actions.borrowResource(request);
      if (type.kind === "owned-resource") {
        const identity = `${type.resourceType}:${handle}`;
        if (identities.has(identity)) {
          throw new TypeError("raw canonical owned resource is duplicated");
        }
        identities.add(identity);
        owned.push({ request, value });
      }
      return value;
    },
    commit() {
      for (const entry of owned) actions.transferResource(entry.request);
    },
  });
}

function snapshotCoreValues(values, types) {
  if (!Array.isArray(values) || Object.getOwnPropertySymbols(values).length !== 0
      || values.length !== types.length) {
    throw new TypeError("raw canonical flat core values are invalid");
  }
  const descriptors = Object.getOwnPropertyDescriptors(values);
  const expectedNames = [
    "length",
    ...Array.from({ length: values.length }, (_, index) => `${index}`),
  ];
  const names = Object.keys(descriptors).sort();
  expectedNames.sort();
  if (names.length !== expectedNames.length
      || names.some((name, index) => name !== expectedNames[index])) {
    throw new TypeError("raw canonical flat core value shape is invalid");
  }
  return types.map((type, index) => {
    const descriptor = descriptors[index];
    if (!descriptor.enumerable || !("value" in descriptor)) {
      throw new TypeError("raw canonical flat core value shape is invalid");
    }
    coreValidators[type](descriptor.value);
    return descriptor.value;
  });
}

function nextCoreValue(cursor) {
  return cursor.values[cursor.index++];
}

function coerceCoreValue(value, source, destination) {
  coreValidators[source](value);
  if (source === destination) return value;
  return coercions[`${source}:${destination}`](value);
}

function taggedValue(tag, payloadType, value) {
  const result = { tag };
  if (payloadType !== null) defineValue(result, "val", value);
  return result;
}

function variantType(caseTypes) {
  return {
    kind: "variant",
    cases: caseTypes.map((type, index) => ({ name: `case-${index}`, type })),
  };
}

function isOption(type) {
  return underlyingKind(type) === "option";
}

function underlyingKind(type) {
  let current = type;
  while (current.kind === "alias") current = current.element;
  return current.kind;
}

function targetAddress(index, target) {
  return target === "wasm64" ? BigInt(index) : index;
}

function defineValue(target, name, value) {
  Object.defineProperty(target, name, {
    configurable: true,
    enumerable: true,
    value,
    writable: true,
  });
}

function validateMemory(memory, target) {
  projectRawCanonicalMemoryRange({
    address: target === "wasm64" ? 0n : 0,
    alignment: 1,
    byteLength: 0,
    memory,
    target,
  });
}

function readFactory(request, label) {
  assertExactObject(request, factoryKeys, label);
  if (typeof request.borrowResource !== "function"
      || typeof request.transferResource !== "function") {
    throw new TypeError(`${label} resource capabilities are invalid`);
  }
  return Object.freeze({
    borrowResource: request.borrowResource,
    transferResource: request.transferResource,
  });
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

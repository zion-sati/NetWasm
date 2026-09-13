import {
  projectRawCanonicalMemoryRange,
} from "./raw-canonical-memory-range-projector.mjs";
import {
  lowerRawCanonicalFlatScalar,
  writeRawCanonicalMemoryScalar,
} from "./raw-canonical-scalar-codecs.mjs";
import {
  flattenRawCanonicalType,
  planRawCanonicalTypeLayout,
} from "./raw-canonical-type-planner.mjs";

const factoryKeys = [
  "allocateMemory",
  "deallocateMemory",
  "dropResource",
  "registerResource",
  "releaseResource",
];
const flatRequestKeys = ["memory", "store", "target", "type", "value"];
const memoryRequestKeys = ["address", "memory", "store", "target", "type", "value"];
const utf8Encoder = new TextEncoder();
const coercionScratch = new DataView(new ArrayBuffer(8));

const planStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: planUnit,
  bool: planScalar,
  s8: planScalar,
  u8: planScalar,
  s16: planScalar,
  u16: planScalar,
  s32: planScalar,
  u32: planScalar,
  s64: planScalar,
  u64: planScalar,
  f32: planScalar,
  f64: planScalar,
  character: planScalar,
  text: planText,
  alias: planAlias,
  list: planList,
  record: planRecord,
  tuple: planTuple,
  option: planOption,
  result: planResult,
  variant: planVariant,
  enum: planEnum,
  flags: planFlags,
  "owned-resource": planOwnedResource,
  "borrowed-resource": planBorrowedResource,
}));

const materializeStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: materializeIdentity,
  bool: materializeIdentity,
  s8: materializeIdentity,
  u8: materializeIdentity,
  s16: materializeIdentity,
  u16: materializeIdentity,
  s32: materializeIdentity,
  u32: materializeIdentity,
  s64: materializeIdentity,
  u64: materializeIdentity,
  f32: materializeIdentity,
  f64: materializeIdentity,
  character: materializeIdentity,
  text: materializeText,
  alias: materializeAlias,
  list: materializeList,
  record: materializeRecord,
  tuple: materializeRecord,
  option: materializeVariant,
  result: materializeVariant,
  variant: materializeVariant,
  enum: materializeIdentity,
  flags: materializeIdentity,
  "owned-resource": materializeIdentity,
}));

const flatStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: lowerFlatUnit,
  bool: lowerFlatScalar,
  s8: lowerFlatScalar,
  u8: lowerFlatScalar,
  s16: lowerFlatScalar,
  u16: lowerFlatScalar,
  s32: lowerFlatScalar,
  u32: lowerFlatScalar,
  s64: lowerFlatScalar,
  u64: lowerFlatScalar,
  f32: lowerFlatScalar,
  f64: lowerFlatScalar,
  character: lowerFlatScalar,
  text: lowerFlatSequence,
  alias: lowerFlatAlias,
  list: lowerFlatSequence,
  record: lowerFlatRecord,
  tuple: lowerFlatRecord,
  option: lowerFlatVariant,
  result: lowerFlatVariant,
  variant: lowerFlatVariant,
  enum: lowerFlatEnum,
  flags: lowerFlatFlags,
  "owned-resource": lowerFlatResource,
}));

const memoryStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: writeMemoryUnit,
  bool: writeMemoryScalar,
  s8: writeMemoryScalar,
  u8: writeMemoryScalar,
  s16: writeMemoryScalar,
  u16: writeMemoryScalar,
  s32: writeMemoryScalar,
  u32: writeMemoryScalar,
  s64: writeMemoryScalar,
  u64: writeMemoryScalar,
  f32: writeMemoryScalar,
  f64: writeMemoryScalar,
  character: writeMemoryScalar,
  text: writeMemorySequence,
  alias: writeMemoryAlias,
  list: writeMemorySequence,
  record: writeMemoryRecord,
  tuple: writeMemoryRecord,
  option: writeMemoryVariant,
  result: writeMemoryVariant,
  variant: writeMemoryVariant,
  enum: writeMemoryEnum,
  flags: writeMemoryFlags,
  "owned-resource": writeMemoryResource,
}));

const coercions = Object.freeze(Object.assign(Object.create(null), {
  "f32:i32": value => {
    coercionScratch.setFloat32(0, value, true);
    return coercionScratch.getInt32(0, true);
  },
  "i32:i64": value => BigInt.asUintN(32, BigInt(value)),
  "f32:i64": value => {
    coercionScratch.setFloat32(0, value, true);
    return BigInt(coercionScratch.getUint32(0, true));
  },
  "f64:i64": value => {
    coercionScratch.setFloat64(0, value, true);
    return coercionScratch.getBigInt64(0, true);
  },
  "f32:f64": value => value,
}));

export function createRawCanonicalFlatValueLowerer(request = {}) {
  const actions = readFactory(request, "raw canonical flat-value lowerer");
  return Object.freeze(input => lowerFlat(input, actions));
}

export function createRawCanonicalMemoryValueWriter(request = {}) {
  const actions = readFactory(request, "raw canonical memory-value writer");
  return Object.freeze(input => writeMemory(input, actions));
}

function lowerFlat(request, actions) {
  assertExactObject(request, flatRequestKeys, "raw canonical flat-value lower");
  validateMemory(request.memory, request.target);
  flattenRawCanonicalType({ target: request.target, type: request.type });
  const transaction = createLoweringTransaction(request, actions);
  try {
    const plan = planValue(request.type, request.value, request.target, transaction);
    transaction.register();
    const materialized = materializeValue(plan, request.type, context(request), transaction);
    const values = lowerFlatValue(materialized, request.type, request.target);
    transaction.commit();
    return Object.freeze(values);
  } catch (error) {
    transaction.rollback(error);
  }
}

function writeMemory(request, actions) {
  assertExactObject(request, memoryRequestKeys, "raw canonical memory-value write");
  const valueLayout = planRawCanonicalTypeLayout({ target: request.target, type: request.type });
  const range = projectRawCanonicalMemoryRange({
    address: request.address,
    alignment: valueLayout.alignment,
    byteLength: valueLayout.size,
    memory: request.memory,
    target: request.target,
  });
  const transaction = createLoweringTransaction(request, actions);
  try {
    const plan = planValue(request.type, request.value, request.target, transaction);
    transaction.register();
    const materialized = materializeValue(plan, request.type, context(request), transaction);
    writeMemoryValue(materialized, request.type, valueLayout, range.index, context(request));
    transaction.commit();
  } catch (error) {
    transaction.rollback(error);
  }
}

function context(request) {
  return { memory: request.memory, target: request.target };
}

function planValue(type, value, target, transaction) {
  return planStrategies[type.kind](type, value, target, transaction);
}

function planUnit(_type, value) {
  if (value !== undefined) throw invalidValue("unit");
  return Object.freeze({ kind: "unit" });
}

function planScalar(type, value) {
  const lowered = lowerRawCanonicalFlatScalar({ kind: type.kind, value });
  return Object.freeze({ kind: type.kind, value, core: coreScalar(type.kind, lowered) });
}

function planText(_type, value) {
  if (typeof value !== "string") throw invalidValue("text");
  const bytes = utf8Encoder.encode(value);
  return Object.freeze({ bytes, kind: "text", length: bytes.byteLength });
}

function planAlias(type, value, target, transaction) {
  return Object.freeze({
    element: planValue(type.element, value, target, transaction),
    kind: "alias",
  });
}

function planList(type, value, target, transaction) {
  const values = underlyingKind(type.element) === "u8"
    ? snapshotByteList(value)
    : snapshotArray(value, undefined, "list");
  const elementLayout = planRawCanonicalTypeLayout({ target, type: type.element });
  if (elementLayout.size === 0 && values.length !== 0) {
    throw new TypeError("raw canonical zero-sized list elements are unsupported");
  }
  const byteLength = elementLayout.size * values.length;
  const items = values instanceof Uint8Array
    ? values
    : Object.freeze(values.map(item => planValue(type.element, item, target, transaction)));
  return Object.freeze({ byteLength, elementLayout, items, kind: "list", length: values.length });
}

function planRecord(type, value, target, transaction) {
  const values = snapshotObject(value, type.fields.map(field => field.javascriptName), "record");
  return Object.freeze({
    fields: Object.freeze(type.fields.map((field, index) =>
      planValue(field.type, values[index], target, transaction))),
    kind: "record",
  });
}

function planTuple(type, value, target, transaction) {
  const values = snapshotArray(value, type.fields.length, "tuple");
  return Object.freeze({
    fields: Object.freeze(type.fields.map((field, index) =>
      planValue(field.type, values[index], target, transaction))),
    kind: "tuple",
  });
}

function planOption(type, value, target, transaction) {
  if (!isOption(type.element)) {
    return planSelection(
      "option", value === undefined ? 0 : 1, value === undefined ? null : type.element,
      value, value !== undefined, target, transaction);
  }
  const tagged = snapshotTagged(value, ["none", "some"], "option");
  return planSelection(
    "option", tagged.index, tagged.index === 0 ? null : type.element,
    tagged.value, tagged.hasValue, target, transaction);
}

function planResult(type, value, target, transaction) {
  const tagged = snapshotTagged(value, ["ok", "err"], "result");
  const selectedType = tagged.index === 0 ? type.ok : type.error;
  return planSelection(
    "result", tagged.index, selectedType, tagged.value, tagged.hasValue, target, transaction);
}

function planVariant(type, value, target, transaction) {
  const tagged = snapshotTagged(value, type.cases.map(item => item.name), "variant");
  return planSelection(
    "variant", tagged.index, type.cases[tagged.index].type, tagged.value,
    tagged.hasValue, target, transaction);
}

function planSelection(kind, index, selectedType, value, hasValue, target, transaction) {
  if (hasValue !== (selectedType !== null)) throw invalidValue(kind);
  return Object.freeze({
    index,
    kind,
    payload: selectedType === null ? null : planValue(selectedType, value, target, transaction),
    selectedType,
  });
}

function planEnum(type, value) {
  if (typeof value !== "string") throw invalidValue("enum");
  const index = type.cases.findIndex(item => item.name === value);
  if (index < 0) throw invalidValue("enum");
  return Object.freeze({ index, kind: "enum" });
}

function planFlags(type, value) {
  const values = snapshotObject(value, type.flags.map(flag => flag.javascriptName), "flags");
  const words = Array(Math.max(1, Math.ceil(type.count / 32))).fill(0);
  values.forEach((selected, index) => {
    if (typeof selected !== "boolean") throw invalidValue("flags");
    if (selected) words[Math.floor(index / 32)] |= 1 << (index % 32);
  });
  return Object.freeze({ kind: "flags", words: Object.freeze(words.map(value => value >>> 0)) });
}

function planOwnedResource(type, value, _target, transaction) {
  validateResource(value);
  return Object.freeze({ entry: transaction.acquire(type.resourceType, value), kind: type.kind });
}

function planBorrowedResource() {
  throw new TypeError("raw canonical borrowed resources cannot be lowered as host results");
}

function materializeValue(plan, type, current, transaction) {
  return materializeStrategies[plan.kind](plan, type, current, transaction);
}

function materializeIdentity(plan) {
  return plan;
}

function materializeText(plan, _type, current, transaction) {
  const address = transaction.allocate(1, plan.length);
  if (plan.length !== 0) {
    const range = projectRawCanonicalMemoryRange({
      address,
      alignment: 1,
      byteLength: plan.length,
      memory: current.memory,
      target: current.target,
    });
    new Uint8Array(range.buffer, range.index, plan.length).set(plan.bytes);
  }
  return Object.freeze({ ...plan, address });
}

function materializeAlias(plan, type, current, transaction) {
  return Object.freeze({
    ...plan,
    element: materializeValue(plan.element, type.element, current, transaction),
  });
}

function materializeList(plan, type, current, transaction) {
  const address = transaction.allocate(plan.elementLayout.alignment, plan.byteLength);
  if (plan.items instanceof Uint8Array) {
    if (plan.byteLength !== 0) {
      const range = projectRawCanonicalMemoryRange({
        address,
        alignment: plan.elementLayout.alignment,
        byteLength: plan.byteLength,
        memory: current.memory,
        target: current.target,
      });
      new Uint8Array(range.buffer, range.index, plan.byteLength).set(plan.items);
    }
    return Object.freeze({ ...plan, address });
  }
  const items = Object.freeze(plan.items.map(item =>
    materializeValue(item, type.element, current, transaction)));
  if (plan.byteLength !== 0) {
    const range = projectRawCanonicalMemoryRange({
      address,
      alignment: plan.elementLayout.alignment,
      byteLength: plan.byteLength,
      memory: current.memory,
      target: current.target,
    });
    items.forEach((item, index) => writeMemoryValue(
      item,
      type.element,
      plan.elementLayout,
      range.index + index * plan.elementLayout.size,
      current));
  }
  return Object.freeze({ ...plan, address, items });
}

function materializeRecord(plan, type, current, transaction) {
  return Object.freeze({
    ...plan,
    fields: Object.freeze(plan.fields.map((field, index) =>
      materializeValue(field, type.fields[index].type, current, transaction))),
  });
}

function materializeVariant(plan, _type, current, transaction) {
  return plan.payload === null
    ? plan
    : Object.freeze({
      ...plan,
      payload: materializeValue(plan.payload, plan.selectedType, current, transaction),
    });
}

function lowerFlatValue(plan, type, target) {
  return flatStrategies[plan.kind](plan, type, target);
}

function lowerFlatUnit() {
  return [];
}

function lowerFlatScalar(plan) {
  return [plan.core];
}

function lowerFlatSequence(plan, _type, target) {
  return [plan.address, targetValue(plan.length, target)];
}

function lowerFlatAlias(plan, type, target) {
  return lowerFlatValue(plan.element, type.element, target);
}

function lowerFlatRecord(plan, type, target) {
  return plan.fields.flatMap((field, index) =>
    lowerFlatValue(field, type.fields[index].type, target));
}

function lowerFlatVariant(plan, type, target) {
  const caseTypes = selectionTypes(type);
  const joinedTypes = flattenRawCanonicalType({
    target,
    type: syntheticVariant(caseTypes),
  }).slice(1);
  const values = joinedTypes.map(coreZero);
  if (plan.payload !== null) {
    const selectedTypes = flattenRawCanonicalType({ target, type: plan.selectedType });
    const selectedValues = lowerFlatValue(plan.payload, plan.selectedType, target);
    selectedValues.forEach((value, index) => {
      values[index] = coerceCoreValue(value, selectedTypes[index], joinedTypes[index]);
    });
  }
  return [plan.index, ...values];
}

function lowerFlatEnum(plan) {
  return [plan.index];
}

function lowerFlatFlags(plan) {
  return plan.words.map(value => value | 0);
}

function lowerFlatResource(plan) {
  return [plan.entry.handle | 0];
}

function writeMemoryValue(plan, type, valueLayout, index, current) {
  memoryStrategies[plan.kind](plan, type, valueLayout, index, current);
}

function writeMemoryUnit() {}

function writeMemoryScalar(plan, _type, _layout, index, current) {
  writeRawCanonicalMemoryScalar({
    address: targetAddress(index, current.target),
    kind: plan.kind,
    memory: current.memory,
    target: current.target,
    value: plan.value,
  });
}

function writeMemorySequence(plan, _type, _layout, index, current) {
  const kind = current.target === "wasm64" ? "u64" : "u32";
  const size = current.target === "wasm64" ? 8 : 4;
  writeRawCanonicalMemoryScalar({
    address: targetAddress(index, current.target),
    kind,
    memory: current.memory,
    target: current.target,
    value: addressForMemory(plan.address, current.target),
  });
  writeRawCanonicalMemoryScalar({
    address: targetAddress(index + size, current.target),
    kind,
    memory: current.memory,
    target: current.target,
    value: targetValue(plan.length, current.target),
  });
}

function writeMemoryAlias(plan, type, valueLayout, index, current) {
  writeMemoryValue(plan.element, type.element, valueLayout, index, current);
}

function writeMemoryRecord(plan, type, valueLayout, index, current) {
  plan.fields.forEach((field, fieldIndex) => {
    const fieldLayout = valueLayout.fields[fieldIndex];
    writeMemoryValue(
      field,
      type.fields[fieldIndex].type,
      fieldLayout.layout,
      index + fieldLayout.offset,
      current);
  });
}

function writeMemoryVariant(plan, _type, valueLayout, index, current) {
  if (plan.payload !== null) {
    const payloadLayout = planRawCanonicalTypeLayout({
      target: current.target,
      type: plan.selectedType,
    });
    writeMemoryValue(
      plan.payload,
      plan.selectedType,
      payloadLayout,
      index + valueLayout.payloadOffset,
      current);
  }
  writeDiscriminant(valueLayout.discriminantSize, index, current.memory, plan.index);
}

function writeMemoryEnum(plan, _type, valueLayout, index, current) {
  writeDiscriminant(valueLayout.size, index, current.memory, plan.index);
}

function writeMemoryFlags(plan, _type, valueLayout, index, current) {
  const view = new DataView(current.memory.buffer);
  if (valueLayout.size === 1) view.setUint8(index, plan.words[0]);
  else if (valueLayout.size === 2) view.setUint16(index, plan.words[0], true);
  else plan.words.forEach((word, wordIndex) => view.setUint32(index + wordIndex * 4, word, true));
}

function writeMemoryResource(plan, _type, _layout, index, current) {
  writeRawCanonicalMemoryScalar({
    address: targetAddress(index, current.target),
    kind: "u32",
    memory: current.memory,
    target: current.target,
    value: plan.entry.handle,
  });
}

function createLoweringTransaction(request, actions) {
  const resources = [];
  const identities = new Map();
  const allocations = [];
  return Object.freeze({
    acquire(type, value) {
      let values = identities.get(type);
      if (values === undefined) {
        values = new Set();
        identities.set(type, values);
      }
      if (values.has(value)) throw new TypeError("raw canonical owned resource is duplicated");
      values.add(value);
      const entry = { handle: null, registered: false, type, value };
      resources.push(entry);
      return entry;
    },
    register() {
      for (const entry of resources) {
        const handle = actions.registerResource({
          release: value => actions.releaseResource({ type: entry.type, value }),
          store: request.store,
          type: entry.type,
          value: entry.value,
        });
        lowerRawCanonicalFlatScalar({ kind: "u32", value: handle });
        if (handle === 0) throw new TypeError("raw canonical resource handle zero is reserved");
        entry.handle = handle;
        entry.registered = true;
      }
    },
    allocate(alignment, byteLength) {
      const address = actions.allocateMemory({
        alignment,
        byteLength,
        memory: request.memory,
        target: request.target,
      });
      if (byteLength !== 0) allocations.push({ address, alignment, byteLength });
      return address;
    },
    commit() {
      resources.length = 0;
      allocations.length = 0;
    },
    rollback(error) {
      const failures = [];
      for (const allocation of allocations.reverse()) {
        captureFailure(failures, () => actions.deallocateMemory({
          ...allocation,
          memory: request.memory,
          target: request.target,
        }));
      }
      for (const entry of resources.reverse()) {
        captureFailure(failures, () => entry.registered
          ? actions.dropResource({ handle: entry.handle, store: request.store, type: entry.type })
          : actions.releaseResource({ type: entry.type, value: entry.value }));
      }
      if (failures.length === 0) throw error;
      throw new AggregateError(
        [error, ...failures],
        "raw canonical value lowering failed during cleanup");
    },
  });
}

function captureFailure(failures, action) {
  try {
    action();
  } catch (error) {
    failures.push(error);
  }
}

function snapshotByteList(value) {
  if (!(value instanceof Uint8Array)) throw invalidValue("byte list");
  return value.slice();
}

function snapshotArray(value, expectedLength, label) {
  if (!Array.isArray(value) || Object.getOwnPropertySymbols(value).length !== 0
      || expectedLength !== undefined && value.length !== expectedLength) {
    throw invalidValue(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const expectedNames = [
    "length",
    ...Array.from({ length: value.length }, (_, index) => `${index}`),
  ].sort();
  const names = Object.keys(descriptors).sort();
  if (names.length !== expectedNames.length
      || names.some((name, index) => name !== expectedNames[index])) {
    throw invalidValue(label);
  }
  return Array.from({ length: value.length }, (_, index) => {
    const descriptor = descriptors[index];
    if (!descriptor.enumerable || !("value" in descriptor)) throw invalidValue(label);
    return descriptor.value;
  });
}

function snapshotObject(value, expectedNames, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw invalidValue(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualNames = Object.keys(descriptors).sort();
  const names = [...expectedNames].sort();
  if (actualNames.length !== names.length
      || actualNames.some((name, index) => name !== names[index])) {
    throw invalidValue(label);
  }
  return expectedNames.map(name => {
    const descriptor = descriptors[name];
    if (!descriptor.enumerable || !("value" in descriptor)) throw invalidValue(label);
    return descriptor.value;
  });
}

function snapshotTagged(value, tags, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw invalidValue(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const tagDescriptor = descriptors.tag;
  if (tagDescriptor === undefined || !tagDescriptor.enumerable || !("value" in tagDescriptor)
      || typeof tagDescriptor.value !== "string") {
    throw invalidValue(label);
  }
  const index = tags.indexOf(tagDescriptor.value);
  if (index < 0) throw invalidValue(label);
  const hasValue = Object.hasOwn(descriptors, "val");
  const valueDescriptor = descriptors.val;
  if (hasValue && (!valueDescriptor.enumerable || !("value" in valueDescriptor))) {
    throw invalidValue(label);
  }
  const actualNames = Object.keys(descriptors).sort();
  const expectedNames = hasValue ? ["tag", "val"] : ["tag"];
  if (actualNames.length !== expectedNames.length
      || actualNames.some((name, nameIndex) => name !== expectedNames[nameIndex])) {
    throw invalidValue(label);
  }
  return { index, value: hasValue ? valueDescriptor.value : undefined, hasValue };
}

function validateResource(value) {
  if (value === null || typeof value !== "object" && typeof value !== "function") {
    throw invalidValue("owned resource");
  }
}

function selectionTypes(type) {
  if (type.kind === "option") return [null, type.element];
  if (type.kind === "result") return [type.ok, type.error];
  return type.cases.map(item => item.type);
}

function syntheticVariant(caseTypes) {
  return {
    kind: "variant",
    cases: caseTypes.map((type, index) => ({ name: `case-${index}`, type })),
  };
}

function coreScalar(kind, value) {
  if (["bool", "s8", "u8", "s16", "u16", "s32", "u32", "character"].includes(kind)) {
    return value | 0;
  }
  if (kind === "s64" || kind === "u64") return BigInt.asIntN(64, value);
  return value;
}

function coerceCoreValue(value, source, destination) {
  return source === destination ? value : coercions[`${source}:${destination}`](value);
}

function coreZero(kind) {
  return kind === "i64" ? 0n : 0;
}

function writeDiscriminant(size, index, memory, value) {
  const view = new DataView(memory.buffer);
  if (size === 1) view.setUint8(index, value);
  else if (size === 2) view.setUint16(index, value, true);
  else view.setUint32(index, value, true);
}

function readFactory(request, label) {
  assertExactObject(request, factoryKeys, label);
  if (factoryKeys.some(key => typeof request[key] !== "function")) {
    throw new TypeError(`${label} capabilities are invalid`);
  }
  return Object.freeze(Object.fromEntries(factoryKeys.map(key => [key, request[key]])));
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

function underlyingKind(type) {
  let current = type;
  while (current.kind === "alias") current = current.element;
  return current.kind;
}

function isOption(type) {
  return underlyingKind(type) === "option";
}

function targetAddress(index, target) {
  return target === "wasm64" ? BigInt(index) : index;
}

function targetValue(value, target) {
  return target === "wasm64" ? BigInt(value) : value;
}

function addressForMemory(value, target) {
  return target === "wasm64" ? BigInt.asUintN(64, value) : value >>> 0;
}

function invalidValue(kind) {
  return new TypeError(`raw canonical ${kind} value is invalid`);
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

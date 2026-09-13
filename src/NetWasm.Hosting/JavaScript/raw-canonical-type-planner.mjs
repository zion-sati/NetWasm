const requestKeys = ["target", "type"];
const emptyFields = Object.freeze([]);
const targets = Object.freeze(Object.assign(Object.create(null), {
  wasm32: Object.freeze({ addressSize: 4, addressType: "i32" }),
  wasm64: Object.freeze({ addressSize: 8, addressType: "i64" }),
}));
const scalarLayouts = Object.freeze(Object.assign(Object.create(null), {
  unit: Object.freeze({ size: 0, alignment: 1 }),
  bool: Object.freeze({ size: 1, alignment: 1 }),
  s8: Object.freeze({ size: 1, alignment: 1 }),
  u8: Object.freeze({ size: 1, alignment: 1 }),
  s16: Object.freeze({ size: 2, alignment: 2 }),
  u16: Object.freeze({ size: 2, alignment: 2 }),
  s32: Object.freeze({ size: 4, alignment: 4 }),
  u32: Object.freeze({ size: 4, alignment: 4 }),
  s64: Object.freeze({ size: 8, alignment: 8 }),
  u64: Object.freeze({ size: 8, alignment: 8 }),
  f32: Object.freeze({ size: 4, alignment: 4 }),
  f64: Object.freeze({ size: 8, alignment: 8 }),
  character: Object.freeze({ size: 4, alignment: 4 }),
  "owned-resource": Object.freeze({ size: 4, alignment: 4 }),
  "borrowed-resource": Object.freeze({ size: 4, alignment: 4 }),
}));
const typeKeys = Object.freeze(Object.assign(Object.create(null), {
  unit: ["kind"],
  bool: ["kind"],
  s8: ["kind"],
  u8: ["kind"],
  s16: ["kind"],
  u16: ["kind"],
  s32: ["kind"],
  u32: ["kind"],
  s64: ["kind"],
  u64: ["kind"],
  f32: ["kind"],
  f64: ["kind"],
  character: ["kind"],
  text: ["kind"],
  alias: ["element", "kind"],
  list: ["element", "kind"],
  record: ["fields", "kind"],
  tuple: ["fields", "kind"],
  option: ["element", "kind"],
  result: ["error", "kind", "ok"],
  variant: ["cases", "kind"],
  enum: ["cases", "kind"],
  flags: ["count", "flags", "kind"],
  "owned-resource": ["kind", "resourceType"],
  "borrowed-resource": ["kind", "resourceType"],
}));

const layoutStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: scalarLayout,
  bool: scalarLayout,
  s8: scalarLayout,
  u8: scalarLayout,
  s16: scalarLayout,
  u16: scalarLayout,
  s32: scalarLayout,
  u32: scalarLayout,
  s64: scalarLayout,
  u64: scalarLayout,
  f32: scalarLayout,
  f64: scalarLayout,
  character: scalarLayout,
  text: pointerPairLayout,
  alias: aliasLayout,
  list: pointerPairLayout,
  record: recordLayout,
  tuple: recordLayout,
  option: optionLayout,
  result: resultLayout,
  variant: variantTypeLayout,
  enum: enumLayout,
  flags: flagsLayout,
  "owned-resource": scalarLayout,
  "borrowed-resource": scalarLayout,
}));

const flattenStrategies = Object.freeze(Object.assign(Object.create(null), {
  unit: () => [],
  bool: i32Flat,
  s8: i32Flat,
  u8: i32Flat,
  s16: i32Flat,
  u16: i32Flat,
  s32: i32Flat,
  u32: i32Flat,
  s64: () => ["i64"],
  u64: () => ["i64"],
  f32: () => ["f32"],
  f64: () => ["f64"],
  character: i32Flat,
  text: pointerPairFlat,
  alias: aliasFlat,
  list: pointerPairFlat,
  record: recordFlat,
  tuple: recordFlat,
  option: optionFlat,
  result: resultFlat,
  variant: variantFlat,
  enum: i32Flat,
  flags: flagsFlat,
  "owned-resource": i32Flat,
  "borrowed-resource": i32Flat,
}));

for (const keys of Object.values(typeKeys)) Object.freeze(keys);

export function planRawCanonicalTypeLayout(request = {}) {
  const { type, target } = readRequest(request, "raw canonical type-layout planning");
  return plan(type, target);
}

export function flattenRawCanonicalType(request = {}) {
  const { type, target } = readRequest(request, "raw canonical type flattening");
  return Object.freeze(flatten(type, target));
}

function plan(type, target) {
  const kind = readType(type);
  return layoutStrategies[kind](type, target);
}

function flatten(type, target) {
  const kind = readType(type);
  return flattenStrategies[kind](type, target);
}

function scalarLayout(type) {
  if (type.kind === "owned-resource" || type.kind === "borrowed-resource") {
    readResourceType(type.resourceType);
  }
  return layout(scalarLayouts[type.kind].size, scalarLayouts[type.kind].alignment);
}

function pointerPairLayout(type, target) {
  if (type.kind === "list") plan(type.element, target);
  return layout(checkedMultiply(target.addressSize, 2), target.addressSize);
}

function aliasLayout(type, target) {
  return plan(type.element, target);
}

function recordLayout(type, target) {
  const named = type.kind === "record";
  const fields = readFields(type.fields, named);
  const planned = [];
  let offset = 0;
  let alignment = 1;
  for (const field of fields) {
    const fieldLayout = plan(field.type, target);
    alignment = Math.max(alignment, fieldLayout.alignment);
    offset = align(offset, fieldLayout.alignment);
    planned.push(Object.freeze({ name: field.name, offset, layout: fieldLayout }));
    offset = checkedAdd(offset, fieldLayout.size);
  }
  return layout(align(offset, alignment), alignment, Object.freeze(planned));
}

function optionLayout(type, target) {
  return variantLayout([null, type.element], target);
}

function resultLayout(type, target) {
  return variantLayout([type.ok, type.error], target);
}

function variantTypeLayout(type, target) {
  return variantLayout(readCases(type.cases, false).map(item => item.type), target);
}

function enumLayout(type) {
  return layout(discriminantSize(readCases(type.cases, true).length),
    discriminantSize(type.cases.length));
}

function variantLayout(caseTypes, target) {
  const discriminant = discriminantSize(caseTypes.length);
  const payloads = caseTypes.filter(type => type !== null).map(type => plan(type, target));
  const payloadAlignment = payloads.reduce(
    (maximum, current) => Math.max(maximum, current.alignment), 1);
  const payloadSize = payloads.reduce(
    (maximum, current) => Math.max(maximum, current.size), 0);
  const alignment = Math.max(discriminant, payloadAlignment);
  const payloadOffset = align(discriminant, payloadAlignment);
  return layout(
    align(checkedAdd(payloadOffset, payloadSize), alignment),
    alignment,
    emptyFields,
    discriminant,
    payloadOffset);
}

function flagsLayout(type) {
  readFlags(type);
  if (type.count <= 8) return layout(1, 1);
  if (type.count <= 16) return layout(2, 2);
  return layout(checkedMultiply(Math.ceil(type.count / 32), 4), 4);
}

function i32Flat() {
  return ["i32"];
}

function pointerPairFlat(type, target) {
  if (type.kind === "list") flatten(type.element, target);
  return [target.addressType, target.addressType];
}

function aliasFlat(type, target) {
  return flatten(type.element, target);
}

function recordFlat(type, target) {
  return readFields(type.fields, type.kind === "record")
    .flatMap(field => flatten(field.type, target));
}

function optionFlat(type, target) {
  return flattenVariant([null, type.element], target);
}

function resultFlat(type, target) {
  return flattenVariant([type.ok, type.error], target);
}

function variantFlat(type, target) {
  return flattenVariant(readCases(type.cases, false).map(item => item.type), target);
}

function flattenVariant(caseTypes, target) {
  const payloads = caseTypes.map(type => type === null ? [] : flatten(type, target));
  const width = payloads.reduce((maximum, payload) => Math.max(maximum, payload.length), 0);
  const flattened = ["i32"];
  for (let index = 0; index < width; index++) {
    flattened.push(payloads
      .filter(payload => index < payload.length)
      .map(payload => payload[index])
      .reduce(joinCoreTypes));
  }
  return flattened;
}

function flagsFlat(type) {
  readFlags(type);
  return Array(Math.max(1, Math.ceil(type.count / 32))).fill("i32");
}

function joinCoreTypes(left, right) {
  if (left === right) return left;
  if (left === "i64" || right === "i64") return "i64";
  if (left === "f32" && right === "f64" || left === "f64" && right === "f32") {
    return "f64";
  }
  if (left === "i32" && right === "f32" || left === "f32" && right === "i32") {
    return "i32";
  }
  return "i64";
}

function readRequest(request, label) {
  assertExactObject(request, requestKeys, label);
  const target = typeof request.target === "string" && Object.hasOwn(targets, request.target)
    ? targets[request.target]
    : undefined;
  if (target === undefined) throw new TypeError("raw canonical type target is unsupported");
  return { type: request.type, target };
}

function readType(type) {
  if (type === null || typeof type !== "object" || Array.isArray(type)) {
    throw new TypeError("raw canonical type is unsupported");
  }
  const kindDescriptor = Object.getOwnPropertyDescriptor(type, "kind");
  const kind = kindDescriptor?.enumerable && "value" in kindDescriptor
    ? kindDescriptor.value
    : undefined;
  if (typeof kind !== "string" || !Object.hasOwn(typeKeys, kind)) {
    throw new TypeError("raw canonical type is unsupported");
  }
  assertExactObject(type, typeKeys[kind], "raw canonical type");
  return kind;
}

function readFields(value, named) {
  if (!Array.isArray(value)) throw new TypeError("raw canonical fields are invalid");
  const names = new Set();
  const javaScriptNames = new Set();
  for (const field of value) {
    assertExactObject(field,
      named ? ["javascriptName", "name", "type"] : ["name", "type"],
      "raw canonical field");
    readName(field.name, "field");
    if (names.has(field.name)) throw new TypeError("raw canonical field name is duplicated");
    names.add(field.name);
    if (named) {
      readName(field.javascriptName, "field JavaScript");
      if (javaScriptNames.has(field.javascriptName)) {
        throw new TypeError("raw canonical field JavaScript name is duplicated");
      }
      javaScriptNames.add(field.javascriptName);
    }
  }
  return value;
}

function readCases(value, enumeration) {
  if (!Array.isArray(value) || value.length === 0) {
    throw new TypeError("raw canonical cases are invalid");
  }
  const names = new Set();
  for (const item of value) {
    assertExactObject(item, ["name", "type"], "raw canonical case");
    readName(item.name, "case");
    if (names.has(item.name)) throw new TypeError("raw canonical case name is duplicated");
    names.add(item.name);
    if (enumeration && item.type !== null) {
      throw new TypeError("raw canonical enum case payload is invalid");
    }
  }
  return value;
}

function readFlags(type) {
  if (!Number.isSafeInteger(type.count) || type.count < 0
      || !Array.isArray(type.flags) || type.flags.length !== type.count) {
    throw new TypeError("raw canonical flags are invalid");
  }
  const names = new Set();
  const javaScriptNames = new Set();
  for (const flag of type.flags) {
    assertExactObject(flag, ["javascriptName", "name"], "raw canonical flag");
    readName(flag.name, "flag");
    readName(flag.javascriptName, "flag JavaScript");
    if (names.has(flag.name) || javaScriptNames.has(flag.javascriptName)) {
      throw new TypeError("raw canonical flag name is duplicated");
    }
    names.add(flag.name);
    javaScriptNames.add(flag.javascriptName);
  }
}

function readResourceType(value) {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new TypeError("raw canonical resource type is invalid");
  }
}

function readName(value, label) {
  if (typeof value !== "string" || value.length === 0) {
    throw new TypeError(`raw canonical ${label} name is invalid`);
  }
}

function discriminantSize(count) {
  return count <= 0x100 ? 1 : count <= 0x1_0000 ? 2 : 4;
}

function layout(size, alignment, fields = emptyFields, discriminantSize = 0, payloadOffset = 0) {
  return Object.freeze({ size, alignment, discriminantSize, payloadOffset, fields });
}

function align(value, alignment) {
  return Math.ceil(value / alignment) * alignment;
}

function checkedAdd(left, right) {
  return left + right;
}

function checkedMultiply(left, right) {
  return left * right;
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

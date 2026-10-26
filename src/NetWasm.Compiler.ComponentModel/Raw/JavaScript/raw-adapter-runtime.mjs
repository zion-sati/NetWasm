// The helpers a generated raw adapter's canonical-ABI bindings call. The
// adapter writer appends this file, without its export keywords, to every
// adapter so the adapter stays dependency-free; a Release build's bundler
// drops the helpers no binding uses. Core values come from WebAssembly calls,
// so they are already of their declared core types.

const maximumSequenceByteLength = 0x0fff_ffff;
const utf8Decoder = new TextDecoder("utf-8", { fatal: true });
const utf8Encoder = new TextEncoder();
const coercionScratch = new DataView(new ArrayBuffer(8));

let viewedBuffer = null;
let cachedView = null;

// A view over linear memory, renewed when memory grows and replaces its buffer.
export function rcView(memory) {
  const buffer = memory.buffer;
  if (buffer !== viewedBuffer) {
    viewedBuffer = buffer;
    cachedView = new DataView(buffer);
  }
  return cachedView;
}

// The byte index of `byteLength` bytes at a wasm32 (number) or wasm64 (bigint)
// address, which must be aligned and inside linear memory.
export function rcIndex(memory, address, alignment, byteLength) {
  if (typeof address === "bigint") {
    const index = BigInt.asUintN(64, address);
    if (index % BigInt(alignment) !== 0n) {
      throw new RangeError("raw canonical memory address is unaligned");
    }
    const limit = BigInt(memory.buffer.byteLength);
    if (index > limit || BigInt(byteLength) > limit - index) {
      throw new RangeError("raw canonical memory range is out of bounds");
    }
    return Number(index);
  }
  const index = address >>> 0;
  if (index % alignment !== 0) throw new RangeError("raw canonical memory address is unaligned");
  const limit = memory.buffer.byteLength;
  if (index > limit || byteLength > limit - index) {
    throw new RangeError("raw canonical memory range is out of bounds");
  }
  return index;
}

export function rcSequenceLength(value, label) {
  const length = typeof value === "bigint" ? BigInt.asUintN(64, value) : BigInt(value >>> 0);
  if (length > BigInt(maximumSequenceByteLength)) {
    throw new RangeError(`raw canonical ${label} length is too large`);
  }
  return Number(length);
}

export function rcListByteLength(elementSize, length) {
  if (elementSize === 0 && length !== 0) {
    throw new TypeError("raw canonical zero-sized list elements are unsupported");
  }
  const byteLength = elementSize * length;
  if (byteLength > maximumSequenceByteLength) {
    throw new RangeError("raw canonical list byte length is too large");
  }
  return byteLength;
}

// Lowering checks only that a non-empty list has sized elements.
export function rcLoweredListByteLength(elementSize, length) {
  if (elementSize === 0 && length !== 0) {
    throw new TypeError("raw canonical zero-sized list elements are unsupported");
  }
  return elementSize * length;
}

export function rcReadText(memory, address, rawLength) {
  const length = rcSequenceLength(rawLength, "text");
  const index = rcIndex(memory, address, 1, length);
  const snapshot = new Uint8Array(memory.buffer, index, length).slice();
  try {
    return utf8Decoder.decode(snapshot);
  } catch (error) {
    throw new TypeError("raw canonical UTF-8 text is invalid", { cause: error });
  }
}

export function rcReadBytes(memory, address, rawLength) {
  const length = rcSequenceLength(rawLength, "list");
  rcListByteLength(1, length);
  const index = rcIndex(memory, address, 1, length);
  return new Uint8Array(memory.buffer, index, length).slice();
}

export function rcCharacter(codePoint) {
  if (codePoint > 0x10ffff || codePoint >= 0xd800 && codePoint <= 0xdfff) {
    throw new TypeError("raw canonical character code point is invalid");
  }
  return String.fromCodePoint(codePoint);
}

export function rcEnumCase(names, index) {
  if (index >= names.length) throw new TypeError("raw canonical enum discriminant is invalid");
  return names[index];
}

export function rcFlags(names, words) {
  const value = {};
  for (let index = 0; index < names.length; index++) {
    value[names[index]] = ((words[Math.floor(index / 32)] >>> (index % 32)) & 1) === 1;
  }
  return value;
}

// A variant's joined flat slot, reread as the selected case's core type.
export function rcF32FromI32(value) {
  coercionScratch.setInt32(0, value, true);
  return coercionScratch.getFloat32(0, true);
}

export function rcI32FromI64(value) {
  return Number(BigInt.asIntN(32, value));
}

export function rcF32FromI64(value) {
  coercionScratch.setBigInt64(0, value, true);
  return coercionScratch.getFloat32(0, true);
}

export function rcF64FromI64(value) {
  coercionScratch.setBigInt64(0, value, true);
  return coercionScratch.getFloat64(0, true);
}

export function rcInvalid(label) {
  return new TypeError(`raw canonical ${label} value is invalid`);
}

export function rcUnit(value) {
  if (value !== undefined) throw rcInvalid("unit");
  return undefined;
}

export function rcBorrowedResult() {
  throw new TypeError("raw canonical borrowed resources cannot be lowered as host results");
}

export function rcBool(value) {
  if (typeof value !== "boolean") throw rcInvalid("bool");
  return value ? 1 : 0;
}

export function rcInteger(value, minimum, maximum, label) {
  if (!Number.isInteger(value) || value < minimum || value > maximum) throw rcInvalid(label);
  return value;
}

export function rcBigInteger(value, minimum, maximum, label) {
  if (typeof value !== "bigint" || value < minimum || value > maximum) throw rcInvalid(label);
  return value;
}

export function rcFloat32(value) {
  if (typeof value !== "number") throw rcInvalid("f32");
  return Math.fround(value);
}

export function rcFloat64(value) {
  if (typeof value !== "number") throw rcInvalid("f64");
  return value;
}

export function rcCodePoint(value) {
  if (typeof value !== "string" || [...value].length !== 1) throw rcInvalid("character");
  const codePoint = value.codePointAt(0);
  if (codePoint >= 0xd800 && codePoint <= 0xdfff) throw rcInvalid("character");
  return codePoint;
}

export function rcEncodeText(value) {
  if (typeof value !== "string") throw rcInvalid("text");
  return utf8Encoder.encode(value);
}

export function rcByteList(value) {
  if (!(value instanceof Uint8Array)) throw rcInvalid("byte list");
  return value.slice();
}

// JavaScript Component Model bindings may carry a numeric list as its typed array.
export function rcNumberList(value, typedArray) {
  return value instanceof typedArray ? Array.from(value) : rcArray(value, undefined, "list");
}

export function rcArray(value, expectedLength, label) {
  if (!Array.isArray(value) || Object.getOwnPropertySymbols(value).length !== 0
      || expectedLength !== undefined && value.length !== expectedLength) {
    throw rcInvalid(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const expectedNames = [
    "length",
    ...Array.from({ length: value.length }, (_, index) => `${index}`),
  ].sort();
  const names = Object.keys(descriptors).sort();
  if (names.length !== expectedNames.length
      || names.some((name, index) => name !== expectedNames[index])) {
    throw rcInvalid(label);
  }
  return Array.from({ length: value.length }, (_, index) => {
    const descriptor = descriptors[index];
    if (!descriptor.enumerable || !("value" in descriptor)) throw rcInvalid(label);
    return descriptor.value;
  });
}

export function rcObject(value, expectedNames, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw rcInvalid(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualNames = Object.keys(descriptors).sort();
  const names = [...expectedNames].sort();
  if (actualNames.length !== names.length
      || actualNames.some((name, index) => name !== names[index])) {
    throw rcInvalid(label);
  }
  return expectedNames.map(name => {
    const descriptor = descriptors[name];
    if (!descriptor.enumerable || !("value" in descriptor)) throw rcInvalid(label);
    return descriptor.value;
  });
}

// A tagged value's case index and payload, which must be present exactly when
// the selected case carries a payload.
export function rcTagged(value, tags, payloads, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw rcInvalid(label);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const tagDescriptor = descriptors.tag;
  if (tagDescriptor === undefined || !tagDescriptor.enumerable || !("value" in tagDescriptor)
      || typeof tagDescriptor.value !== "string") {
    throw rcInvalid(label);
  }
  const index = tags.indexOf(tagDescriptor.value);
  if (index < 0) throw rcInvalid(label);
  const hasValue = Object.hasOwn(descriptors, "val");
  const valueDescriptor = descriptors.val;
  if (hasValue && (!valueDescriptor.enumerable || !("value" in valueDescriptor))) {
    throw rcInvalid(label);
  }
  const actualNames = Object.keys(descriptors).sort();
  const expectedNames = hasValue ? ["tag", "val"] : ["tag"];
  if (actualNames.length !== expectedNames.length
      || actualNames.some((name, nameIndex) => name !== expectedNames[nameIndex])) {
    throw rcInvalid(label);
  }
  if (hasValue !== payloads[index]) throw rcInvalid(label);
  return { index, value: hasValue ? valueDescriptor.value : undefined };
}

export function rcEnumIndex(value, names) {
  if (typeof value !== "string") throw rcInvalid("enum");
  const index = names.indexOf(value);
  if (index < 0) throw rcInvalid("enum");
  return index;
}

export function rcFlagWords(value, names, words) {
  const selected = rcObject(value, names, "flags");
  const result = Array(words).fill(0);
  selected.forEach((flag, index) => {
    if (typeof flag !== "boolean") throw rcInvalid("flags");
    if (flag) result[Math.floor(index / 32)] |= 1 << (index % 32);
  });
  return result.map(word => word >>> 0);
}

// Owned handles lifted from one call's arguments leave the table only once
// every argument has lifted.
export function rcLiftTransaction(host, store) {
  const owned = [];
  const identities = new Set();
  return {
    borrow(type, handle) {
      return host.borrow({ handle, store, type });
    },
    own(type, handle) {
      const request = { handle, store, type };
      const value = host.borrow(request);
      const identity = `${type}:${handle}`;
      if (identities.has(identity)) throw new TypeError("raw canonical owned resource is duplicated");
      identities.add(identity);
      owned.push(request);
      return value;
    },
    commit() {
      for (const request of owned) host.transfer(request);
    },
  };
}

// Lowering registers a result's owned resources and allocates its sequences;
// a failure frees what it allocated and releases or drops those resources.
export function rcLowerTransaction(host, memory, store, wasm64) {
  const resources = [];
  const identities = new Map();
  const allocations = [];
  const zero = wasm64 ? 0n : 0;
  const coreSize = value => wasm64 ? BigInt(value) : value;
  return {
    acquire(type, value) {
      if (value === null || typeof value !== "object" && typeof value !== "function") {
        throw rcInvalid("owned resource");
      }
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
        const handle = host.register({
          release: value => host.release({ type: entry.type, value }),
          store,
          type: entry.type,
          value: entry.value,
        });
        rcInteger(handle, 0, 0xffff_ffff, "u32");
        if (handle === 0) throw new TypeError("raw canonical resource handle zero is reserved");
        entry.handle = handle;
        entry.registered = true;
      }
    },
    allocate(alignment, byteLength) {
      if (byteLength === 0) return zero;
      const address = host.reallocate(zero, zero, coreSize(alignment), coreSize(byteLength));
      rcIndex(memory, address, alignment, byteLength);
      allocations.push({ address, alignment, byteLength });
      return address;
    },
    commit() {
      resources.length = 0;
      allocations.length = 0;
    },
    rollback(error) {
      const failures = [];
      for (const allocation of allocations.reverse()) {
        rcCapture(failures, () => {
          rcIndex(memory, allocation.address, allocation.alignment, allocation.byteLength);
          host.reallocate(
            allocation.address, coreSize(allocation.byteLength), coreSize(allocation.alignment), zero);
        });
      }
      for (const entry of resources.reverse()) {
        rcCapture(failures, () => entry.registered
          ? host.drop({ handle: entry.handle, store, type: entry.type })
          : host.release({ type: entry.type, value: entry.value }));
      }
      if (failures.length === 0) throw error;
      throw new AggregateError(
        [error, ...failures],
        "raw canonical value lowering failed during cleanup");
    },
  };
}

function rcCapture(failures, action) {
  try {
    action();
  } catch (error) {
    failures.push(error);
  }
}

export function rcTaggedResult(tag, hasPayload, value) {
  const result = Object.create(null);
  result.tag = tag;
  if (hasPayload) result.val = value;
  return Object.freeze(result);
}

// A provider that throws reports a result's error case through its `payload`;
// any other Error is a host failure.
export function rcErrorPayload(error) {
  if (error !== null && (typeof error === "object" || typeof error === "function")) {
    const descriptor = Object.getOwnPropertyDescriptor(error, "payload");
    if (descriptor !== undefined) {
      if (!("value" in descriptor)) {
        throw new TypeError("raw provider error payload must be a data property");
      }
      return descriptor.value;
    }
  }
  if (error instanceof Error) throw error;
  return error;
}

export function rcFunction(providers, interfaceName, name) {
  const owner = rcProvider(providers, interfaceName);
  const member = rcDataProperty(owner, name, "raw provider function");
  if (typeof member !== "function") throw new TypeError("raw provider function is invalid");
  return (...parameters) => member.apply(owner, parameters);
}

export function rcStatic(providers, interfaceName, resourceName, name) {
  const owner = rcResource(providers, interfaceName, resourceName);
  const member = rcDataProperty(owner, name, "raw provider static function");
  if (typeof member !== "function") throw new TypeError("raw provider static function is invalid");
  return (...parameters) => member.apply(owner, parameters);
}

export function rcConstructor(providers, interfaceName, resourceName) {
  const resource = rcResource(providers, interfaceName, resourceName);
  return (...parameters) => Reflect.construct(resource, parameters);
}

export function rcMethod(providers, interfaceName, resourceName, name) {
  const resource = rcResource(providers, interfaceName, resourceName);
  if (typeof rcInheritedDataProperty(resource.prototype, name, "raw provider method") !== "function") {
    throw new TypeError("raw provider method is invalid");
  }
  return (receiver, ...parameters) => {
    if (!(receiver instanceof resource)) throw new TypeError("raw provider method receiver is invalid");
    const method = rcInheritedDataProperty(receiver, name, "raw provider method");
    if (typeof method !== "function") throw new TypeError("raw provider method is invalid");
    return method.apply(receiver, parameters);
  };
}

function rcProvider(providers, interfaceName) {
  const source = rcDataProperty(providers, interfaceName, "raw logical provider");
  if (source === null || typeof source !== "object") {
    throw new TypeError(`raw logical provider '${interfaceName}' is unavailable`);
  }
  return source;
}

function rcResource(providers, interfaceName, resourceName) {
  const resource = rcDataProperty(
    rcProvider(providers, interfaceName), resourceName, "raw provider resource");
  if (typeof resource !== "function") {
    throw new TypeError(`raw provider resource '${resourceName}' is unavailable`);
  }
  return resource;
}

function rcDataProperty(value, name, label) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
  return descriptor.value;
}

function rcInheritedDataProperty(value, name, label) {
  let current = value;
  while (current !== null) {
    const descriptor = Object.getOwnPropertyDescriptor(current, name);
    if (descriptor !== undefined) {
      if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
      return descriptor.value;
    }
    current = Object.getPrototypeOf(current);
  }
  return undefined;
}

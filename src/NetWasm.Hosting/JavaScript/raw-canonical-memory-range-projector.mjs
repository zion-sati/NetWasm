const requestKeys = ["address", "alignment", "byteLength", "memory", "target"];
const targets = Object.freeze(Object.assign(Object.create(null), {
  wasm32: Object.freeze({ kind: "number", minimum: -0x8000_0000, maximum: 0xffff_ffff }),
  wasm64: Object.freeze({
    kind: "bigint",
    minimum: -0x8000_0000_0000_0000n,
    maximum: 0xffff_ffff_ffff_ffffn,
  }),
}));

export function projectRawCanonicalMemoryRange(request = {}) {
  assertExactObject(request, requestKeys, "raw canonical memory-range projection");
  const target = Object.hasOwn(targets, request.target) ? targets[request.target] : undefined;
  if (target === undefined) {
    throw new TypeError("raw canonical memory target is unsupported");
  }
  if (!(request.memory instanceof WebAssembly.Memory)) {
    throw new TypeError("raw canonical memory is invalid");
  }
  if (!Number.isSafeInteger(request.byteLength) || request.byteLength < 0) {
    throw new TypeError("raw canonical memory byte length is invalid");
  }
  if (!Number.isSafeInteger(request.alignment) || request.alignment <= 0
      || !Number.isInteger(Math.log2(request.alignment))) {
    throw new TypeError("raw canonical memory alignment is invalid");
  }

  const address = normalizeAddress(request.address, target);
  const alignment = BigInt(request.alignment);
  if (address % alignment !== 0n) {
    throw new RangeError("raw canonical memory address is unaligned");
  }

  const buffer = request.memory.buffer;
  const limit = BigInt(buffer.byteLength);
  const byteLength = BigInt(request.byteLength);
  if (address > limit || byteLength > limit - address) {
    throw new RangeError("raw canonical memory range is out of bounds");
  }
  return Object.freeze({
    buffer,
    byteLength: request.byteLength,
    index: Number(address),
  });
}

function normalizeAddress(value, target) {
  if (target.kind === "number") {
    if (!Number.isInteger(value) || value < target.minimum || value > target.maximum) {
      throw new TypeError("raw canonical wasm32 address is invalid");
    }
    return BigInt(value >>> 0);
  }
  if (typeof value !== "bigint" || value < target.minimum || value > target.maximum) {
    throw new TypeError("raw canonical wasm64 address is invalid");
  }
  return BigInt.asUintN(64, value);
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

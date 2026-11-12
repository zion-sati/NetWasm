const sinkKeys = ["write"];
const chunkLimit = 64 * 1024;

// Copies a guest's stream<u8> (wasi:cli/stdout and stderr write-via-stream) into a host
// sink until the guest drops its writable end, and settles as the write's future does:
// ok once every byte reached the sink, or the WASI error code a failed sink write maps to.
// A failed copy drops the guest's stream, so the guest sees a closed pipe rather than waiting
// for a reader that will never come back.
export async function writeStreamToSink(stream, sink) {
  validateOutputSink(sink);
  const read = createGuestByteReader(stream);
  try {
    for (;;) {
      const chunk = await read();
      if (chunk === null) return Object.freeze({ tag: "ok", val: undefined });
      sink.write(chunk);
    }
  } catch {
    dropGuestStream(stream);
    return Object.freeze({ tag: "err", val: "io" });
  }
}

// Drops the host's end of a guest stream: a jco stream end disposes, an async iterator returns.
export function dropGuestStream(stream) {
  const dispose = stream?.[Symbol.dispose] ?? stream?.[Symbol.for("dispose")];
  try {
    if (typeof dispose === "function") dispose.call(stream);
    else if (typeof stream?.return === "function") stream.return();
  } catch {
    // The stream's guest already went away; nothing is left to release.
  }
}

export function validateOutputSink(value, label = "output sink") {
  assertExactDataObject(value, sinkKeys, label);
  if (!Object.isFrozen(value) || typeof value.write !== "function") {
    throw new TypeError(`${label} must be an immutable synchronous writer`);
  }
  return value;
}

// jco hands a host import a stream it can read in chunks or iterate, and a raw host hands it
// an async iterator; either yields bytes until the guest drops the writable end, then null.
export function createGuestByteReader(stream) {
  if (stream !== null && typeof stream === "object" && typeof stream.read === "function") {
    return async () => readChunk(await stream.read({ count: chunkLimit }));
  }
  if (stream !== null && typeof stream === "object"
      && typeof stream[Symbol.asyncIterator] === "function") {
    const iterator = stream[Symbol.asyncIterator]();
    return async () => readChunk(await iterator.next());
  }
  throw new TypeError("guest output stream is not readable");
}

function readChunk(value) {
  if (value !== null && typeof value === "object" && typeof value.done === "boolean") {
    return value.done ? null : bytesOf(value.value);
  }
  return value === null || value === undefined ? null : bytesOf(value);
}

function bytesOf(value) {
  if (value instanceof Uint8Array) return new Uint8Array(value);
  if (Array.isArray(value)) return Uint8Array.from(value);
  if (typeof value === "number") return Uint8Array.of(value);
  throw new TypeError("guest output chunk is not bytes");
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

const sourceKeys = ["read"];

// A host's standard input for one execution, or null for none (an empty standard input). Its
// read action returns an async iterator of byte chunks; the host calls it once, when the guest
// first reads standard input, so a guest that never reads leaves the host's input untouched.
export function validateInputSource(value, label = "input source") {
  if (value === null) return null;
  if (typeof value !== "object" || Array.isArray(value) || Object.getPrototypeOf(value) !== Object.prototype
      || Object.getOwnPropertySymbols(value).length !== 0
      || Object.keys(value).sort().join() !== sourceKeys.join()
      || !Object.isFrozen(value) || typeof value.read !== "function") {
    throw new TypeError(`${label} must be null or an immutable reader`);
  }
  return value;
}

// Reads a source's chunks as a guest's stream<u8> (wasi:cli/stdin read-via-stream) and settles
// the stream's future when it ends: ok at the end of input or when the guest stops reading,
// or io when the source fails. stopped says the execution ended, and the read stops.
export async function* readInputSource(iterator, settle, stopped) {
  let result = Object.freeze({ tag: "ok", val: undefined });
  try {
    while (!stopped()) {
      const next = await iterator.next();
      if (next.done) return;
      yield inputBytes(next.value);
    }
  } catch {
    result = Object.freeze({ tag: "err", val: "io" });
  } finally {
    settle(result);
  }
}

function inputBytes(value) {
  if (value instanceof Uint8Array) return new Uint8Array(value);
  if (typeof value === "string") return new TextEncoder().encode(value);
  throw new TypeError("standard input chunk is not bytes");
}

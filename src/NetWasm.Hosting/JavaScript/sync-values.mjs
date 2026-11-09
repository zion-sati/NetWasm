// Values a synchronous WASI provider hands its host. A raw host, which cannot suspend the guest,
// reads them at once, so the guest never waits; a jco host treats them as the promise and async
// iterable it expects. A sync future computes its value on first use, which is after the stream
// it reports on has been read to its end; a computation that cannot finish yet returns a promise,
// which the raw host then waits for like any other.

const futureBrand = Symbol.for("netwasm.sync-future");

export function createSyncFuture(compute) {
  if (typeof compute !== "function") throw new TypeError("a sync future needs a computation");
  let settled = false;
  let value;
  const get = () => {
    if (!settled) {
      value = compute();
      settled = true;
    }
    return value;
  };
  return Object.freeze({
    [futureBrand]: get,
    then(onFulfilled, onRejected) {
      return new Promise(resolve => resolve(get())).then(onFulfilled, onRejected);
    },
  });
}

// The synchronous reader of a sync future, or null for any other value.
export function readSyncFuture(value) {
  return value !== null && typeof value === "object" && typeof value[futureBrand] === "function"
    ? value[futureBrand]
    : null;
}

// A stream source a raw host pulls synchronously and jco iterates asynchronously.
export function createSyncStream(iterator) {
  return Object.freeze({
    [Symbol.iterator]: () => iterator,
    [Symbol.asyncIterator]: () => Object.freeze({
      next: async () => iterator.next(),
      return: async value => iterator.return(value),
    }),
  });
}

// The key under which a raw host gives a stream the guest writes its synchronous drain: the
// drain returns the next chunk the host has buffered, null once the guest has dropped the stream
// and nothing is left, or undefined while the guest may still write.
export const syncDrainKey = Symbol.for("netwasm.sync-drain");

// The synchronous drain of a guest stream, or null when its host cannot drain it at once.
export function readSyncDrain(stream) {
  return stream !== null && typeof stream === "object" && typeof stream[syncDrainKey] === "function"
    ? stream[syncDrainKey]
    : null;
}

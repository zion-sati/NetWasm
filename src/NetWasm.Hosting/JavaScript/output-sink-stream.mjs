import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const requestKeys = ["outputStreamType", "pollableType", "sink"];
const sinkKeys = ["write"];
const writePermit = 1_000_000n;
const blockingWriteLimit = 4096;
const streamStates = new WeakMap();
const pollableStates = new WeakSet();

export function createOutputSinkStream(request) {
  assertExactDataObject(request, requestKeys, "output-sink stream request");
  assertResourceType(request.outputStreamType, "output-stream");
  assertResourceType(request.pollableType, "pollable");
  validateOutputSink(request.sink);

  class OutputSinkStream extends request.outputStreamType {
    checkWrite() {
      const state = readOpenStream(this);
      state.permit = writePermit;
      return state.permit;
    }

    write(bytes) {
      const state = readOpenStream(this);
      validateBytes(bytes);
      if (BigInt(bytes.byteLength) > state.permit) {
        throw new Error("output write exceeds the permit returned by checkWrite");
      }
      state.permit -= BigInt(bytes.byteLength);
      state.sink.write(new Uint8Array(bytes));
    }

    blockingWriteAndFlush(bytes) {
      const state = readOpenStream(this);
      validateBytes(bytes);
      if (bytes.byteLength > blockingWriteLimit) {
        throw new RangeError("blocking output writes accept at most 4096 bytes");
      }
      state.sink.write(new Uint8Array(bytes));
    }

    flush() {
      const state = readOpenStream(this);
      state.permit = 0n;
    }

    blockingFlush() {
      readOpenStream(this);
    }

    writeZeroes(length) {
      const state = readOpenStream(this);
      const count = validateLength(length);
      if (length > state.permit) {
        throw new Error("zero write exceeds the permit returned by checkWrite");
      }
      this.write(new Uint8Array(count));
    }

    blockingWriteZeroesAndFlush(length) {
      readOpenStream(this);
      const count = validateLength(length);
      if (count > blockingWriteLimit) {
        throw new RangeError("blocking zero writes accept at most 4096 bytes");
      }
      this.blockingWriteAndFlush(new Uint8Array(count));
    }

    splice(source, length) {
      readOpenStream(this);
      const count = Math.min(validateLength(length), Number(this.checkWrite()));
      const bytes = readInput(source, "read", BigInt(count));
      this.write(bytes);
      return BigInt(bytes.byteLength);
    }

    blockingSplice(source, length) {
      readOpenStream(this);
      const count = Math.min(validateLength(length), Number(this.checkWrite()));
      const bytes = readInput(source, "blockingRead", BigInt(count));
      this.write(bytes);
      return BigInt(bytes.byteLength);
    }

    subscribe() {
      readOpenStream(this);
      return createReadyPollable(request.pollableType);
    }

    [resourceDisposeSymbol]() {
      const state = streamStates.get(this);
      if (state === undefined || !state.open) return;
      state.open = false;
      state.permit = 0n;
    }
  }

  const stream = new OutputSinkStream();
  streamStates.set(stream, { open: true, permit: 0n, sink: request.sink });
  return stream;
}

export function validateOutputSink(value, label = "output sink") {
  assertExactDataObject(value, sinkKeys, label);
  if (!Object.isFrozen(value) || typeof value.write !== "function") {
    throw new TypeError(`${label} must be an immutable synchronous writer`);
  }
  return value;
}

function createReadyPollable(Pollable) {
  class ReadyPollable extends Pollable {
    ready() {
      readPollable(this);
      return true;
    }

    block() {
      readPollable(this);
    }

    [resourceDisposeSymbol]() {
      pollableStates.delete(this);
    }
  }
  const pollable = new ReadyPollable();
  pollableStates.add(pollable);
  return pollable;
}

function readOpenStream(stream) {
  const state = streamStates.get(stream);
  if (state === undefined || !state.open) throw Object.freeze({ tag: "closed" });
  return state;
}

function readPollable(pollable) {
  if (!pollableStates.has(pollable)) {
    throw new TypeError("output-sink pollable is disposed");
  }
}

function readInput(source, method, length) {
  if (source === null || typeof source !== "object" || typeof source[method] !== "function") {
    throw new TypeError("output splice source is invalid");
  }
  const bytes = source[method](length);
  validateBytes(bytes);
  return bytes;
}

function validateBytes(bytes) {
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("output bytes must be a Uint8Array");
  }
}

function validateLength(length) {
  if (typeof length !== "bigint" || length < 0n || length > BigInt(Number.MAX_SAFE_INTEGER)) {
    throw new TypeError("output length must be a safely representable u64");
  }
  return Number(length);
}

function assertResourceType(value, label) {
  if (typeof value !== "function" || value.prototype === undefined) {
    throw new TypeError(`${label} resource type is invalid`);
  }
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

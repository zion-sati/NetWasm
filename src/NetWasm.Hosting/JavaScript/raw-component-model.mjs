// The Component Model built-ins a raw core module imports, for an instance whose every
// stream, future and subtask has the JavaScript host at its other end. One table holds the
// instance's waitable sets, subtasks and stream and future ends. An async-lowered host call
// that settles later is a subtask; a copy the host cannot complete yet blocks and later
// raises an event on the waitable set its end joined. The host drives a callback-lifted
// export by passing it the events of the set it waits on. Nothing here blocks: a guest
// that waits synchronously for an event that has not happened yet fails, since a raw host
// runs without JavaScript Promise Integration.

import { readSyncFuture, syncDrainKey } from "./sync-values.mjs";

const subtaskStarted = 1;
const subtaskReturned = 2;
const cancelledBeforeReturned = 4;
const eventNone = 0;
const eventSubtask = 1;
const eventStreamRead = 2;
const eventFutureRead = 4;
const copyCompleted = 0;
const copyDropped = 1;
const copyCancelled = 2;
const blocked = 0xffffffff;
const callbackExit = 0;
const callbackYield = 1;
const callbackWait = 2;
const callbackPoll = 3;
const codecKeys = ["align", "bytes", "lift", "lower", "size"];

export function createRawComponentModel(options) {
  if (options === null || typeof options !== "object" || typeof options.memory !== "function") {
    throw new TypeError("raw Component Model memory accessor is required");
  }
  const table = new Map();
  let nextHandle = 1;
  let failure = null;
  let closed = false;
  let woken = false;
  const waiters = new Set();
  const contexts = [0, 0];

  const memory = () => options.memory();
  const add = entry => {
    const handle = nextHandle++;
    entry.handle = handle;
    table.set(handle, entry);
    return handle;
  };
  const get = (handle, kinds) => {
    const entry = table.get(Number(handle));
    if (entry === undefined || !kinds.includes(entry.kind)) {
      throw new WebAssembly.RuntimeError(`invalid Component Model handle ${handle}`);
    }
    return entry;
  };
  const remove = (handle, kinds) => {
    const entry = get(handle, kinds);
    if (entry.set !== null) leave(entry);
    table.delete(entry.handle);
    return entry;
  };
  const fail = error => {
    if (failure === null) failure = error instanceof Error ? error : new Error(String(error));
    notify();
  };
  const notify = () => {
    for (const waiter of [...waiters]) waiter();
  };
  const leave = waitable => {
    const set = table.get(waitable.set);
    set?.members.delete(waitable);
    set?.ready.delete(waitable);
    waitable.set = null;
  };
  // A waitable holds at most one pending event; the set it joined reports it. Each set keeps
  // its members with an event apart, so taking one costs the same however many wait.
  const raise = (waitable, code, payload) => {
    waitable.event = { code, index: waitable.handle, payload };
    if (waitable.set !== null) table.get(waitable.set).ready.add(waitable);
    notify();
  };
  const takeEvent = setHandle => {
    const set = get(setHandle, ["waitable-set"]);
    const waitable = set.ready.values().next().value;
    if (waitable === undefined) return null;
    set.ready.delete(waitable);
    const event = waitable.event;
    waitable.event = null;
    return event;
  };
  const writeEvent = (address, event) => {
    const view = new DataView(memory().buffer);
    view.setUint32(Number(address), event.index, true);
    view.setUint32(Number(address) + 4, event.payload, true);
    return event.code;
  };
  const throwIfFailed = () => {
    if (failure !== null) throw failure;
    if (closed) throw new Error("the raw Component Model instance is closed");
  };

  const root = Object.freeze({
    "[waitable-set-new]": () => add({ kind: "waitable-set", members: new Set(), ready: new Set(), set: null }),
    // A raw host cannot suspend the guest, so a wait for an operation that has not completed
    // reports no event (which a blocking host never does), and the guest's synchronous API
    // throws PlatformNotSupportedException instead of the host trapping.
    "[waitable-set-wait]"(setHandle, address) {
      return writeEvent(address, takeEvent(setHandle) ?? { code: eventNone, index: 0, payload: 0 });
    },
    "[waitable-set-poll]"(setHandle, address) {
      const event = takeEvent(setHandle);
      return event === null ? (writeEvent(address, { code: eventNone, index: 0, payload: 0 }), eventNone)
        : writeEvent(address, event);
    },
    "[waitable-set-drop]"(setHandle) {
      const set = get(setHandle, ["waitable-set"]);
      if (set.members.size !== 0) throw new WebAssembly.RuntimeError("waitable set still has members");
      table.delete(set.handle);
    },
    "[waitable-join]"(waitableHandle, setHandle) {
      const waitable = get(waitableHandle, [
        "subtask", "stream-readable", "stream-writable", "future-readable", "future-writable",
      ]);
      if (waitable.set !== null) leave(waitable);
      if (Number(setHandle) === 0) return;
      const set = get(setHandle, ["waitable-set"]);
      set.members.add(waitable);
      waitable.set = set.handle;
      if (waitable.event !== null) {
        set.ready.add(waitable);
        notify();
      }
    },
    "[subtask-drop]"(handle) {
      const subtask = get(handle, ["subtask"]);
      if (subtask.state === subtaskStarted) {
        throw new WebAssembly.RuntimeError("a subtask cannot be dropped before it returns");
      }
      remove(handle, ["subtask"]);
    },
    "[subtask-cancel]"(handle) {
      const subtask = get(handle, ["subtask"]);
      if (subtask.state === subtaskReturned) return subtaskReturned;
      subtask.state = cancelledBeforeReturned;
      subtask.event = null;
      return cancelledBeforeReturned;
    },
    // A raw host cannot suspend the guest, so a yield returns at once, not cancelled (0), as it
    // does where the task cannot block.
    "[thread-yield]": () => 0,
    "[backpressure-inc]"() {},
    "[backpressure-dec]"() {},
    "[context-get-0]": () => contexts[0],
    "[context-set-0]"(value) { contexts[0] = value; },
  });

  // An async-lowered import: its result reaches the guest at once, or later through the
  // subtask's RETURNED event. A rejected host call fails the instance, as a trap would.
  function callAsync(start, deliver) {
    throwIfFailed();
    const value = start();
    if (value === null || typeof value !== "object" || typeof value.then !== "function") {
      deliver(value);
      return subtaskReturned;
    }
    const subtask = { kind: "subtask", state: subtaskStarted, set: null, event: null };
    const handle = add(subtask);
    value.then(result => {
      if (subtask.state !== subtaskStarted || closed) return;
      try {
        deliver(result);
      } catch (error) {
        fail(error);
        return;
      }
      subtask.state = subtaskReturned;
      raise(subtask, eventSubtask, subtaskReturned);
    }, fail);
    return (handle << 4) | subtaskStarted;
  }

  function newPair(kind, codec) {
    const shared = { kind, codec: readCodec(codec), buffer: chunkQueue(), ended: false, readerGone: false };
    const readable = add({ kind: `${kind}-readable`, shared, set: null, event: null, pending: null });
    const writable = add({ kind: `${kind}-writable`, shared, set: null, event: null });
    shared.readable = readable;
    shared.writable = writable;
    return (BigInt(writable) << 32n) | BigInt(readable);
  }

  const stream = Object.freeze({
    new: codec => newPair("stream", codec),
    // The guest writes to a stream the host reads: every write completes at once, since the
    // host buffers what it has not read yet.
    write(codec, handle, address, count) {
      const writable = get(handle, ["stream-writable"]);
      const { shared } = writable;
      if (shared.readerGone) return copyDropped;
      const length = Number(count);
      enqueue(shared.buffer, readElements(memory(), shared.codec, address, length));
      shared.wake?.();
      return (length << 4) | copyCompleted;
    },
    read(codec, handle, address, count) {
      const readable = get(handle, ["stream-readable"]);
      if (readable.pending !== null) throw new WebAssembly.RuntimeError("a stream read is already pending");
      const copied = copyBuffered(readable, address, Number(count));
      if (copied !== null) return copied;
      readable.pending = { address, count: Number(count) };
      pump(readable);
      return blocked;
    },
    cancelRead(handle) {
      const readable = get(handle, ["stream-readable"]);
      if (readable.pending === null) throw new WebAssembly.RuntimeError("no stream read is pending");
      readable.pending = null;
      return copyCancelled;
    },
    cancelWrite(handle) {
      get(handle, ["stream-writable"]);
      throw new WebAssembly.RuntimeError("no stream write is pending");
    },
    dropReadable(handle) {
      const { shared } = remove(handle, ["stream-readable"]);
      shared.readerGone = true;
      shared.source?.return?.();
    },
    dropWritable(handle) {
      const { shared } = remove(handle, ["stream-writable"]);
      shared.ended = true;
      shared.wake?.();
    },
  });

  const future = Object.freeze({
    new: codec => newPair("future", codec),
    write(codec, handle, address) {
      const writable = get(handle, ["future-writable"]);
      const { shared } = writable;
      if (shared.readerGone) return copyDropped;
      shared.value = readElements(memory(), shared.codec, address, 1)[0];
      shared.written = true;
      shared.settle?.(shared.value);
      return copyCompleted;
    },
    read(codec, handle, address) {
      const readable = get(handle, ["future-readable"]);
      const { shared } = readable;
      if (!shared.settled && shared.compute !== undefined) settleComputed(readable);
      if (shared.settled) return lowerFutureValue(readable, address);
      readable.pending = { address };
      return blocked;
    },
    cancelRead(handle) {
      const readable = get(handle, ["future-readable"]);
      readable.pending = null;
      return copyCancelled;
    },
    cancelWrite(handle) {
      get(handle, ["future-writable"]);
      throw new WebAssembly.RuntimeError("no future write is pending");
    },
    dropReadable(handle) {
      remove(handle, ["future-readable"]).shared.readerGone = true;
    },
    dropWritable(handle) {
      const writable = remove(handle, ["future-writable"]);
      if (!writable.shared.written) throw new WebAssembly.RuntimeError("a future was dropped before it was written");
    },
  });

  // Copies what the host already supplied, and what a synchronous source supplies now, into
  // a pending or new read; null when nothing is there yet and the host's source is still open.
  function copyBuffered(readable, address, count) {
    const { shared } = readable;
    let copied = 0;
    for (;;) {
      if (queued(shared.buffer) === 0 && !shared.ended && copied < count) pullSynchronously(shared);
      if (copied === count || queued(shared.buffer) === 0) break;
      const chunk = shared.buffer.items[shared.buffer.head];
      const take = Math.min(count - copied, chunk.length);
      writeElements(memory(), shared.codec, address, copied, part(chunk, 0, take));
      copied += take;
      // A partly read chunk keeps its rest as a view, so reading a large chunk in small pieces
      // copies each byte once.
      if (take === chunk.length) dequeue(shared.buffer);
      else shared.buffer.items[shared.buffer.head] = part(chunk, take, chunk.length);
    }
    if (copied === 0) return shared.ended ? copyDropped : null;
    return (copied << 4) | copyCompleted;
  }

  function pump(readable) {
    const { shared } = readable;
    if (shared.pulling || shared.next === undefined) return;
    shared.pulling = true;
    Promise.resolve().then(() => shared.next()).then(result => {
      shared.pulling = false;
      accept(shared, result);
      settleRead(readable);
      if (readable.pending !== null && !shared.ended) pump(readable);
    }, error => {
      shared.pulling = false;
      shared.ended = true;
      shared.error = error;
      settleRead(readable);
    });
  }

  function settleRead(readable) {
    if (readable.pending === null || !table.has(readable.handle)) return;
    const payload = copyBuffered(readable, readable.pending.address, readable.pending.count);
    if (payload === null) return;
    readable.pending = null;
    raise(readable, eventStreamRead, payload);
  }

  function lowerFutureValue(readable, address) {
    readable.shared.codec.lower(memory(), Number(address), readable.shared.value);
    return copyCompleted;
  }

  // The host reads a stream the guest writes, such as standard output.
  function liftStream(handle) {
    const readable = remove(handle, ["stream-readable"]);
    const { shared } = readable;
    const next = () => new Promise(resolve => {
      const attempt = () => {
        if (queued(shared.buffer) !== 0) {
          shared.wake = undefined;
          resolve({ done: false, value: dequeue(shared.buffer) });
        } else if (shared.ended || shared.readerGone || closed) {
          shared.wake = undefined;
          resolve({ done: true, value: undefined });
        } else {
          shared.wake = attempt;
        }
      };
      attempt();
    });
    const release = () => {
      shared.readerGone = true;
      shared.buffer = chunkQueue();
      shared.wake?.();
    };
    // A synchronous provider takes what the guest has written so far at once.
    const drain = () => queued(shared.buffer) !== 0
      ? dequeue(shared.buffer)
      : shared.ended || shared.readerGone || closed ? null : undefined;
    return Object.freeze({
      read: next,
      next,
      async return() { release(); return { done: true, value: undefined }; },
      [Symbol.asyncIterator]() { return this; },
      [Symbol.dispose]: release,
      [syncDrainKey]: drain,
    });
  }

  // The guest reads a stream the host writes, such as a file's contents.
  function lowerStream(source, codec) {
    const packed = newPair("stream", codec);
    const writable = Number(packed >> 32n);
    const readable = Number(packed & 0xffffffffn);
    const { shared } = table.get(readable);
    table.delete(writable);
    const iterator = readSource(source);
    shared.source = iterator;
    shared.next = iterator.next.bind(iterator);
    shared.sync = iterator.sync === true;
    return readable;
  }

    function pullSynchronously(shared) {
    if (shared.sync) accept(shared, shared.next());
  }

  // The host awaits a future the guest writes.
  function liftFuture(handle) {
    const readable = remove(handle, ["future-readable"]);
    const { shared } = readable;
    return new Promise(resolve => {
      if (shared.written) resolve(shared.value);
      else shared.settle = resolve;
    });
  }

  // The guest reads a future the host settles, such as a write's completion. A synchronous
  // provider's future is computed when the guest first reads it, so the guest need not wait.
  function lowerFuture(promise, codec) {
    const packed = newPair("future", codec);
    const writable = Number(packed >> 32n);
    const readable = table.get(Number(packed & 0xffffffffn));
    table.delete(writable);
    const compute = readSyncFuture(promise);
    if (compute !== null) {
      readable.shared.compute = compute;
      return readable.handle;
    }
    settleLater(readable, promise);
    return readable.handle;
  }

  function settleComputed(readable) {
    const value = readable.shared.compute();
    readable.shared.compute = undefined;
    if (value !== null && typeof value === "object" && typeof value.then === "function") {
      settleLater(readable, value);
    } else {
      readable.shared.settled = true;
      readable.shared.value = value;
    }
  }

  function settleLater(readable, promise) {
    const { shared } = readable;
    Promise.resolve(promise).then(value => {
      shared.settled = true;
      shared.value = value;
      if (readable.pending === null || !table.has(readable.handle) || closed) return;
      try {
        lowerFutureValue(readable, readable.pending.address);
      } catch (error) {
        fail(error);
        return;
      }
      readable.pending = null;
      raise(readable, eventFutureRead, copyCompleted);
    }, fail);
  }

  // Runs a callback-lifted export to completion: each WAIT hands the callback the next
  // event of the set the export waits on.
  async function drive(code, callback) {
    for (;;) {
      throwIfFailed();
      const kind = code & 0xf;
      const set = code >>> 4;
      if (kind === callbackExit) return;
      let event;
      if (kind === callbackWait) event = await nextEvent(set);
      else if (kind === callbackPoll) event = takeEvent(set) ?? { code: eventNone, index: 0, payload: 0 };
      else if (kind === callbackYield) {
        await new Promise(resolve => setTimeout(resolve, 0));
        event = { code: eventNone, index: 0, payload: 0 };
      } else {
        throw new WebAssembly.RuntimeError(`invalid callback code ${code}`);
      }
      code = callback(event.code, event.index, event.payload);
    }
  }

  function nextEvent(set) {
    return new Promise((resolve, reject) => {
      const attempt = () => {
        if (failure !== null || closed) {
          waiters.delete(attempt);
          reject(failure ?? new Error("the raw Component Model instance is closed"));
          return;
        }
        const event = takeEvent(set) ?? (woken ? { code: eventNone, index: 0, payload: 0 } : null);
        if (event !== null) {
          woken = false;
          waiters.delete(attempt);
          resolve(event);
        }
      };
      waiters.add(attempt);
      attempt();
    });
  }

  return Object.freeze({
    root,
    callAsync,
    stream,
    future,
    liftStream,
    lowerStream,
    liftFuture,
    lowerFuture,
    drive,
    // Gives a waiting callback a turn without an event, such as after the host ran guest
    // code that queued work.
    wake() {
      woken = true;
      notify();
    },
    // Ends what the guest left open: host reads of its streams finish and waits reject.
    close() {
      if (closed) return;
      closed = true;
      for (const entry of table.values()) {
        if (entry.shared?.source) entry.shared.source.return?.();
        entry.shared?.wake?.();
      }
      notify();
      table.clear();
    },
  });
}

function readCodec(codec) {
  if (codec === null || typeof codec !== "object"
      || Object.keys(codec).sort().join() !== codecKeys.join()
      || !Number.isInteger(codec.size) || codec.size < 0
      || !Number.isInteger(codec.align) || codec.align < 1
      || typeof codec.bytes !== "boolean"
      || typeof codec.lift !== "function" || typeof codec.lower !== "function") {
    throw new TypeError("raw Component Model element codec is invalid");
  }
  return codec;
}

// A stream's chunks in order. Taking one advances a head index instead of shifting the array,
// which costs time in proportion to the chunks behind it once there are many.
function chunkQueue() {
  return { items: [], head: 0 };
}

function queued(queue) {
  return queue.items.length - queue.head;
}

function enqueue(queue, chunk) {
  queue.items.push(chunk);
}

function dequeue(queue) {
  const chunk = queue.items[queue.head];
  queue.items[queue.head++] = undefined;
  if (queue.head === queue.items.length) {
    queue.items = [];
    queue.head = 0;
  } else if (queue.head >= 1024 && queue.head * 2 >= queue.items.length) {
    queue.items = queue.items.slice(queue.head);
    queue.head = 0;
  }
  return chunk;
}

// Bytes are viewed, not copied; other elements are few per chunk.
function part(chunk, start, end) {
  return chunk instanceof Uint8Array ? chunk.subarray(start, end) : chunk.slice(start, end);
}

function readElements(memory, codec, address, count) {
  const start = Number(address);
  if (codec.bytes) return new Uint8Array(memory.buffer, start, count).slice();
  const values = [];
  for (let index = 0; index < count; index++) values.push(codec.lift(memory, start + index * codec.size));
  return values;
}

function writeElements(memory, codec, address, offset, values) {
  const start = Number(address) + offset * codec.size;
  if (codec.bytes) {
    new Uint8Array(memory.buffer, start, values.length).set(values);
    return;
  }
  values.forEach((value, index) => codec.lower(memory, start + index * codec.size, value));
}

function accept(shared, result) {
  if (result === null || typeof result !== "object" || result.done) {
    shared.ended = true;
    return;
  }
  const value = result.value;
  // A byte stream's source yields chunks of bytes; any other source yields one element at a
  // time, since an element may itself be an array.
  if (shared.codec.bytes) {
    const bytes = value instanceof Uint8Array ? value : Uint8Array.from(value);
    if (bytes.length !== 0) enqueue(shared.buffer, bytes);
  } else {
    enqueue(shared.buffer, [value]);
  }
}

// A host source is an array or other iterable (read synchronously), an async iterable,
// or a WHATWG ReadableStream.
function readSource(source) {
  if (source !== null && typeof source === "object" && typeof source.getReader === "function") {
    const reader = source.getReader();
    return { next: () => reader.read(), return: () => reader.cancel().catch(() => {}) };
  }
  // A source with both iterators is a synchronous provider's: the raw host pulls it at once.
  if (source !== null && typeof source === "object" && typeof source[Symbol.iterator] === "function") {
    const iterator = source[Symbol.iterator]();
    return { sync: true, next: () => iterator.next(), return: () => iterator.return?.() };
  }
  if (source !== null && typeof source === "object" && typeof source[Symbol.asyncIterator] === "function") {
    return source[Symbol.asyncIterator]();
  }
  throw new TypeError("raw Component Model stream source is not iterable");
}

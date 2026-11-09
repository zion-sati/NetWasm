// Test-only Adapter: deliver WASI 0.3 standard output and error to explicitly injected sinks.
// The guest makes a stream<u8>, hands its readable end to write-via-stream and writes to its
// writable end; every copy completes at once, and each stream's future reports success.
// Capabilities: getMemory(), writeStdout(bytes), writeStderr(bytes).
export function createOutputStreamImports(target, getMemory, writeStdout, writeStderr) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported output target');
  if (typeof getMemory !== 'function' || typeof writeStdout !== 'function' || typeof writeStderr !== 'function')
    throw new TypeError('Guest memory and output sinks are required');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const completed = 0;
  // A writable end's readable end, until write-via-stream gives the readable end a sink.
  const writables = new Map();
  const readables = new Map();
  const futures = new Set();
  let nextHandle = 1;
  const stream = () => ({
    '[stream-new-0]write-via-stream'() {
      const readable = nextHandle++;
      const writable = nextHandle++;
      writables.set(writable, readable);
      readables.set(readable, null);
      return (BigInt(writable) << 32n) | BigInt(readable);
    },
    '[async-lower][stream-write-0]write-via-stream'(writable, pointer, length) {
      const readable = writables.get(writable);
      if (readable === undefined) throw new TypeError('Unknown output stream');
      const target = readables.get(readable);
      if (target === null) throw new TypeError('Output stream has no destination');
      const bytes = new Uint8Array(getMemory().buffer, Number(pointer), Number(length)).slice();
      target(bytes);
      return bytes.length * 16 + completed;
    },
    '[stream-drop-writable-0]write-via-stream'(writable) {
      const readable = writables.get(writable);
      if (readable === undefined) throw new TypeError('Unknown output stream');
      writables.delete(writable);
      readables.delete(readable);
    },
    '[async-lower][future-read-1]write-via-stream'(future, result) {
      if (!futures.has(future)) throw new TypeError('Unknown output future');
      // The sink can grow guest memory; fetch the result view after delivery.
      new DataView(getMemory().buffer).setUint8(Number(result), 0);
      return completed;
    },
    '[future-drop-readable-1]write-via-stream'(future) {
      if (!futures.delete(future)) throw new TypeError('Unknown output future');
    },
  });
  const writeViaStream = sink => readable => {
    if (readables.get(readable) !== null) throw new TypeError('Unknown output stream');
    readables.set(readable, sink);
    const future = nextHandle++;
    futures.add(future);
    return future;
  };
  return {
    'wasi:cli/stdout@0.3.2': stream(),
    'wasi:cli/stderr@0.3.2': stream(),
    [`${prefix}|wasi:cli/stdout@0.3`]: { 'write-via-stream': writeViaStream(writeStdout) },
    [`${prefix}|wasi:cli/stderr@0.3`]: { 'write-via-stream': writeViaStream(writeStderr) },
  };
}

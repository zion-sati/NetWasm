// Test-only Adapter: deliver Preview 2 stream bytes to explicitly injected sinks.
// Capabilities: getMemory(), writeStdout(bytes), writeStderr(bytes).
export function createOutputStreamImports(target, getMemory, writeStdout, writeStderr) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported output target');
  if (typeof getMemory !== 'function' || typeof writeStdout !== 'function' || typeof writeStderr !== 'function')
    throw new TypeError('Guest memory and output sinks are required');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const streams = new Map();
  let nextHandle = 1;
  const open = sink => {
    const handle = nextHandle++;
    streams.set(handle, sink);
    return handle;
  };
  return {
    [`${prefix}|wasi:cli/stdout@0.2`]: { 'get-stdout': () => open(writeStdout) },
    [`${prefix}|wasi:cli/stderr@0.2`]: { 'get-stderr': () => open(writeStderr) },
    [`${prefix}|wasi:io/streams@0.2`]: {
      '[method]output-stream.blocking-write-and-flush'(handle, pointer, length, result) {
        const sink = streams.get(handle);
        if (sink === undefined) throw new TypeError('Unknown output stream');
        const bytes = new Uint8Array(getMemory().buffer, Number(pointer), Number(length)).slice();
        sink(bytes);
        // The sink can grow guest memory; fetch the result view after delivery.
        new DataView(getMemory().buffer).setUint8(Number(result), 0);
      },
      'output-stream_drop'(handle) {
        if (!streams.delete(handle)) throw new TypeError('Unknown output stream');
      },
    },
  };
}

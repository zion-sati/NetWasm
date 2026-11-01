// Factory for native contracts that select no environment or external I/O.
// With the guest's exports it also returns random bytes, in a list the guest
// allocates as a host's would be.
export function createRuntimeContractImports(target, getMemory, getExports) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported runtime target');
  if (typeof getMemory !== 'function')
    throw new TypeError('A runtime memory accessor is required');
  const width = target === 'wasm64' ? 8 : 4;
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const unexpected = () => { throw new Error('Unexpected runtime service call'); };
  const address = value => target === 'wasm64' ? BigInt(value) : Number(value);
  // Bytes keep counting across calls, as the simulated oracle's do, so code
  // that draws until a value fits a range gets a new one each time.
  let randomByte = 0;
  return {
    'netwasm.application.v1': {
      'netwasm.filter': unexpected,
      'netwasm.finalize': unexpected,
    },
    env: { emscripten_notify_memory_growth() {} },
    [`${prefix}|wasi:cli/environment@0.2`]: {
      'get-environment'(result) {
        new Uint8Array(getMemory().buffer, Number(result), width * 2).fill(0);
      },
    },
    [`${prefix}|wasi:random/random@0.2`]: {
      'get-random-u64': () => 0x0123456789abcdefn,
      'get-random-bytes'(length, result) {
        const reallocate = getExports?.().component_realloc;
        if (typeof reallocate !== 'function') unexpected();
        const count = Number(length);
        const bytes = Number(reallocate(address(0), address(0), address(1), address(count)));
        const memory = getMemory().buffer;
        const values = new Uint8Array(memory, bytes, count);
        for (let index = 0; index < count; index++) values[index] = (++randomByte) & 255;
        const view = new DataView(memory);
        if (target === 'wasm64') {
          view.setBigUint64(Number(result), BigInt(bytes), true);
          view.setBigUint64(Number(result) + 8, BigInt(count), true);
        } else {
          view.setUint32(Number(result), bytes, true);
          view.setUint32(Number(result) + 4, count, true);
        }
      },
    },
    [`${prefix}|wasi:cli/stdout@0.2`]: { 'get-stdout': unexpected },
    [`${prefix}|wasi:cli/stderr@0.2`]: { 'get-stderr': unexpected },
    [`${prefix}|wasi:io/streams@0.2`]: {
      '[method]output-stream.blocking-write-and-flush': unexpected,
      'output-stream_drop': unexpected,
    },
    [`${prefix}|wasi:io/error@0.2`]: { error_drop: unexpected },
    [`${prefix}|wasi:cli/exit@0.2`]: { exit: unexpected },
  };
}

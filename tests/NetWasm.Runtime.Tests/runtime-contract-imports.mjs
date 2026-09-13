// Factory for native contracts that select no environment or external I/O.
export function createRuntimeContractImports(target, getMemory) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported runtime target');
  if (typeof getMemory !== 'function')
    throw new TypeError('A runtime memory accessor is required');
  const width = target === 'wasm64' ? 8 : 4;
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const unexpected = () => { throw new Error('Unexpected runtime service call'); };
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

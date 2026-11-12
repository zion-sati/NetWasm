// Factory for native contracts that select no environment or external I/O.
// With the guest's exports it also returns random bytes, in a list the guest
// allocates as a host's would be. A contract never waits, so the Component
// Model built-ins only keep waitable sets and subtasks as bookkeeping.
export function createRuntimeContractImports(target, getMemory, getExports) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported runtime target');
  if (typeof getMemory !== 'function')
    throw new TypeError('A runtime memory accessor is required');
  const width = target === 'wasm64' ? 8 : 4;
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const unexpected = () => { throw new Error('Unexpected runtime service call'); };
  const address = value => target === 'wasm64' ? BigInt(value) : Number(value);
  // Opening a standard stream is output, which a contract never writes.
  const standardStream = {
    '[stream-new-0]write-via-stream': unexpected,
    '[async-lower][stream-write-0]write-via-stream': unexpected,
    '[stream-drop-writable-0]write-via-stream': unexpected,
    '[async-lower][future-read-1]write-via-stream': unexpected,
    '[future-drop-readable-1]write-via-stream': unexpected,
  };
  // Bytes keep counting across calls, as the simulated oracle's do, so code
  // that draws until a value fits a range gets a new one each time.
  let randomByte = 0;
  let nextWaitableSet = 0;
  return {
    'netwasm.application.v1': {
      'netwasm.filter': unexpected,
      'netwasm.finalize': unexpected,
    },
    env: { emscripten_notify_memory_growth() {} },
    $root: {
      '[waitable-set-new]': () => ++nextWaitableSet,
      '[waitable-join]'() {},
      '[waitable-set-wait]': unexpected,
      '[subtask-drop]'() {},
      '[subtask-cancel]': () => 4,
      // Console output yields to the host after a write it took at once (#224); a test host
      // runs no other task, so the yield returns at once, not cancelled.
      '[thread-yield]': () => 0,
    },
    [`${prefix}|wasi:cli/environment@0.3`]: {
      'get-environment'(result) {
        new Uint8Array(getMemory().buffer, Number(result), width * 2).fill(0);
      },
    },
    [`${prefix}|wasi:random/random@0.3`]: {
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
    [`${prefix}|wasi:cli/stdout@0.3`]: { 'write-via-stream': unexpected },
    [`${prefix}|wasi:cli/stderr@0.3`]: { 'write-via-stream': unexpected },
    'wasi:cli/stdout@0.3.2': standardStream,
    'wasi:cli/stderr@0.3.2': standardStream,
    [`${prefix}|wasi:cli/exit@0.3`]: { exit: unexpected, 'exit-with-code': unexpected },
  };
}

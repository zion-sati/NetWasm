// Test-only: the WASI 0.3 monotonic clock and its async-lowered wait-for. A wait
// starts a subtask that stays pending: only a callback-lifted export may wait, and
// a synchronous observation delivers no events, so its delayed work never runs.
export function createMonotonicClockImports(target, now) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported clock target');
  if (typeof now !== 'function')
    throw new TypeError('A monotonic clock is required');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const started = 1;
  // Subtask handles start apart from the waitable sets the contract imports count.
  let nextSubtask = 1000;
  return {
    [`${prefix}|wasi:clocks/monotonic-clock@0.3`]: { now: () => now() },
    'wasi:clocks/monotonic-clock@0.3.2': {
      '[async-lower]wait-for': () => (++nextSubtask) * 16 + started,
    },
  };
}

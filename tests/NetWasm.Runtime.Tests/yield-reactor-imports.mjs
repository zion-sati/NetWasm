// Test-only Adapter: translate zero-duration pollable readiness into queued wakes.
// Injected capabilities: enqueue(callback), wake(token). No timers or ambient state.
export function createYieldReactorImports(target, enqueue, wake) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported reactor target');
  if (typeof enqueue !== 'function' || typeof wake !== 'function')
    throw new TypeError('Reactor queue and wake capabilities are required');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const pollables = new Set();
  const pending = new Map();
  let nextPollable = 0;
  return {
    [`${prefix}|wasi:clocks/monotonic-clock@0.2`]: {
      'subscribe-duration'(duration) {
        if (duration !== 0n) throw new RangeError('Only yield readiness is supported');
        const pollable = ++nextPollable;
        pollables.add(pollable);
        return pollable;
      },
    },
    [`${prefix}|netwasm:runtime/reactor-host@1`]: {
      watch(pollable, token) {
        if (!pollables.has(pollable) || pending.has(token))
          throw new Error('Invalid reactor registration');
        pollables.delete(pollable);
        const registration = {};
        pending.set(token, registration);
        enqueue(() => {
          if (pending.get(token) === registration) {
            pending.delete(token);
            wake(token);
          }
        });
      },
      cancel(token) { pending.delete(token); },
    },
  };
}

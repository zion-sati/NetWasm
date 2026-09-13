// Test-only Adapter: translate monotonic deadlines into cancellable reactor wakes.
// now() returns bigint nanoseconds; schedule(milliseconds, callback) queues (never
// invokes inline) and returns a handle accepted by unschedule(handle).
export function createTimerReactorImports(target, now, schedule, unschedule, wake) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported reactor target');
  if (typeof now !== 'function' || typeof schedule !== 'function' ||
      typeof unschedule !== 'function' || typeof wake !== 'function')
    throw new TypeError('Clock, scheduling, cancellation and wake capabilities are required');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const pollables = new Map();
  const pending = new Map();
  let nextPollable = 0;
  const arm = (registration, token) => {
    const remaining = registration.deadline - now();
    const milliseconds = remaining <= 0n ? 0n : (remaining + 999999n) / 1000000n;
    // setTimeout delays above signed-int32 milliseconds are coerced to 1 ms.
    const delay = Number(milliseconds > 2147483647n ? 2147483647n : milliseconds);
    registration.handle = schedule(delay, () => {
      if (pending.get(token) !== registration) return;
      if (now() < registration.deadline) {
        arm(registration, token);
        return;
      }
      pending.delete(token);
      wake(token);
    });
  };
  return {
    [`${prefix}|wasi:clocks/monotonic-clock@0.2`]: {
      'subscribe-duration'(duration) {
        const deadline = now() + BigInt.asUintN(64, duration);
        const pollable = ++nextPollable;
        pollables.set(pollable, deadline);
        return pollable;
      },
    },
    [`${prefix}|netwasm:runtime/reactor-host@1`]: {
      watch(pollable, token) {
        if (!pollables.has(pollable) || pending.has(token))
          throw new Error('Invalid reactor registration');
        const registration = { deadline: pollables.get(pollable) };
        arm(registration, token);
        pollables.delete(pollable);
        pending.set(token, registration);
      },
      cancel(token) {
        const registration = pending.get(token);
        if (registration === undefined) return;
        pending.delete(token);
        unschedule(registration.handle);
      },
    },
  };
}

export function createReactorOracleImports(target = "wasm32", hooks = {}) {
  const canonicalPrefix = target === "wasm64" ? "cm64p2" : "cm32p2";
  const reactorHostModule = `${canonicalPrefix}|netwasm:runtime/reactor-host@1`;
  const monotonicClockModule = `${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`;
  return {
    [reactorHostModule]: {
      watch(pollable, token) { hooks.watch?.(pollable, token); },
      cancel(token) { hooks.cancel?.(token); },
    },
    [monotonicClockModule]: {
      now: () => process.hrtime.bigint(),
      "subscribe-duration": () => 1,
    },
  };
}

// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Test-only: the WASI 0.3 system clock at a fixed instant, the one the corpus oracle host reports, so a program that
// reads the time observes the same in every host: now writes seconds (u64) and nanoseconds (u32) to its result.
export function createSystemClockImports(target, getMemory) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported clock target');
  if (typeof getMemory !== 'function')
    throw new TypeError('The clock writes to the program memory');
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  return {
    [`${prefix}|wasi:clocks/system-clock@0.3`]: {
      now(result) {
        const view = new DataView(getMemory().buffer);
        const address = Number(result);
        view.setBigUint64(address, 0n, true);
        view.setUint32(address + 8, 123000000, true);
      },
    },
  };
}

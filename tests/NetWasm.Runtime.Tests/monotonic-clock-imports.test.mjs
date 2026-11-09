import assert from 'node:assert/strict';
import test from 'node:test';
import { createMonotonicClockImports } from './monotonic-clock-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  test(`The clock reads now and each wait starts a pending subtask: ${target}`, () => {
    let reads = 0;
    const imports = createMonotonicClockImports(target, () => 41n + BigInt(++reads));
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';

    assert.equal(imports[`${prefix}|wasi:clocks/monotonic-clock@0.3`].now(), 42n);
    const waitFor = imports['wasi:clocks/monotonic-clock@0.3.2']['[async-lower]wait-for'];
    const first = waitFor(25n);
    const second = waitFor(0n);
    assert.equal(first & 15, 1);
    assert.equal(second & 15, 1);
    assert.equal((second >> 4) - (first >> 4), 1);
    assert.ok(first >> 4 > 1000);
    assert.equal(reads, 1);
  });
}

test('The clock rejects an unsupported target or a missing clock', () => {
  assert.throws(() => createMonotonicClockImports('invalid', () => 0n), /Unsupported clock target/);
  assert.throws(() => createMonotonicClockImports('wasm32', null), /monotonic clock is required/);
});

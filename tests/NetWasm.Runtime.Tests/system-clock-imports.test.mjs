import assert from 'node:assert/strict';
import test from 'node:test';
import { createSystemClockImports } from './system-clock-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  test(`The clock writes its fixed instant to the result: ${target}`, () => {
    const memory = { buffer: new ArrayBuffer(64) };
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
    const imports = createSystemClockImports(target, () => memory);

    imports[`${prefix}|wasi:clocks/system-clock@0.3`].now(target === 'wasm64' ? 16n : 16);

    const view = new DataView(memory.buffer);
    assert.equal(view.getBigUint64(16, true), 0n);
    assert.equal(view.getUint32(24, true), 123000000);
  });
}

test('The clock rejects an unsupported target or a missing memory', () => {
  assert.throws(() => createSystemClockImports('invalid', () => ({})), /Unsupported clock target/);
  assert.throws(() => createSystemClockImports('wasm32', null), /writes to the program memory/);
});

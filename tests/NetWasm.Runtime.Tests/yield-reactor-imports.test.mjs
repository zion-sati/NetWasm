import assert from 'node:assert/strict';
import test from 'node:test';
import { createYieldReactorImports } from './yield-reactor-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  test(`${target} yield ownership, cancellation and token reuse`, () => {
    const queue = [];
    const wakes = [];
    const imports = createYieldReactorImports(target, callback => queue.push(callback),
      token => wakes.push(token));
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
    const clock = imports[`${prefix}|wasi:clocks/monotonic-clock@0.2`];
    const host = imports[`${prefix}|netwasm:runtime/reactor-host@1`];
    assert.equal(Object.keys(imports).length, 2);
    assert.throws(() => clock['subscribe-duration'](1n), RangeError);
    assert.throws(() => clock['subscribe-duration'](0), RangeError);
    const first = clock['subscribe-duration'](0n);
    const second = clock['subscribe-duration'](0n);
    assert.notEqual(first, second);
    assert.throws(() => host.watch(-1, 9), /Invalid reactor registration/);
    assert.equal(queue.length, 0);
    host.watch(first, 9);
    assert.throws(() => host.watch(first, 10), /Invalid reactor registration/);
    assert.throws(() => host.watch(second, 9), /Invalid reactor registration/);
    assert.equal(queue.length, 1);
    assert.deepEqual(wakes, []);
    host.cancel(9);
    host.cancel(9);
    host.watch(second, 9);
    queue[0]();
    assert.deepEqual(wakes, []);
    queue[1]();
    assert.deepEqual(wakes, [9]);
    queue[1]();
    assert.deepEqual(wakes, [9]);
    const third = clock['subscribe-duration'](0n);
    host.watch(third, 10);
    queue[2]();
    assert.deepEqual(wakes, [9, 10]);
  });
}

test('rejects missing capabilities and unsupported target', () => {
  assert.throws(() => createYieldReactorImports('other', () => {}, () => {}), TypeError);
  assert.throws(() => createYieldReactorImports('wasm32', null, () => {}), TypeError);
  assert.throws(() => createYieldReactorImports('wasm32', () => {}, null), TypeError);
});

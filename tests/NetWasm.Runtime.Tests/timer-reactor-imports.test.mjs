import assert from 'node:assert/strict';
import test from 'node:test';
import { createTimerReactorImports } from './timer-reactor-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  function fixture() {
    let time = 0n;
    const queued = [];
    const cancelled = [];
    const wakes = [];
    const imports = createTimerReactorImports(target, () => time,
      (delay, callback) => { const handle = { delay, callback }; queued.push(handle); return handle; },
      handle => cancelled.push(handle), token => wakes.push(token));
    return { imports, queued, cancelled, wakes, setTime: value => { time = value; },
      subscribe: imports[`${prefix}|wasi:clocks/monotonic-clock@0.2`]['subscribe-duration'],
      reactor: imports[`${prefix}|netwasm:runtime/reactor-host@1`] };
  }

  test(`${target}: zero duration queues one wake and permits token reuse`, () => {
    const f = fixture();
    assert.deepEqual(Object.keys(f.imports), [
      `${prefix}|wasi:clocks/monotonic-clock@0.2`, `${prefix}|netwasm:runtime/reactor-host@1`,
    ]);
    const first = f.subscribe(0n);
    f.reactor.watch(first, 7);
    assert.equal(f.queued[0].delay, 0);
    assert.deepEqual(f.wakes, []);
    f.queued[0].callback();
    assert.deepEqual(f.wakes, [7]);
    f.queued[0].callback();
    assert.deepEqual(f.wakes, [7]);
    const second = f.subscribe(0n);
    assert.notEqual(first, second);
    f.reactor.watch(second, 7);
    f.queued[0].callback();
    assert.deepEqual(f.wakes, [7]);
    f.queued[1].callback();
    assert.deepEqual(f.wakes, [7, 7]);
    f.reactor.cancel(7);
    assert.deepEqual(f.cancelled, []);
  });

  test(`${target}: deadline starts at subscription, rounds up and rejects early wake`, () => {
    const f = fixture();
    const pollable = f.subscribe(100000001n);
    f.setTime(50000000n);
    f.reactor.watch(pollable, 11);
    assert.equal(f.queued[0].delay, 51);
    f.setTime(100000000n);
    f.queued[0].callback();
    assert.deepEqual(f.wakes, []);
    assert.equal(f.queued[1].delay, 1);
    f.setTime(100000001n);
    f.queued[1].callback();
    assert.deepEqual(f.wakes, [11]);
    const expired = f.subscribe(1n);
    f.setTime(200000000n);
    f.reactor.watch(expired, 12);
    assert.equal(f.queued[2].delay, 0);
    f.queued[2].callback();
    assert.deepEqual(f.wakes, [11, 12]);
  });

  test(`${target}: long unsigned durations retain their deadline across timer chunks`, () => {
    const f = fixture();
    const pollable = f.subscribe(-1n);
    f.reactor.watch(pollable, 1);
    assert.equal(f.queued[0].delay, 2147483647);
    f.setTime(2147483647000000n);
    f.queued[0].callback();
    assert.equal(f.queued[1].delay, 2147483647);
    assert.deepEqual(f.wakes, []);
    f.setTime(18446744073709551615n);
    f.queued[1].callback();
    assert.deepEqual(f.wakes, [1]);
  });

  test(`${target}: cancellation protects replacement registrations and validates ownership`, () => {
    const f = fixture();
    const first = f.subscribe(100000000n);
    const second = f.subscribe(0n);
    assert.throws(() => f.reactor.watch(999, 7), /Invalid reactor registration/);
    assert.equal(f.queued.length, 0);
    f.reactor.watch(first, 7);
    assert.throws(() => f.reactor.watch(second, 7), /Invalid reactor registration/);
    assert.throws(() => f.reactor.watch(first, 8), /Invalid reactor registration/);
    assert.equal(f.queued.length, 1);
    f.reactor.cancel(7);
    assert.deepEqual(f.cancelled, [f.queued[0]]);
    f.reactor.cancel(7);
    f.reactor.cancel(999);
    assert.equal(f.cancelled.length, 1);
    f.reactor.watch(second, 7);
    f.setTime(100000000n);
    f.queued[0].callback();
    assert.deepEqual(f.wakes, []);
    f.queued[1].callback();
    assert.deepEqual(f.wakes, [7]);
  });

  test(`${target}: initial scheduling failure preserves the unconsumed pollable`, () => {
    const failure = new Error('schedule failed');
    let fail = true;
    let callback;
    const wakes = [];
    const imports = createTimerReactorImports(target, () => 0n, (_delay, invoke) => {
      if (fail) throw failure;
      callback = invoke;
      return 1;
    }, () => assert.fail('Nothing should be cancelled'), token => wakes.push(token));
    const pollable = imports[`${prefix}|wasi:clocks/monotonic-clock@0.2`]['subscribe-duration'](0n);
    const reactor = imports[`${prefix}|netwasm:runtime/reactor-host@1`];
    assert.throws(() => reactor.watch(pollable, 7), error => error === failure);
    reactor.cancel(7);
    fail = false;
    reactor.watch(pollable, 7);
    callback();
    assert.deepEqual(wakes, [7]);
  });
}

test('validates constructor capabilities without invoking them', () => {
  const unexpected = () => assert.fail('Construction must not invoke capabilities');
  assert.throws(() => createTimerReactorImports('wasm16', unexpected, unexpected, unexpected, unexpected), TypeError);
  for (let index = 0; index < 4; index++) {
    const callbacks = [unexpected, unexpected, unexpected, unexpected];
    callbacks[index] = null;
    assert.throws(() => createTimerReactorImports('wasm32', ...callbacks), TypeError);
  }
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { createGuestEnvironmentImports } from './guest-environment-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  const width = target === 'wasm64' ? 8 : 4;
  const address = value => width === 8 ? BigInt(value) : value;
  const namespace = `${width === 8 ? 'cm64p2' : 'cm32p2'}|wasi:cli/environment@0.2`;
  test(`${target}: selected pairs survive allocation-time memory growth and repeated reads`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const allocations = [];
    let next = 256;
    const allocate = (length, alignment) => {
      next = Math.ceil(next / alignment) * alignment;
      const pointer = next;
      next += length;
      memory.grow(1);
      allocations.push({ length, alignment, pointer });
      return address(pointer);
    };
    const environment = { TZ: 'Australia/Lord_Howe', '名': 'café=水', EMPTY: '' };
    const expected = Object.entries(environment);
    const imports = createGuestEnvironmentImports(target, () => memory, allocate, environment);
    assert.deepEqual(Object.keys(imports), [namespace]);
    assert.deepEqual(Object.keys(imports[namespace]), ['get-environment']);
    environment.TZ = 'UTC';
    environment.EXTRA = 'not selected';
    const readAddress = pointer => {
      const view = new DataView(memory.buffer);
      return width === 8 ? Number(view.getBigUint64(pointer, true)) : view.getUint32(pointer, true);
    };
    const decode = result => {
      const base = readAddress(result);
      const count = readAddress(result + width);
      return Array.from({ length: count }, (_, index) => [0, 1].map(field => {
        const slot = base + (index * 4 + field * 2) * width;
        const pointer = readAddress(slot);
        const length = readAddress(slot + width);
        if (length === 0) assert.equal(pointer, 0);
        return new TextDecoder().decode(new Uint8Array(memory.buffer, pointer, length));
      }));
    };
    imports[namespace]['get-environment'](address(32));
    const firstElements = readAddress(32);
    assert.deepEqual(decode(32), expected);
    assert.equal(allocations[0].length, expected.length * width * 4);
    assert.equal(allocations[0].alignment, width);
    assert.equal(allocations.length, 6);
    for (const item of allocations.slice(1)) assert.equal(item.alignment, 1);
    imports[namespace]['get-environment'](address(64));
    assert.notEqual(readAddress(64), firstElements);
    assert.deepEqual(decode(32), expected);
    assert.deepEqual(decode(64), expected);
    assert.equal(allocations.length, 12);
  });

  test(`${target}: empty selection clears the result without allocating`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    new Uint8Array(memory.buffer, 32, width * 2).fill(0xff);
    const imports = createGuestEnvironmentImports(target, () => memory,
      () => assert.fail('Empty selection must not allocate'), {});
    imports[namespace]['get-environment'](address(32));
    assert.deepEqual([...new Uint8Array(memory.buffer, 32, width * 2)], Array(width * 2).fill(0));
  });

  test(`${target}: allocator failures propagate before a result is published`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const failure = new Error('allocation failed');
    const imports = createGuestEnvironmentImports(target, () => memory, () => { throw failure; }, { KEY: 'value' });
    assert.throws(() => imports[namespace]['get-environment'](address(32)), error => error === failure);
    assert.deepEqual([...new Uint8Array(memory.buffer, 32, width * 2)], Array(width * 2).fill(0));
  });
}

test('rejects invalid setup before using guest capabilities', () => {
  const unexpected = () => assert.fail('Invalid setup must not invoke guest capabilities');
  for (const target of ['', 'wasm16', null])
    assert.throws(() => createGuestEnvironmentImports(target, unexpected, unexpected, {}), TypeError);
  assert.throws(() => createGuestEnvironmentImports('wasm32', null, unexpected, {}), TypeError);
  assert.throws(() => createGuestEnvironmentImports('wasm32', unexpected, null, {}), TypeError);
  for (const environment of [null, undefined, [], 1, 'value'])
    assert.throws(() => createGuestEnvironmentImports('wasm32', unexpected, unexpected, environment), TypeError);
  for (const value of [undefined, null, 1, false, {}])
    assert.throws(() => createGuestEnvironmentImports('wasm32', unexpected, unexpected, { KEY: value }), TypeError);
});

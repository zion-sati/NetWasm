import assert from 'node:assert/strict';
import test from 'node:test';
import { createReadOnlyAssetImports } from './read-only-asset-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  const width = target === 'wasm64' ? 8 : 4;
  const address = value => width === 8 ? BigInt(value) : value;
  const prefix = width === 8 ? 'cm64p2' : 'cm32p2';
  function fixture(bytes = new Uint8Array([10, 20, 30, 40])) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    let next = 1024;
    const allocations = [];
    const imports = createReadOnlyAssetImports(target, () => memory, (length, alignment) => {
      next = Math.ceil(next / alignment) * alignment;
      const pointer = next;
      next += length;
      memory.grow(1);
      allocations.push({ length, alignment });
      return address(pointer);
    }, { mountPath: '/資産', fileName: '資料.bin', bytes });
    const preopens = imports[`${prefix}|wasi:filesystem/preopens@0.2`];
    const files = imports[`${prefix}|wasi:filesystem/types@0.2`];
    const view = () => new DataView(memory.buffer);
    const pointer = slot => width === 8 ? Number(view().getBigUint64(slot, true)) : view().getUint32(slot, true);
    const text = slot => new TextDecoder().decode(new Uint8Array(memory.buffer, pointer(slot), pointer(slot + width)));
    const directories = () => {
      preopens['get-directories'](address(32));
      assert.equal(pointer(32 + width), 1);
      const base = pointer(32);
      assert.equal(text(base + width), '/資産');
      return view().getInt32(base, true);
    };
    const open = (directory, name = '資料.bin', pathFlags = 0, openFlags = 0, flags = 1) => {
      const encoded = new TextEncoder().encode(name);
      new Uint8Array(memory.buffer, 256, encoded.length).set(encoded);
      files.descriptor_open_at(directory, pathFlags, address(256), address(encoded.length), openFlags, flags, address(64));
      return view().getUint8(64) === 0 ? view().getInt32(68, true) : -view().getUint8(68);
    };
    const read = (handle, length, offset) => {
      files.descriptor_read(handle, length, offset, address(96));
      if (view().getUint8(96) !== 0) return { error: view().getUint8(96 + width) };
      return {
        bytes: [...new Uint8Array(memory.buffer, pointer(96 + width), pointer(96 + width * 2))],
        eof: view().getUint8(96 + width * 3) === 1,
      };
    };
    return { memory, allocations, imports, files, directories, open, read, pointer };
  }

  test(`${target}: real immutable bytes, independent handles and memory growth`, () => {
    const bytes = new Uint8Array([10, 20, 30, 40]);
    const f = fixture(bytes);
    bytes.fill(99);
    assert.deepEqual(Object.keys(f.imports), [
      `${prefix}|wasi:filesystem/preopens@0.2`, `${prefix}|wasi:filesystem/types@0.2`,
    ]);
    assert.equal(f.files.descriptor_open_at, f.files['[method]descriptor.open-at']);
    assert.equal(f.files.descriptor_read, f.files['[method]descriptor.read']);
    assert.equal(f.files.descriptor_drop, f.files['[resource-drop]descriptor']);
    const firstDirectory = f.directories();
    const secondDirectory = f.directories();
    assert.notEqual(firstDirectory, secondDirectory);
    assert.deepEqual(f.allocations.slice(0, 2), [
      { length: new TextEncoder().encode('/資産').length, alignment: 1 },
      { length: width * 3, alignment: width },
    ]);
    const first = f.open(firstDirectory);
    const second = f.open(secondDirectory, '資料.bin', 1);
    assert.ok(first > 0);
    assert.ok(second > 0);
    assert.notEqual(first, second);
    assert.deepEqual(f.read(first, 2n, 1n), { bytes: [20, 30], eof: false });
    assert.deepEqual(f.read(first, 0n, 0n), { bytes: [], eof: false });
    assert.equal(f.pointer(96 + width), 0);
    assert.deepEqual(f.read(second, 100n, 2n), { bytes: [30, 40], eof: true });
    assert.deepEqual(f.read(first, -1n, 0n), { bytes: [10, 20, 30, 40], eof: true });
    assert.deepEqual(f.read(first, 1n, 4n), { bytes: [], eof: true });
    assert.deepEqual(f.read(first, 1n, -1n), { bytes: [], eof: true });
    f.files.descriptor_drop(first);
    assert.deepEqual(f.read(first, 1n, 0n), { error: 3 });
    assert.deepEqual(f.read(second, 1n, 0n), { bytes: [10], eof: false });
    assert.throws(() => f.files.descriptor_drop(first), TypeError);
    f.files.descriptor_drop(firstDirectory);
    assert.equal(f.open(firstDirectory), -3);
    f.files.descriptor_drop(second);
    f.files.descriptor_drop(secondDirectory);
  });

  test(`${target}: empty file and exact Preview 2 errors`, () => {
    const f = fixture(new Uint8Array());
    const directory = f.directories();
    const file = f.open(directory);
    assert.deepEqual(f.read(file, 10n, 0n), { bytes: [], eof: true });
    assert.deepEqual(f.read(directory, 10n, 0n), { error: 14 });
    assert.deepEqual(f.read(999, 10n, 0n), { error: 3 });
    assert.equal(f.open(999), -3);
    assert.equal(f.open(file), -24);
    assert.equal(f.open(directory, 'absent'), -20);
    for (const name of ['/資料.bin', '../資料.bin', 'a/../資料.bin'])
      assert.equal(f.open(directory, name), -31);
    for (const openFlags of [1, 8]) assert.equal(f.open(directory, '資料.bin', 0, openFlags), -33);
    for (const flags of [2, 32]) assert.equal(f.open(directory, '資料.bin', 0, 0, flags), -33);
    assert.equal(f.open(directory, '資料.bin', 0, 2), -24);
    assert.equal(f.open(directory, '資料.bin', 2), -27);
    assert.equal(f.open(directory, '資料.bin', 0, 4), -27);
    assert.equal(f.open(directory, '資料.bin', 0, 0, 0), -27);
  });

  test(`${target}: allocation failure is not reported as file success`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const failure = new Error('allocation failed');
    const imports = createReadOnlyAssetImports(target, () => memory, () => { throw failure; },
      { mountPath: '/assets', fileName: 'data', bytes: new Uint8Array([1]) });
    assert.throws(() => imports[`${prefix}|wasi:filesystem/preopens@0.2`]['get-directories'](address(32)),
      error => error === failure);
    assert.deepEqual([...new Uint8Array(memory.buffer, 32, width * 2)], Array(width * 2).fill(0));
  });
}

test('rejects invalid setup without invoking guest capabilities', () => {
  const unexpected = () => assert.fail('Invalid setup must not invoke capabilities');
  const asset = { mountPath: '/assets', fileName: 'data', bytes: new Uint8Array() };
  assert.throws(() => createReadOnlyAssetImports('wasm16', unexpected, unexpected, asset), TypeError);
  assert.throws(() => createReadOnlyAssetImports('wasm32', null, unexpected, asset), TypeError);
  assert.throws(() => createReadOnlyAssetImports('wasm32', unexpected, null, asset), TypeError);
  for (const mountPath of [null, 'relative'])
    assert.throws(() => createReadOnlyAssetImports('wasm32', unexpected, unexpected, { ...asset, mountPath }), TypeError);
  for (const fileName of [null, '', 'a/b', '.', '..'])
    assert.throws(() => createReadOnlyAssetImports('wasm32', unexpected, unexpected, { ...asset, fileName }), TypeError);
  assert.throws(() => createReadOnlyAssetImports('wasm32', unexpected, unexpected, { ...asset, bytes: [] }), TypeError);
});

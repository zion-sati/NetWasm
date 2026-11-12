import assert from 'node:assert/strict';
import test from 'node:test';
import { createReadOnlyAssetImports } from './read-only-asset-imports.mjs';

const returned = 2;
const completed = 0;
const dropped = 1;

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
    const preopens = imports[`${prefix}|wasi:filesystem/preopens@0.3`];
    const types = imports[`${prefix}|wasi:filesystem/types@0.3`];
    const builtIns = imports['wasi:filesystem/types@0.3.2'];
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
    // The record CoreLib writes, its path's address and length pointer-sized, and the result
    // whose payload lies at the pointer width.
    const writeAddress = (at, value) => width === 8
      ? view().setBigUint64(at, BigInt(value), true)
      : view().setUint32(at, value, true);
    const open = (directory, name = '資料.bin', pathFlags = 0, openFlags = 0, flags = 1) => {
      const encoded = new TextEncoder().encode(name);
      new Uint8Array(memory.buffer, 256, encoded.length).set(encoded);
      view().setInt32(128, directory, true);
      view().setUint8(132, pathFlags);
      writeAddress(136, 256);
      writeAddress(136 + width, encoded.length);
      view().setUint8(136 + width * 2, openFlags);
      view().setUint8(137 + width * 2, flags);
      for (let offset = 0; offset < width * 5; offset++) view().setUint8(64 + offset, 0xcc);
      assert.equal(builtIns['[async-lower][method]descriptor.open-at'](address(128), address(64)), returned);
      return view().getUint8(64) === 0 ? view().getInt32(64 + width, true) : -view().getUint8(64 + width);
    };
    const streamOf = (handle, offset) => {
      types['[method]descriptor.read-via-stream'](handle, BigInt(offset), address(96));
      return { stream: view().getInt32(96, true), future: view().getInt32(100, true) };
    };
    const read = (stream, length) => {
      const status = builtIns['[async-lower][stream-read-0][method]descriptor.read-via-stream'](
        stream, address(512), address(length));
      const count = status >>> 4;
      return { copy: status & 15, bytes: [...new Uint8Array(memory.buffer, 512, count)] };
    };
    const outcome = future => {
      assert.equal(builtIns['[async-lower][future-read-1][method]descriptor.read-via-stream'](
        future, address(160)), completed);
      return view().getUint8(160) === 0 ? 'ok' : view().getUint8(160 + width);
    };
    return { allocations, imports, types, builtIns, directories, open, streamOf, read, outcome };
  }

  test(`${target}: streams the immutable bytes from independent handles`, () => {
    const bytes = new Uint8Array([10, 20, 30, 40]);
    const f = fixture(bytes);
    bytes.fill(99);
    assert.deepEqual(Object.keys(f.imports), [
      `${prefix}|wasi:filesystem/preopens@0.3`, `${prefix}|wasi:filesystem/types@0.3`,
      'wasi:filesystem/types@0.3.2',
    ]);
    const firstDirectory = f.directories();
    const secondDirectory = f.directories();
    assert.notEqual(firstDirectory, secondDirectory);
    assert.deepEqual(f.allocations.slice(0, 2), [
      { length: new TextEncoder().encode('/資産').length, alignment: 1 },
      { length: width * 3, alignment: width },
    ]);
    const first = f.open(firstDirectory);
    const second = f.open(secondDirectory, '資料.bin', 1);
    assert.ok(first > 0 && second > 0 && first !== second);

    const ends = f.streamOf(first, 1);
    assert.deepEqual(f.read(ends.stream, 2), { copy: completed, bytes: [20, 30] });
    assert.deepEqual(f.read(ends.stream, 10), { copy: completed, bytes: [40] });
    assert.deepEqual(f.read(ends.stream, 10), { copy: dropped, bytes: [] });
    assert.equal(f.outcome(ends.future), 'ok');
    f.builtIns['[stream-drop-readable-0][method]descriptor.read-via-stream'](ends.stream);
    f.builtIns['[future-drop-readable-1][method]descriptor.read-via-stream'](ends.future);
    assert.throws(() => f.read(ends.stream, 1), /Unknown asset stream/);
    assert.throws(() => f.outcome(ends.future), /Unknown asset future/);
    assert.throws(
      () => f.builtIns['[stream-drop-readable-0][method]descriptor.read-via-stream'](ends.stream),
      /Unknown asset stream/);
    assert.throws(
      () => f.builtIns['[future-drop-readable-1][method]descriptor.read-via-stream'](ends.future),
      /Unknown asset future/);

    // An offset at or past the end, or one a signed 64-bit value wraps, ends at once.
    for (const offset of [4, 9, -1]) {
      const past = f.streamOf(second, offset);
      assert.deepEqual(f.read(past.stream, 1), { copy: dropped, bytes: [] });
      assert.equal(f.outcome(past.future), 'ok');
    }

    f.types['[resource-drop]descriptor'](first);
    assert.throws(() => f.types['[resource-drop]descriptor'](first), /Unknown asset descriptor/);
    const closed = f.streamOf(first, 0);
    assert.deepEqual(f.read(closed.stream, 1), { copy: dropped, bytes: [] });
    assert.equal(f.outcome(closed.future), 2);
    f.types['[resource-drop]descriptor'](firstDirectory);
    assert.equal(f.open(firstDirectory), -2);
  });

  test(`${target}: an empty file, and WASI 0.3's error codes`, () => {
    const f = fixture(new Uint8Array());
    const directory = f.directories();
    const file = f.open(directory);
    const empty = f.streamOf(file, 0);
    assert.deepEqual(f.read(empty.stream, 10), { copy: dropped, bytes: [] });
    assert.equal(f.outcome(empty.future), 'ok');
    const listing = f.streamOf(directory, 0);
    assert.deepEqual(f.read(listing.stream, 10), { copy: dropped, bytes: [] });
    assert.equal(f.outcome(listing.future), 13);
    assert.equal(f.open(999), -2);
    assert.equal(f.open(file), -23);
    assert.equal(f.open(directory, 'absent'), -19);
    for (const name of ['/資料.bin', '../資料.bin', 'a/../資料.bin'])
      assert.equal(f.open(directory, name), -30);
    for (const openFlags of [1, 8]) assert.equal(f.open(directory, '資料.bin', 0, openFlags), -32);
    for (const flags of [2, 32]) assert.equal(f.open(directory, '資料.bin', 0, 0, flags), -32);
    assert.equal(f.open(directory, '資料.bin', 0, 2), -23);
    assert.equal(f.open(directory, '資料.bin', 2), -26);
    assert.equal(f.open(directory, '資料.bin', 0, 4), -26);
    assert.equal(f.open(directory, '資料.bin', 0, 0, 0), -26);
  });

  test(`${target}: allocation failure is not reported as file success`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const failure = new Error('allocation failed');
    const imports = createReadOnlyAssetImports(target, () => memory, () => { throw failure; },
      { mountPath: '/assets', fileName: 'data', bytes: new Uint8Array([1]) });
    assert.throws(() => imports[`${prefix}|wasi:filesystem/preopens@0.3`]['get-directories'](address(32)),
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

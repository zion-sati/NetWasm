import assert from 'node:assert/strict';
import test from 'node:test';
import { createOutputStreamImports } from './output-stream-imports.mjs';

const completed = 0;

for (const target of ['wasm32', 'wasm64']) {
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const address = value => target === 'wasm64' ? BigInt(value) : value;
  const ends = packed => ({ readable: Number(packed & 0xffffffffn), writable: Number(packed >> 32n) });
  const name = suffix => `${suffix}write-via-stream`;

  test(`${target}: delivers each stream's bytes to its sink and survives memory growth`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const stdout = [];
    const stderr = [];
    const imports = createOutputStreamImports(target, () => memory,
      bytes => { stdout.push(bytes); memory.grow(1); },
      bytes => { stderr.push(bytes); memory.grow(1); });
    assert.deepEqual(Object.keys(imports), [
      'wasi:cli/stdout@0.3.2', 'wasi:cli/stderr@0.3.2',
      `${prefix}|wasi:cli/stdout@0.3`, `${prefix}|wasi:cli/stderr@0.3`,
    ]);
    const out = imports['wasi:cli/stdout@0.3.2'];
    const err = imports['wasi:cli/stderr@0.3.2'];
    const first = ends(out[name('[stream-new-0]')]());
    const second = ends(out[name('[stream-new-0]')]());
    const error = ends(err[name('[stream-new-0]')]());
    assert.equal(new Set([first.readable, first.writable, second.readable, error.writable]).size, 4);
    const firstFuture = imports[`${prefix}|wasi:cli/stdout@0.3`]['write-via-stream'](first.readable);
    const secondFuture = imports[`${prefix}|wasi:cli/stdout@0.3`]['write-via-stream'](second.readable);
    const errorFuture = imports[`${prefix}|wasi:cli/stderr@0.3`]['write-via-stream'](error.readable);
    assert.throws(() => imports[`${prefix}|wasi:cli/stdout@0.3`]['write-via-stream'](first.readable),
      /Unknown output stream/);

    const contents = new TextEncoder().encode('café\0水\n');
    new Uint8Array(memory.buffer, 128, contents.length).set(contents);
    assert.equal(out[name('[async-lower][stream-write-0]')](
      first.writable, address(128), address(contents.length)), contents.length * 16 + completed);
    new Uint8Array(memory.buffer, 128, contents.length).fill(0);
    assert.deepEqual(stdout, [contents]);
    assert.equal(err[name('[async-lower][stream-write-0]')](error.writable, address(128), address(0)), completed);
    assert.deepEqual(stderr, [new Uint8Array()]);
    assert.equal(out[name('[async-lower][stream-write-0]')](second.writable, address(128), address(2)), 32);
    assert.deepEqual(stdout, [contents, new Uint8Array(2)]);

    out[name('[stream-drop-writable-0]')](first.writable);
    assert.throws(() => out[name('[async-lower][stream-write-0]')](first.writable, address(128), address(1)),
      /Unknown output stream/);
    assert.throws(() => out[name('[stream-drop-writable-0]')](first.writable), /Unknown output stream/);
    new DataView(memory.buffer).setUint8(32, 1);
    assert.equal(out[name('[async-lower][future-read-1]')](firstFuture, address(32)), completed);
    assert.equal(new DataView(memory.buffer).getUint8(32), 0);
    out[name('[future-drop-readable-1]')](firstFuture);
    assert.throws(() => out[name('[async-lower][future-read-1]')](firstFuture, address(32)), /Unknown output future/);
    assert.throws(() => out[name('[future-drop-readable-1]')](firstFuture), /Unknown output future/);
    out[name('[future-drop-readable-1]')](secondFuture);
    err[name('[future-drop-readable-1]')](errorFuture);
  });

  test(`${target}: a stream without a destination or a failing sink publishes nothing`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const failure = new Error('sink failed');
    let writes = 0;
    const imports = createOutputStreamImports(target, () => memory,
      () => assert.fail('Wrong sink selected'),
      bytes => { assert.deepEqual(bytes, new Uint8Array([42])); writes++; throw failure; });
    new Uint8Array(memory.buffer)[128] = 42;
    const err = imports['wasi:cli/stderr@0.3.2'];
    const unbound = ends(err[name('[stream-new-0]')]());
    assert.throws(() => err[name('[async-lower][stream-write-0]')](unbound.writable, address(128), address(1)),
      /no destination/);
    imports[`${prefix}|wasi:cli/stderr@0.3`]['write-via-stream'](unbound.readable);
    assert.throws(() => err[name('[async-lower][stream-write-0]')](unbound.writable, address(128), address(1)),
      thrown => thrown === failure);
    assert.equal(writes, 1);
    assert.throws(() => imports[`${prefix}|wasi:cli/stderr@0.3`]['write-via-stream'](999), /Unknown output stream/);
  });
}

test('rejects invalid construction before invoking capabilities', () => {
  const unexpected = () => assert.fail('Invalid construction must not invoke capabilities');
  assert.throws(() => createOutputStreamImports('wasm16', unexpected, unexpected, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', null, unexpected, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', unexpected, null, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', unexpected, unexpected, null), TypeError);
});

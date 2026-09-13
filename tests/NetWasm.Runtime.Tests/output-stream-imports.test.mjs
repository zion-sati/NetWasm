import assert from 'node:assert/strict';
import test from 'node:test';
import { createOutputStreamImports } from './output-stream-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const address = value => target === 'wasm64' ? BigInt(value) : value;
  test(`${target}: delivers independent stream bytes and survives sink memory growth`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const stdout = [];
    const stderr = [];
    const imports = createOutputStreamImports(target, () => memory,
      bytes => { stdout.push(bytes); memory.grow(1); },
      bytes => { stderr.push(bytes); memory.grow(1); });
    assert.deepEqual(Object.keys(imports), [
      `${prefix}|wasi:cli/stdout@0.2`, `${prefix}|wasi:cli/stderr@0.2`, `${prefix}|wasi:io/streams@0.2`,
    ]);
    const streams = imports[`${prefix}|wasi:io/streams@0.2`];
    const write = streams['[method]output-stream.blocking-write-and-flush'];
    const drop = streams['output-stream_drop'];
    const first = imports[`${prefix}|wasi:cli/stdout@0.2`]['get-stdout']();
    const second = imports[`${prefix}|wasi:cli/stdout@0.2`]['get-stdout']();
    const error = imports[`${prefix}|wasi:cli/stderr@0.2`]['get-stderr']();
    assert.equal(new Set([first, second, error]).size, 3);
    const contents = new TextEncoder().encode('café\0水\n');
    new Uint8Array(memory.buffer, 128, contents.length).set(contents);
    new DataView(memory.buffer).setUint8(32, 1);
    write(first, address(128), address(contents.length), address(32));
    assert.equal(new DataView(memory.buffer).getUint8(32), 0);
    new Uint8Array(memory.buffer, 128, contents.length).fill(0);
    assert.deepEqual(stdout, [contents]);
    assert.deepEqual(stderr, []);
    write(error, address(128), address(0), address(32));
    assert.deepEqual(stderr, [new Uint8Array()]);
    drop(first);
    assert.throws(() => write(first, address(128), address(1), address(32)), TypeError);
    assert.throws(() => drop(first), TypeError);
    write(second, address(128), address(2), address(32));
    assert.deepEqual(stdout, [contents, new Uint8Array(2)]);
    drop(second);
    drop(error);
    assert.throws(() => write(999, address(0), address(0), address(32)), TypeError);
    assert.throws(() => drop(999), TypeError);
  });

  test(`${target}: sink failure propagates without publishing success`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const failure = new Error('sink failed');
    let writes = 0;
    const imports = createOutputStreamImports(target, () => memory,
      () => assert.fail('Wrong sink selected'),
      bytes => { assert.deepEqual(bytes, new Uint8Array([42])); writes++; throw failure; });
    new Uint8Array(memory.buffer)[128] = 42;
    new DataView(memory.buffer).setUint8(32, 0xff);
    const handle = imports[`${prefix}|wasi:cli/stderr@0.2`]['get-stderr']();
    assert.throws(() => imports[`${prefix}|wasi:io/streams@0.2`]['[method]output-stream.blocking-write-and-flush'](
      handle, address(128), address(1), address(32)), error => error === failure);
    assert.equal(writes, 1);
    assert.equal(new DataView(memory.buffer).getUint8(32), 0xff);
  });
}

test('rejects invalid construction before invoking capabilities', () => {
  const unexpected = () => assert.fail('Invalid construction must not invoke capabilities');
  assert.throws(() => createOutputStreamImports('wasm16', unexpected, unexpected, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', null, unexpected, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', unexpected, null, unexpected), TypeError);
  assert.throws(() => createOutputStreamImports('wasm32', unexpected, unexpected, null), TypeError);
});

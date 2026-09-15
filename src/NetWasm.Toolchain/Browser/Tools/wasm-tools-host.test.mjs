import assert from 'node:assert/strict';
import test from 'node:test';
import { createWasmToolsHost } from './wasm-tools-host.mjs';
const page = 65536;
function moduleBytes(initial = 1, maximum) {
  const limits = maximum === undefined ? [1, 0, initial] : [1, 1, initial, maximum];
  return Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0, 5, limits.length, ...limits,
    7, 10, 1, 6, 109, 101, 109, 111, 114, 121, 2, 0);
}
function shim(start) {
  class File { constructor(data) { this.data = data; } }
  class PreopenDirectory { constructor(name, entries) { this.dir = { contents: new Map(entries) }; } fd_close() { } }
  class OpenFile { fd_close() { } }
  class ConsoleStdout { fd_close() { } }
  class WASI {
    constructor(args, env, fds) { this.fds = fds; this.wasiImport = {}; }
    start(instance) { this.inst = instance; return start(instance); }
  }
  return { File, PreopenDirectory, OpenFile, ConsoleStdout, WASI };
}

test('default ceiling is enforced on fresh instances while only the capped module is reused', async () => {
  let loads = 0, runs = 0;
  const source = moduleBytes();
  const host = createWasmToolsHost({ loadAsset: async () => { loads++; return source; },
    wasiShim: shim(instance => {
      runs++;
      assert.equal(instance.exports.memory.buffer.byteLength, page);
      assert.throws(() => instance.exports.memory.grow(8192), RangeError);
      return runs === 1 ? 1 : 0;
    }) });
  assert.equal((await host.run({})).exitCode, 1);
  const recovery = await host.run({});
  assert.equal(recovery.exitCode, 0);
  assert.equal(recovery.memoryBytes, page);
  assert.deepEqual(Object.keys(recovery.files), []);
  assert.equal(loads, 1);
  assert.equal(runs, 2);
  assert.equal(source.length, moduleBytes().length);
});

test('configurable ceiling keeps a stricter declared maximum', async () => {
  const host = createWasmToolsHost({ loadAsset: async () => moduleBytes(1, 1),
    limits: { maximumMemoryBytes: 2 * page }, wasiShim: shim(instance => {
      assert.throws(() => instance.exports.memory.grow(1), RangeError); return 0;
    }) });
  assert.equal((await host.run({})).exitCode, 0);
});

test('memory policy failure does not poison the loader or busy state', async () => {
  let loads = 0;
  const host = createWasmToolsHost({ loadAsset: async () => ++loads === 1 ? moduleBytes(2) : moduleBytes(),
    limits: { maximumMemoryBytes: page }, wasiShim: shim(() => 0) });
  await assert.rejects(host.run({}), /initial memory/);
  assert.equal((await host.run({})).exitCode, 0);
  assert.equal(loads, 2);
});

test('rejects invalid memory limits at construction before loading assets', () => {
  for (const maximumMemoryBytes of [0, page - 1, 4294967296 + page, NaN])
    assert.throws(() => createWasmToolsHost({ loadAsset: async () => { throw Error('Must not load'); },
      limits: { maximumMemoryBytes }, wasiShim: shim(() => 0) }));
});

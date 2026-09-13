import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRuntimeContractImports } from './runtime-contract-imports.mjs';

const [modulePath, target] = process.argv.slice(2);
assert.ok(target === 'wasm32' || target === 'wasm64');
const address = value => target === 'wasm64' ? BigInt(value) : value;
const module = await WebAssembly.compile(fs.readFileSync(modulePath));
assert.equal(WebAssembly.Module.imports(module).some(entry =>
  entry.module === 'wasi_snapshot_preview1' || entry.module === 'wasi_unstable'), false);

for (const kind of ['root', 'value', 'stack-trace', 'value-content']) {
  let api;
  const instance = await WebAssembly.instantiate(module,
    createRuntimeContractImports(target, () => api.memory));
  api = instance.exports;
  api.initialize(address(65536), 1, 1);
  const before = BigInt(api.gc_get_metric(0));
  if (kind === 'value-content') {
    const sizes = [0, 1, 2, 3, 7, 8, 15, 16, 17, 31, 32, 33,
      63, 64, 65, 127, 128, 129, 255, 256, 257];
    for (let iteration = 0; iteration < 8; iteration++) {
      const frames = [];
      for (const size of sizes) {
        const frame = api.value_frame_enter(address(size));
        assert.notEqual(BigInt(frame), 0n);
        const fill = 1 + (size + iteration) % 254;
        new Uint8Array(api.memory.buffer, Number(frame), size).fill(fill);
        frames.push({ frame, size, fill });
        for (const active of frames) {
          const bytes = new Uint8Array(api.memory.buffer, Number(active.frame), active.size);
          assert.ok(bytes.every(value => value === active.fill),
            'Allocating another value frame must preserve existing frame contents');
        }
      }
      while (frames.length !== 0) {
        const active = frames.pop();
        const bytes = new Uint8Array(api.memory.buffer, Number(active.frame), active.size);
        assert.ok(bytes.every(value => value === active.fill),
          'Leaving a nested value frame must preserve its caller storage');
        api.value_frame_leave(active.frame);
      }
    }
  } else if (kind === 'stack-trace') {
    for (let index = 0; index < 16384; index++) api.stack_trace_frame_enter(1);
    for (let index = 0; index < 16384; index++) api.stack_trace_frame_leave(1);
  } else {
    const frames = [];
    for (let index = 0; index < 64; index++) {
      const frame = kind === 'root'
        ? api.root_frame_enter(16384)
        : api.value_frame_enter(address(131072));
      assert.notEqual(BigInt(frame), 0n);
      frames.push(frame);
    }
    while (frames.length !== 0) api[`${kind}_frame_leave`](frames.pop());
  }
  assert.equal(BigInt(api.gc_get_metric(0)), before,
    'Metadata allocation must not introduce an unpublished managed collection point');
  api.collect();
  assert.ok(BigInt(api.gc_get_metric(0)) > before,
    'Metadata allocation must restore the original collection state');
}
console.log(JSON.stringify({ target, passed: 4, failed: 0 }));

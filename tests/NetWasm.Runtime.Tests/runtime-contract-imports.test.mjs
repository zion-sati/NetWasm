import assert from 'node:assert/strict';
import test from 'node:test';
import { createRuntimeContractImports } from './runtime-contract-imports.mjs';

for (const target of ['wasm32', 'wasm64']) {
  test(`Native contract imports preserve the empty environment ABI: ${target}`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const bytes = new Uint8Array(memory.buffer);
    bytes.fill(0xa5);
    let reads = 0;
    const imports = createRuntimeContractImports(target, () => {
      reads++;
      return memory;
    });
    assert.equal(reads, 0);
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
    const width = target === 'wasm64' ? 8 : 4;
    imports[`${prefix}|wasi:cli/environment@0.2`]['get-environment'](
      target === 'wasm64' ? 32n : 32);
    assert.equal(reads, 1);
    assert.deepEqual([...bytes.slice(32, 32 + width * 2)], Array(width * 2).fill(0));
    assert.equal(bytes[31], 0xa5);
    assert.equal(bytes[32 + width * 2], 0xa5);
    assert.equal(imports.env.emscripten_notify_memory_growth(), undefined);
    const guarded = [
      imports['netwasm.application.v1']['netwasm.filter'],
      imports['netwasm.application.v1']['netwasm.finalize'],
      imports[`${prefix}|wasi:cli/stdout@0.2`]['get-stdout'],
      imports[`${prefix}|wasi:cli/stderr@0.2`]['get-stderr'],
      imports[`${prefix}|wasi:io/streams@0.2`]['[method]output-stream.blocking-write-and-flush'],
      imports[`${prefix}|wasi:io/streams@0.2`]['output-stream_drop'],
      imports[`${prefix}|wasi:io/error@0.2`].error_drop,
      imports[`${prefix}|wasi:cli/exit@0.2`].exit,
    ];
    for (const call of guarded) assert.throws(call, /Unexpected runtime service call/);
    assert.equal(reads, 1);
    assert.equal(Object.hasOwn(imports, 'wasi_snapshot_preview1'), false);
    assert.equal(Object.hasOwn(imports, 'wasi_unstable'), false);
  });
}

test('Native contract imports reject an unsupported target before memory access', () => {
  let accessed = false;
  assert.throws(() => createRuntimeContractImports('invalid', () => {
    accessed = true;
  }), /Unsupported runtime target/);
  assert.equal(accessed, false);
});

test('Native contract imports require a memory accessor', () => {
  assert.throws(() => createRuntimeContractImports('wasm32', null), /memory accessor/);
});

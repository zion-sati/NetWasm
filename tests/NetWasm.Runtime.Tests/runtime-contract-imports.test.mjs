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
    imports[`${prefix}|wasi:cli/environment@0.3`]['get-environment'](
      target === 'wasm64' ? 32n : 32);
    assert.equal(reads, 1);
    assert.deepEqual([...bytes.slice(32, 32 + width * 2)], Array(width * 2).fill(0));
    assert.equal(bytes[31], 0xa5);
    assert.equal(bytes[32 + width * 2], 0xa5);
    assert.equal(imports.env.emscripten_notify_memory_growth(), undefined);
    assert.equal(imports[`${prefix}|wasi:random/random@0.3`]['get-random-u64'](),
      0x0123456789abcdefn);
    const guarded = [
      imports['netwasm.application.v1']['netwasm.filter'],
      imports['netwasm.application.v1']['netwasm.finalize'],
      imports[`${prefix}|wasi:cli/stdout@0.3`]['write-via-stream'],
      imports[`${prefix}|wasi:cli/stderr@0.3`]['write-via-stream'],
      ...['stdout', 'stderr'].flatMap(stream => Object.values(imports[`wasi:cli/${stream}@0.3.2`])),
      imports[`${prefix}|wasi:cli/exit@0.3`].exit,
      imports.$root['[waitable-set-wait]'],
    ];
    assert.equal(Object.keys(imports['wasi:cli/stdout@0.3.2']).length, 5);
    // Waitable sets and subtasks are bookkeeping only: nothing a contract runs waits.
    assert.equal(imports.$root['[waitable-set-new]'](), 1);
    assert.equal(imports.$root['[waitable-set-new]'](), 2);
    assert.equal(imports.$root['[waitable-join]'](7, 1), undefined);
    assert.equal(imports.$root['[subtask-drop]'](7), undefined);
    assert.equal(imports.$root['[subtask-cancel]'](7), 4);
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

for (const target of ['wasm32', 'wasm64']) {
  test(`Random bytes come back in a list the guest allocates: ${target}`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const address = value => target === 'wasm64' ? BigInt(value) : Number(value);
    const requests = [];
    const exports = {
      component_realloc(oldAddress, oldSize, alignment, size) {
        requests.push([oldAddress, oldSize, alignment, size]);
        return address(256);
      },
    };
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
    const random = createRuntimeContractImports(target, () => memory, () => exports)[
      `${prefix}|wasi:random/random@0.3`];

    random['get-random-bytes'](5n, address(64));

    assert.deepEqual(requests, [[address(0), address(0), address(1), address(5)]]);
    const view = new DataView(memory.buffer);
    const [pointer, count] = target === 'wasm64'
      ? [view.getBigUint64(64, true), view.getBigUint64(72, true)]
      : [view.getUint32(64, true), view.getUint32(68, true)];
    assert.equal(Number(pointer), 256);
    assert.equal(Number(count), 5);
    assert.deepEqual([...new Uint8Array(memory.buffer, 256, 5)], [1, 2, 3, 4, 5]);
    random['get-random-bytes'](2n, address(64));
    assert.deepEqual([...new Uint8Array(memory.buffer, 256, 2)], [6, 7]);
  });

  test(`Random bytes need the guest's allocator: ${target}`, () => {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
    const random = createRuntimeContractImports(target, () => memory)[`${prefix}|wasi:random/random@0.3`];
    assert.throws(() => random['get-random-bytes'](1n, 0), /Unexpected runtime service call/);
  });
}

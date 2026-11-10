import assert from 'node:assert/strict';
import test from 'node:test';
import { validateRuntimeImports } from './runtime-import-validator.mjs';

function importsFor(target) {
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  return [
    ['netwasm.application.v1', 'netwasm.filter'],
    ['netwasm.application.v1', 'netwasm.finalize'],
    ['env', 'emscripten_notify_memory_growth'],
    [`${prefix}|wasi:cli/environment@0.3`, 'get-environment'],
    ...['stdout', 'stderr'].flatMap(stream => [
      [`${prefix}|wasi:cli/${stream}@0.3`, 'write-via-stream'],
      [`wasi:cli/${stream}@0.3.2`, '[stream-new-0]write-via-stream'],
      [`wasi:cli/${stream}@0.3.2`, '[async-lower][stream-write-0]write-via-stream'],
      [`wasi:cli/${stream}@0.3.2`, '[stream-drop-writable-0]write-via-stream'],
      [`wasi:cli/${stream}@0.3.2`, '[async-lower][future-read-1]write-via-stream'],
      [`wasi:cli/${stream}@0.3.2`, '[future-drop-readable-1]write-via-stream'],
    ]),
    ['$root', '[waitable-set-new]'],
    ['$root', '[waitable-join]'],
    ['$root', '[waitable-set-wait]'],
    [`${prefix}|wasi:cli/exit@0.3`, 'exit'],
    [`${prefix}|wasi:cli/exit@0.3`, 'exit-with-code'],
    [`${prefix}|wasi:random/random@0.3`, 'get-random-u64'],
  ].map(([module, name]) => ({ module, name, kind: 'function' }));
}

for (const target of ['wasm32', 'wasm64']) {
  const allowed = importsFor(target);
  test(`${target}: accepts the supported WASI 0.3 runtime interfaces`, () => {
    assert.doesNotThrow(() => validateRuntimeImports(allowed, target));
    for (const collector of ['compact', 'netwasm', 'bump']) {
      assert.doesNotThrow(() => validateRuntimeImports(allowed, target, collector));
    }
  });
  test(`${target}: accepts required imports without optional output or termination`, () => {
    assert.doesNotThrow(() => validateRuntimeImports(allowed.slice(0, 4), target));
  });
  test(`${target}: accepts pruned finalization for every collector`, () => {
    for (const collector of [undefined, 'compact', 'netwasm', 'bump']) {
      assert.doesNotThrow(() => validateRuntimeImports(
        allowed.filter(entry => entry.name !== 'netwasm.finalize'), target, collector));
    }
    assert.throws(() => validateRuntimeImports(
      allowed.filter(entry => entry.name !== 'netwasm.filter'), target, 'bump'),
    { message: 'Missing runtime import: netwasm.application.v1.netwasm.filter' });
  });
  for (const module of ['unsupported', 'wasi_snapshot_preview1', 'wasi_unstable']) {
    test(`${target}: rejects forbidden host module ${module}`, () => {
      assert.throws(() => validateRuntimeImports([
        ...allowed, { module, name: 'fd_write', kind: 'function' },
      ], target), { message: `Unexpected runtime import: ${module}.fd_write (function)` });
    });
  }
  test(`${target}: rejects an unsupported function in a supported module`, () => {
    assert.throws(() => validateRuntimeImports([
      ...allowed, { ...allowed[3], name: 'unsupported' },
    ], target), { message: `Unexpected runtime import: ${allowed[3].module}.unsupported (function)` });
  });
  test(`${target}: rejects a nonfunction import`, () => {
    assert.throws(() => validateRuntimeImports([
      ...allowed, { ...allowed[0], kind: 'memory' },
    ], target), { message: 'Unexpected runtime import: netwasm.application.v1.netwasm.filter (memory)' });
  });
  test(`${target}: rejects duplicate physical imports`, () => {
    assert.throws(() => validateRuntimeImports([...allowed, allowed[0]], target), /Duplicate runtime import/);
  });
  test(`${target}: rejects canonical imports for the other address width`, () => {
    const other = target === 'wasm32' ? 'wasm64' : 'wasm32';
    assert.throws(() => validateRuntimeImports(importsFor(other), target), /Unexpected runtime import/);
  });
  test(`${target}: rejects an empty import surface`, () => {
    assert.throws(() => validateRuntimeImports([], target), /Missing runtime import/);
  });
  for (const missing of allowed.slice(0, 4).filter(entry => entry.name !== 'netwasm.finalize')) {
    test(`${target}: rejects a missing required import ${missing.name}`, () => {
      assert.throws(() => validateRuntimeImports(
        allowed.filter(entry => entry !== missing), target),
      { message: `Missing runtime import: ${missing.module}.${missing.name}` });
    });
  }
}

test('rejects an unsupported runtime collector', () => {
  assert.throws(() => validateRuntimeImports([], 'wasm32', 'other'),
    /Unsupported runtime collector/);
});

for (const target of [undefined, null, '', 'wasm16']) {
  test(`rejects an unspecified or unsupported target: ${target}`, () => {
    assert.throws(() => validateRuntimeImports([], target), /Unsupported runtime target/);
  });
}

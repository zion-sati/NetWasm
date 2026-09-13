import assert from 'node:assert/strict';
import test from 'node:test';
import { validateRuntimeImports } from './runtime-import-validator.mjs';

function importsFor(target) {
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  return [
    ['netwasm.application.v1', 'netwasm.filter'],
    ['netwasm.application.v1', 'netwasm.finalize'],
    [`${prefix}|wasi:cli/environment@0.2`, 'get-environment'],
    [`${prefix}|wasi:cli/stdout@0.2`, 'get-stdout'],
    [`${prefix}|wasi:cli/stderr@0.2`, 'get-stderr'],
    [`${prefix}|wasi:io/streams@0.2`, '[method]output-stream.blocking-write-and-flush'],
    [`${prefix}|wasi:io/streams@0.2`, 'output-stream_drop'],
    [`${prefix}|wasi:io/error@0.2`, 'error_drop'],
    [`${prefix}|wasi:cli/exit@0.2`, 'exit'],
  ].map(([module, name]) => ({ module, name, kind: 'function' }));
}

for (const target of ['wasm32', 'wasm64']) {
  const allowed = importsFor(target);
  test(`${target}: accepts the supported Preview 2 runtime interfaces`, () => {
    assert.doesNotThrow(() => validateRuntimeImports(allowed, target));
  });
  test(`${target}: accepts required imports without optional output or termination`, () => {
    assert.doesNotThrow(() => validateRuntimeImports(allowed.slice(0, 3), target));
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
      ...allowed, { ...allowed[2], name: 'unsupported' },
    ], target), { message: `Unexpected runtime import: ${allowed[2].module}.unsupported (function)` });
  });
  test(`${target}: rejects a nonfunction import`, () => {
    assert.throws(() => validateRuntimeImports([
      ...allowed, { ...allowed[0], kind: 'memory' },
    ], target), { message: 'Unexpected runtime import: netwasm.application.v1.netwasm.filter (memory)' });
  });
  test(`${target}: rejects canonical imports for the other address width`, () => {
    const other = target === 'wasm32' ? 'wasm64' : 'wasm32';
    assert.throws(() => validateRuntimeImports(importsFor(other), target), /Unexpected runtime import/);
  });
  test(`${target}: rejects an empty import surface`, () => {
    assert.throws(() => validateRuntimeImports([], target), /Missing runtime import/);
  });
  for (const missing of allowed.slice(0, 3)) {
    test(`${target}: rejects a missing required import ${missing.name}`, () => {
      assert.throws(() => validateRuntimeImports(
        allowed.filter(entry => entry !== missing), target),
      { message: `Missing runtime import: ${missing.module}.${missing.name}` });
    });
  }
}

for (const target of [undefined, null, '', 'wasm16']) {
  test(`rejects an unspecified or unsupported target: ${target}`, () => {
    assert.throws(() => validateRuntimeImports([], target), /Unsupported runtime target/);
  });
}

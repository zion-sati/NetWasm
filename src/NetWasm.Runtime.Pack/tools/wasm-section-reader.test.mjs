import assert from 'node:assert/strict';
import test from 'node:test';
import { readCustomSections, readFunctionImports } from './wasm-section-reader.mjs';

const encoder = new TextEncoder();
const u32 = value => {
  const bytes = [];
  do {
    let byte = value & 0x7f;
    value >>>= 7;
    if (value !== 0) byte |= 0x80;
    bytes.push(byte);
  } while (value !== 0);
  return bytes;
};
const name = value => {
  const bytes = [...encoder.encode(value)];
  return [...u32(bytes.length), ...bytes];
};
const section = (id, payload) => [id, ...u32(payload.length), ...payload];
const module = (...sections) => Buffer.from([0, 97, 115, 109, 1, 0, 0, 0, ...sections.flat()]);

test('reads function imports without asking the JavaScript engine to compile the module', () => {
  const imports = [
    ...u32(2),
    ...name('netwasm.application.v1'), ...name('netwasm.filter'), 0, ...u32(3),
    ...name('cm64p2|wasi:cli/environment@0.2'), ...name('get-environment'), 0, ...u32(4),
  ];
  assert.deepEqual(readFunctionImports(module(section(2, imports))), [
    { module: 'netwasm.application.v1', name: 'netwasm.filter', kind: 'function' },
    { module: 'cm64p2|wasi:cli/environment@0.2', name: 'get-environment', kind: 'function' },
  ]);
});

test('reads matching custom-section payloads', () => {
  const wasm = module(
    section(0, [...name('unrelated'), 1]),
    section(0, [...name('component-type:netwasm-runtime'), 2, 3, 4]));
  assert.deepEqual(readCustomSections(wasm, 'component-type:netwasm-runtime').map(Buffer.from), [
    Buffer.from([2, 3, 4]),
  ]);
});

test('rejects truncated modules', () => {
  assert.throws(() => readFunctionImports(Buffer.from([0, 97, 115, 109])), /Unexpected end/);
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { capDefinedWasm32Memory, validateWasm32MemoryCeiling } from './wasm32-memory-ceiling.mjs';
import { ToolLimitError } from './tool-inputs.mjs';
const page = 65536;
const magic = [0, 97, 115, 109, 1, 0, 0, 0];
const section = (id, bytes) => [id, bytes.length, ...bytes];
const memory = payload => Uint8Array.of(...magic, ...section(5, payload));

function growModule() {
  return Uint8Array.of(...magic,
    ...section(1, [1, 96, 1, 127, 1, 127]),
    ...section(3, [1, 0]), ...section(5, [1, 0, 1]),
    ...section(7, [2, 6, ...Buffer.from('memory'), 2, 0, 4, ...Buffer.from('grow'), 0, 0]),
    ...section(10, [1, 6, 0, 32, 0, 64, 0, 11]));
}

test('ceiling denies both Wasm and JavaScript growth at the declared limit', async () => {
  const original = growModule();
  const capped = capDefinedWasm32Memory(original, 2 * page);
  const { instance } = await WebAssembly.instantiate(capped);
  assert.equal(instance.exports.grow(1), 1);
  assert.equal(instance.exports.grow(1), -1);
  assert.equal(instance.exports.memory.buffer.byteLength, 2 * page);
  assert.throws(() => instance.exports.memory.grow(1), RangeError);
  const uncapped = await WebAssembly.instantiate(original);
  assert.equal(uncapped.instance.exports.grow(2), 1);
});

test('preserves a stricter declared maximum and returns owned bytes', async () => {
  const original = memory([1, 1, 1, 1]);
  const capped = capDefinedWasm32Memory(original, 8 * page);
  assert.deepEqual(capped, original);
  capped[0] = 1;
  assert.equal(original[0], 0);
  const { instance } = await WebAssembly.instantiate(original);
  assert.equal(WebAssembly.Module.imports(new WebAssembly.Module(original)).length, 0);
  assert.ok(instance);
});

test('lowers a declared maximum without changing function imports, exports or code', async () => {
  const original = Uint8Array.of(...magic,
    ...section(1, [1, 96, 0, 0]),
    ...section(2, [1, 1, 109, 1, 102, 0, 0]),
    ...section(5, [1, 1, 1, 8]),
    ...section(7, [2, 1, 102, 0, 0, 6, ...Buffer.from('memory'), 2, 0]),
    ...section(0, [1, 120, 7, 8]));
  const capped = capDefinedWasm32Memory(original, 2 * page);
  const originalModule = new WebAssembly.Module(original), cappedModule = new WebAssembly.Module(capped);
  assert.deepEqual(WebAssembly.Module.imports(cappedModule), WebAssembly.Module.imports(originalModule));
  assert.deepEqual(WebAssembly.Module.exports(cappedModule), WebAssembly.Module.exports(originalModule));
  assert.deepEqual(WebAssembly.Module.customSections(cappedModule, 'x'), WebAssembly.Module.customSections(originalModule, 'x'));
  const { memory: actual } = (await WebAssembly.instantiate(cappedModule, { m: { f() {} } })).exports;
  assert.equal(actual.grow(1), 1);
  assert.throws(() => actual.grow(1), RangeError);
});

test('rejects an initial size greater than the ceiling and invalid declared limits', () => {
  assert.throws(() => capDefinedWasm32Memory(memory([1, 0, 3]), 2 * page), ToolLimitError);
  assert.throws(() => capDefinedWasm32Memory(memory([1, 1, 3, 2]), 8 * page), /Invalid wasm32/);
});

test('validates ceiling alignment, positivity, wasm32 range and numeric representation', () => {
  for (const value of [0, -page, 1, page + 1, 4294967296 + page, NaN, Infinity, '65536'])
    assert.throws(() => validateWasm32MemoryCeiling(value));
  assert.equal(validateWasm32MemoryCeiling(4294967296), 65536);
});

test('rejects unsupported memory shapes rather than leaving another memory uncapped', () => {
  for (const payload of [[0], [2, 0, 1, 0, 1], [1, 2, 1], [1, 4, 1], [1, 3, 1, 1]])
    assert.throws(() => capDefinedWasm32Memory(memory(payload), page));
  assert.throws(() => capDefinedWasm32Memory(Uint8Array.of(...magic), page), /exactly one/);
  assert.throws(() => capDefinedWasm32Memory(Uint8Array.of(...magic, ...section(5, [1, 0, 1]), ...section(5, [1, 0, 1])), page), /exactly one/);
  assert.throws(() => capDefinedWasm32Memory(Uint8Array.of(...magic, ...section(2, [1, 1, 109, 1, 102, 2, 0, 1]), ...section(5, [1, 0, 1])), page), /only functions/);
});

test('rejects malformed headers, section extents, ULEBs and trailing memory bytes', () => {
  for (const bytes of [[], [...magic.slice(0, 7)], [1, ...magic.slice(1)],
    [...magic, 5, 4, 1, 0, 1], [...magic, 5, 128],
    [...magic, 5, 128, 128, 128, 128, 16],
    [...magic, ...section(5, [1, 0, 128])], [...magic, ...section(5, [1, 0, 1, 9])],
    [...magic, ...section(2, [1, 4, 109]), ...section(5, [1, 0, 1])]])
    assert.throws(() => capDefinedWasm32Memory(Uint8Array.from(bytes), page));
});

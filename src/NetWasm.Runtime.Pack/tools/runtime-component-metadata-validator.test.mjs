import assert from 'node:assert/strict';
import test from 'node:test';
import { validateRuntimeComponentMetadata } from './runtime-component-metadata-validator.mjs';

const expected = Buffer.from('native component metadata');
const section = new TextEncoder().encode('native component metadata').buffer;

test('accepts exactly the encoded native WIT metadata', () => {
  assert.doesNotThrow(() => validateRuntimeComponentMetadata([section], expected));
});

for (const sections of [[], [section, section]]) {
  test(`rejects metadata section count ${sections.length}`, () => {
    assert.throws(() => validateRuntimeComponentMetadata(sections, expected),
      { message: 'Runtime must contain exactly one native component metadata section' });
  });
}

test('rejects different metadata bytes', () => {
  assert.throws(() => validateRuntimeComponentMetadata([section], Buffer.from('another contract')),
    { message: 'Runtime component metadata does not match the native WIT contract' });
});

test('rejects an empty expected contract even when the section is also empty', () => {
  assert.throws(() => validateRuntimeComponentMetadata([new ArrayBuffer(0)], Buffer.alloc(0)),
    { message: 'Runtime component metadata does not match the native WIT contract' });
});

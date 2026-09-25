import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import test from 'node:test';
import { cacheMatches, expected, resolveCompilerPath } from './cache-contract.mjs';

test('arithmetic expectations match an independent closed-form integer oracle', () => {
  for (const input of [-2147483648, -1000, -1, 0, 1, 1000, 2147483647])
    for (const edited of [false, true]) for (const referenceEdited of [false, true]) {
      const exact = (edited ? 41418n : 37044n) + 1093n * BigInt(input) + (referenceEdited ? 5465n : 0n);
      assert.equal(expected(input, edited, referenceEdited), Number(BigInt.asIntN(32, exact)));
    }
});

test('cold, disabled and changed-dependency stages require misses without hits', () => {
  for (const phase of ['disabled', 'cold', 'reference-edited']) {
    assert.equal(cacheMatches(phase, { Hits: 0, Misses: 1 }), true);
    assert.equal(cacheMatches(phase, { Hits: 1, Misses: 1 }), false);
    assert.equal(cacheMatches(phase, { Hits: 0, Misses: 0 }), false);
  }
});

test('same-process warm stage requires a memory hit', () => {
  assert.equal(cacheMatches('memory-warm', { Hits: 1, MemoryHits: 1 }), true);
  assert.equal(cacheMatches('memory-warm', { Hits: 0, MemoryHits: 1 }), false);
  assert.equal(cacheMatches('memory-warm', { Hits: 1, MemoryHits: 0 }), false);
});

test('fresh-process warm and reverted stages require disk hits', () => {
  for (const phase of ['disk-warm', 'edited', 'reverted', 'reference-reverted']) {
    assert.equal(cacheMatches(phase, { Hits: 1, DiskHits: 1 }), true);
    assert.equal(cacheMatches(phase, { Hits: 0, DiskHits: 1 }), false);
    assert.equal(cacheMatches(phase, { Hits: 1, DiskHits: 0 }), false);
  }
});

test('missing metrics and unknown stages cannot pass', () => {
  assert.equal(cacheMatches('cold', undefined), false);
  assert.throws(() => cacheMatches('unknown', {}), /unknown-cache-phase/);
});

test('compiler selection uses the exact repository SDK pin', () => {
  assert.equal(resolveCompilerPath('unrelated\n10.0.300 [/other]\n10.0.302 [/sdk root]\r\n', '10.0.302'),
    resolve('/sdk root/10.0.302/Roslyn/bincore/csc.dll'));
});

test('absent or ambiguous SDK installations fail rather than choosing a fallback', () => {
  assert.throws(() => resolveCompilerPath('', '10.0.302'), /pinned-sdk-not-unique/);
  assert.throws(() => resolveCompilerPath('10.0.302 [/one]\n10.0.302 [/two]', '10.0.302'), /pinned-sdk-not-unique/);
});

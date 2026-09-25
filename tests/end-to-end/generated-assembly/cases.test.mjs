import assert from 'node:assert/strict';
import test from 'node:test';
import { cases, matchesObservations } from './cases.mjs';

test('all actual generator families have independent expected observations', () => {
  assert.deepEqual(cases.map(item => item.id), ['stj', 'regex', 'logging', 'activation', 'binding']);
  for (const item of cases) {
    assert.equal(item.values.length, item.expected.length);
    assert.ok(item.values.length > 0);
    assert.ok(item.libraries.length > 0);
  }
  assert.notEqual(cases[3].type, cases[4].type);
});

test('both oracle protocols require exact values and complete observation counts', () => {
  assert.equal(matchesObservations([{ kind: 'value', value: 42 }], [42]), true);
  assert.equal(matchesObservations([{ Kind: 'value', Value: 42 }], [42], true), true);
  assert.equal(matchesObservations(null, [42]), false);
  assert.equal(matchesObservations([], [42]), false);
  assert.equal(matchesObservations([{ kind: 'exception', value: 42 }], [42]), false);
  assert.equal(matchesObservations([{ kind: 'value', value: 0 }], [42]), false);
  assert.equal(matchesObservations([{ Kind: 'exception', Value: 42 }], [42], true), false);
  assert.equal(matchesObservations([{ Kind: 'value', Value: 0 }], [42], true), false);
});

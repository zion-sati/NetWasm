import assert from 'node:assert/strict';
import test from 'node:test';
import { validateSemanticCoverage } from './semantic-coverage-validator.mjs';

function fixture() {
  return {
    semanticFamilies: Array.from({ length: 32 }, (_, index) => ({
      id: `S${String(index + 1).padStart(2, '0')}`,
      name: 'Family', gap: 'Boundary cases pending', status: 'unqualified',
      requiredBackend: index === 15 || index === 16 ? 'linked-runtime' : 'simulated-or-linked',
      requiredExecution: ['wasm32', 'raw-wasm64'], existingEvidence: ['suite.cs'],
    })),
    operationObligations: [{ id: 'storage', obligations: ['S04'], operations: ['Load', 'Store'] }],
  };
}

const validate = inventory => validateSemanticCoverage(inventory, ['Load', 'Store'], new Set(['suite.cs']));

test('classifies all families and operations without claiming execution', () => {
  assert.deepEqual(validate(fixture()), { semanticFamilyCount: 32, semanticOperationCount: 2 });
});

test('keeps an external evidence gap explicit', () => {
  const inventory = fixture();
  inventory.semanticFamilies[27].existingEvidence = [];
  inventory.semanticFamilies[27].externalEvidence = 'Adjacent package evidence pending';
  inventory.semanticFamilies[27].requiredBackend = 'external-library';
  assert.equal(validate(inventory).semanticFamilyCount, 32);
});

const invalid = [
  ['missing families', x => delete x.semanticFamilies, /lists are required/],
  ['missing groups', x => delete x.operationObligations, /lists are required/],
  ['missing family', x => x.semanticFamilies.pop(), /family universe/],
  ['duplicate family', x => x.semanticFamilies[1].id = 'S01', /family universe/],
  ['unknown family', x => x.semanticFamilies[1].id = 'S99', /family universe/],
  ['missing name', x => delete x.semanticFamilies[0].name, /name and open obligation/],
  ['empty name', x => x.semanticFamilies[0].name = ' ', /name and open obligation/],
  ['missing gap', x => delete x.semanticFamilies[0].gap, /name and open obligation/],
  ['false pass', x => x.semanticFamilies[0].status = 'passed', /cannot claim/],
  ['missing backend', x => delete x.semanticFamilies[0].requiredBackend, /explicit backend/],
  ['missing matrix', x => delete x.semanticFamilies[0].requiredExecution, /both target/],
  ['omitted width', x => x.semanticFamilies[0].requiredExecution.pop(), /both target/],
  ['duplicate wasm64', x => x.semanticFamilies[0].requiredExecution = ['raw-wasm64', 'raw-wasm64'], /both target/],
  ['duplicate wasm32', x => x.semanticFamilies[0].requiredExecution = ['wasm32', 'wasm32'], /both target/],
  ['missing evidence', x => delete x.semanticFamilies[0].existingEvidence, /must be a list/],
  ['unexplained absence', x => x.semanticFamilies[0].existingEvidence = [], /located evidence/],
  ['duplicate evidence', x => x.semanticFamilies[0].existingEvidence.push('suite.cs'), /evidence is duplicated/],
  ['stale evidence', x => x.semanticFamilies[0].existingEvidence = ['deleted.cs'], /path is stale/],
  ['simulated GC', x => x.semanticFamilies[15].requiredBackend = 'simulated-or-linked', /real lifetime/],
  ['simulated finalization', x => x.semanticFamilies[16].requiredBackend = 'simulated-or-linked', /real lifetime/],
  ['missing group ID', x => delete x.operationObligations[0].id, /unique ID/],
  ['duplicate group', x => x.operationObligations.push(structuredClone(x.operationObligations[0])), /unique ID/],
  ['missing obligations', x => delete x.operationObligations[0].obligations, /unknown obligations/],
  ['empty obligations', x => x.operationObligations[0].obligations = [], /unknown obligations/],
  ['duplicate obligation', x => x.operationObligations[0].obligations.push('S04'), /unknown obligations/],
  ['unknown obligation', x => x.operationObligations[0].obligations = ['S99'], /unknown obligations/],
  ['missing operations', x => delete x.operationObligations[0].operations, /group is empty/],
  ['empty operations', x => x.operationObligations[0].operations = [], /group is empty/],
  ['unknown operation', x => x.operationObligations[0].operations.push('Invented'), /unknown operation/],
  ['duplicate operation', x => x.operationObligations[0].operations.push('Load'), /mapping is duplicated/],
  ['unmapped operation', x => x.operationObligations[0].operations.pop(), /lacks a semantic obligation/],
];

for (const [name, mutate, message] of invalid) {
  test(`rejects ${name}`, () => {
    const inventory = fixture();
    mutate(inventory);
    assert.throws(() => validate(inventory), message);
  });
}

test('a newly accepted lowered operation requires a deliberate mapping', () => {
  assert.throws(() => validateSemanticCoverage(fixture(), ['Load', 'Store', 'NewOperation'],
    new Set(['suite.cs'])), /lacks a semantic obligation/);
});

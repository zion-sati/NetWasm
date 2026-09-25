const familyIds = Array.from({ length: 32 }, (_, index) => `S${String(index + 1).padStart(2, '0')}`);
const backends = new Set([
  'unit-and-composition', 'simulated-or-linked', 'linked-runtime',
  'real-host', 'package-consumer', 'external-library',
]);

function requireInvariant(condition, message) {
  if (!condition) throw new Error(message);
}

const nonempty = value => typeof value === 'string' && value.trim().length > 0;

// This is an audit backlog, not an execution receipt. A source reference or an
// operation mapping cannot promote an obligation to qualified status.
export function validateSemanticCoverage(inventory, operations, evidenceFiles) {
  const families = inventory.semanticFamilies;
  const groups = inventory.operationObligations;
  requireInvariant(Array.isArray(families) && Array.isArray(groups), 'semantic inventory lists are required');
  const ids = families.map(row => row.id);
  requireInvariant(ids.length === familyIds.length && new Set(ids).size === ids.length &&
    familyIds.every(id => ids.includes(id)), 'semantic family universe is incomplete or duplicated');
  for (const row of families) {
    requireInvariant(nonempty(row.name) && nonempty(row.gap), 'semantic family needs its name and open obligation');
    requireInvariant(row.status === 'unqualified', 'inventory cannot claim execution qualification');
    requireInvariant(backends.has(row.requiredBackend), 'semantic family needs an explicit backend');
    requireInvariant(Array.isArray(row.requiredExecution) && row.requiredExecution.length === 2 &&
      row.requiredExecution.includes('wasm32') && row.requiredExecution.includes('raw-wasm64'),
    'semantic family must retain both target obligations');
    requireInvariant(Array.isArray(row.existingEvidence), 'semantic evidence must be a list');
    requireInvariant(row.existingEvidence.length > 0 || nonempty(row.externalEvidence),
      'semantic family needs located evidence or an explicit external gap');
    requireInvariant(new Set(row.existingEvidence).size === row.existingEvidence.length,
      'semantic evidence is duplicated');
    requireInvariant(row.existingEvidence.every(path => evidenceFiles.has(path)), 'semantic evidence path is stale');
    if (row.id === 'S16' || row.id === 'S17') {
      requireInvariant(row.requiredBackend === 'linked-runtime', 'real lifetime obligations cannot use a simulated backend');
    }
  }
  const groupIds = new Set();
  const covered = new Set();
  const accepted = new Set(operations);
  for (const group of groups) {
    requireInvariant(nonempty(group.id) && !groupIds.has(group.id), 'operation obligation group needs a unique ID');
    groupIds.add(group.id);
    requireInvariant(Array.isArray(group.obligations) && group.obligations.length > 0 &&
      new Set(group.obligations).size === group.obligations.length &&
      group.obligations.every(id => ids.includes(id)), 'operation group has missing or unknown obligations');
    requireInvariant(Array.isArray(group.operations) && group.operations.length > 0, 'operation group is empty');
    for (const operation of group.operations) {
      requireInvariant(accepted.has(operation), 'semantic mapping names an unknown operation');
      requireInvariant(!covered.has(operation), 'semantic operation mapping is duplicated');
      covered.add(operation);
    }
  }
  requireInvariant(covered.size === accepted.size, 'accepted operation lacks a semantic obligation');
  return { semanticFamilyCount: families.length, semanticOperationCount: covered.size };
}

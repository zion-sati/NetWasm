#!/usr/bin/env node

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validateCoverage } from './audit-cil-coverage.mjs';

const source = JSON.parse(readFileSync(
  new URL('./cil-coverage-inventory.json', import.meta.url), 'utf8'));

assert.doesNotThrow(() => validateCoverage(structuredClone(source)));

const missingFamily = structuredClone(source);
delete missingFamily.families.Numeric;
assert.throws(
  () => validateCoverage(missingFamily),
  /instruction family lacks coverage evidence: Numeric/);

const duplicateOrigin = structuredClone(source);
duplicateOrigin.internalLoweredOperations.push('Add');
assert.throws(
  () => validateCoverage(duplicateOrigin),
  /exactly one accepted origin: Add/);

const missingEvidence = structuredClone(source);
delete missingEvidence.families.Delegates.semantic;
assert.throws(
  () => validateCoverage(missingEvidence),
  /Delegates lacks semantic evidence/);

const runtimeHandleAudit = validateCoverage(structuredClone(source));
assert.ok(runtimeHandleAudit.opcodeCount > 0);
assert.equal(runtimeHandleAudit.semanticFamilyCount, 32);
assert.equal(runtimeHandleAudit.semanticOperationCount, runtimeHandleAudit.operationCount);

const missingSemanticFamily = structuredClone(source);
missingSemanticFamily.semanticFamilies.pop();
assert.throws(() => validateCoverage(missingSemanticFamily), /semantic family universe/);

const missingSemanticOperation = structuredClone(source);
missingSemanticOperation.operationObligations[0].operations.pop();
assert.throws(() => validateCoverage(missingSemanticOperation), /lacks a semantic obligation/);

const falseQualification = structuredClone(source);
falseQualification.semanticFamilies[0].status = 'passed';
assert.throws(() => validateCoverage(falseQualification), /cannot claim execution qualification/);

const staleSchema = structuredClone(source);
staleSchema.schemaVersion = 1;
assert.throws(() => validateCoverage(staleSchema), /unsupported CIL coverage inventory schema/);

const staleMethod = structuredClone(source);
staleMethod.semanticFamilies[0].existingEvidence[0] =
  'tests/NetWasm.Compiler.Tests/Correctness/OracleComparerTests.cs#DeletedSemanticCase';
assert.throws(() => validateCoverage(staleMethod), /coverage evidence method is missing/);

process.stdout.write('CIL coverage audit tests PASS 10/10\n');

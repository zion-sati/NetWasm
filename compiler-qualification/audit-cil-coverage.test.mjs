#!/usr/bin/env node
// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseDecoder, parseOperations, validateCoverage } from './audit-cil-coverage.mjs';

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
delete missingEvidence.families.CallsAndCallableLoading.semantic;
assert.throws(
  () => validateCoverage(missingEvidence),
  /CallsAndCallableLoading lacks semantic evidence/);

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

// Every outcome of an opcode counts: calli decodes to a stub call or to an indirect call.
assert.deepEqual(parseDecoder(`
            case "calli":
                if (stub is { } callStub)
                {
                    operation = CilOperation.Call;
                    operand = new CilOperand.MethodInstance(callStub);
                    break;
                }
                operation = CilOperation.CallIndirect;
                operand = _callSiteSignatures.Resolve(metadata, token);
                break;
            case "ret": operation = CilOperation.Return; operand = new CilOperand.None(); break;
            default:`), [
  { opcode: 'calli', operation: 'Call', operand: 'MethodInstance' },
  { opcode: 'calli', operation: 'CallIndirect', operand: 'CallSite' },
  { opcode: 'ret', operation: 'Return', operand: 'None' },
]);

// Comments in the operation enum, line or block, are not operations.
assert.deepEqual(parseOperations(`public enum CilOperation
{
    Nop,
    // value-if-true, value-if-false, condition -> one of the values, as Wasm's
    // select, without branching.
    Select, /* trailing, note */
}`), ['Nop', 'Select']);

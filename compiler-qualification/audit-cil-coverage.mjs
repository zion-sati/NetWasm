#!/usr/bin/env node

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateSemanticCoverage } from './semantic-coverage-validator.mjs';

const repository = resolve(fileURLToPath(new URL('..', import.meta.url)));
const csharpKeywords = [
  'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char',
  'checked', 'class', 'const', 'continue', 'decimal', 'default', 'delegate',
  'do', 'double', 'else', 'enum', 'event', 'explicit', 'extern', 'false',
  'finally', 'fixed', 'float', 'for', 'foreach', 'goto', 'if', 'implicit',
  'in', 'int', 'interface', 'internal', 'is', 'lock', 'long', 'namespace',
  'new', 'null', 'object', 'operator', 'out', 'override', 'params', 'private',
  'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed',
  'short', 'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch',
  'this', 'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked',
  'unsafe', 'ushort', 'using', 'virtual', 'void', 'volatile', 'while',
  'add', 'alias', 'allows', 'and', 'args', 'ascending', 'async', 'await',
  'by', 'closed', 'descending', 'dynamic', 'equals', 'extension', 'field',
  'file', 'from', 'get', 'global', 'group', 'init', 'into', 'join', 'let',
  'managed', 'nameof', 'nint', 'not', 'notnull', 'nuint', 'on', 'or',
  'orderby', 'partial', 'record', 'remove', 'required', 'safe', 'scoped',
  'select', 'set', 'unmanaged', 'value', 'var', 'when', 'where', 'with',
  'yield',
];

function read(path) {
  return readFileSync(resolve(repository, path), 'utf8');
}

function parseOperations(model) {
  const match = model.match(/public enum CilOperation\s*\{([\s\S]*?)\n\}/);
  if (!match) {
    throw new Error('CilOperation enum was not found');
  }
  return match[1].split(',').map(value => value.trim()).filter(Boolean);
}

function parseFamilies(source) {
  const result = new Map();
  const expression = /new\(\s*InstructionFamily\.(\w+),\s*\[([\s\S]*?)\]\)/g;
  for (const match of source.matchAll(expression)) {
    const operations = [...match[2].matchAll(/CilOperation\.(\w+)/g)]
      .map(item => item[1]);
    result.set(match[1], operations);
  }
  return result;
}

function parseDecoder(source) {
  const cases = [];
  const expression = /case "([^"]+)":([\s\S]*?)(?=\n\s*case "|\n\s*default:)/g;
  for (const match of source.matchAll(expression)) {
    if (match[2].includes('ReadRuntimeHandle(')) {
      cases.push(
        { opcode: match[1], operation: 'LoadTypeToken', operand: 'TypeIdentity' },
        { opcode: match[1], operation: 'LoadFieldToken', operand: 'Entity' });
      continue;
    }
    const operation = match[2].match(/operation\s*=\s*CilOperation\.(\w+)/)?.[1];
    if (!operation) {
      throw new Error(`decoder opcode ${match[1]} has no CilOperation`);
    }
    const operand = match[2].match(/(?:new\s+)?CilOperand\.(\w+)/)?.[1] ??
      (match[2].includes('Conversion(') ? 'NumericConversion' : null) ??
      (match[2].includes('ReadMethod(') ? 'Entity|MethodInstance' : null) ??
      (match[2].includes('ReadField(') ? 'Entity|FieldInstance' : null) ??
      (match[2].includes('ReadType(') ||
       match[2].includes('ReadSignatureType(') ||
       match[2].includes('PrimitiveArrayElement(') ? 'TypeIdentity' : null) ??
      (match[2].includes('ReadBranch(') ? 'BranchTarget' : null) ??
      (match[2].includes('ReadSwitch(') ? 'SwitchTargets' : null) ??
      (match[2].includes('ReadUserString(') ? 'UserString' : null);
    if (!operand) {
      throw new Error(`decoder opcode ${match[1]} has no identifiable operand form`);
    }
    cases.push({ opcode: match[1], operation, operand });
  }
  return cases;
}

function verifyEvidence(reference) {
  const [path, method] = reference.split('#');
  const source = read(path);
  if (method && !new RegExp(`\\b${method}\\s*\\(`).test(source)) {
    throw new Error(`coverage evidence method is missing: ${reference}`);
  }
}

export function validateCoverage(inventory) {
  const operations = parseOperations(read('src/NetWasm.Compiler.Core/CilModel.cs'));
  const operationSet = new Set(operations);
  // The catalog is now a test-owned classification, not a production factory.
  // Live provider completeness is asserted by the module-composition test.
  const families = parseFamilies(read(
    'tests/NetWasm.Compiler.Wasm.Tests/Support/InstructionFamilyCatalogFactory.cs'));
  verifyEvidence('tests/NetWasm.Compiler.Wasm.Tests/WasmModuleServiceCollectionExtensionsTests.cs#ComposesTheCompleteTargetFreeModuleGraph');
  const decoded = parseDecoder(read('src/NetWasm.Compiler.Metadata/CilDecoder.cs'));
  const opcodePolicy = JSON.parse(read(
    'compiler-qualification/cil-opcode-policy.json'));
  const decodedOperations = new Set(decoded.map(item => item.operation));
  const lowered = new Set(inventory.internalLoweredOperations);

  if (inventory.schemaVersion !== 2) {
    throw new Error('unsupported CIL coverage inventory schema');
  }
  if (opcodePolicy.schemaVersion !== 1) {
    throw new Error('unsupported CIL opcode policy schema');
  }
  const decodedOpcodeNames = new Set(decoded.map(item => item.opcode));
  const unsupportedOpcodeNames = opcodePolicy.unsupported.map(item => item.opcode);
  if (new Set(unsupportedOpcodeNames).size !== unsupportedOpcodeNames.length) {
    throw new Error('unsupported CIL opcode policy contains duplicates');
  }
  for (const item of opcodePolicy.unsupported) {
    if (!item.opcode || !item.category || !item.reason) {
      throw new Error('unsupported CIL opcode lacks opcode, category, or reason');
    }
    if (decodedOpcodeNames.has(item.opcode)) {
      throw new Error(`CIL opcode is both decoded and unsupported: ${item.opcode}`);
    }
  }
  if (decodedOpcodeNames.size !== 212 || unsupportedOpcodeNames.length !== 14) {
    throw new Error('CIL opcode universe count changed; update explicit policy');
  }
  read('tests/NetWasm.Compiler.Metadata.Tests/CilOpcodeUniverseTests.cs');
  if (new Set(inventory.internalLoweredOperations).size !==
      inventory.internalLoweredOperations.length) {
    throw new Error('internal lowered operation is duplicated');
  }

  const familyOperations = new Set();
  for (const [family, members] of families) {
    const evidence = inventory.families[family];
    if (!evidence) {
      throw new Error(`instruction family lacks coverage evidence: ${family}`);
    }
    for (const kind of ['decoder', 'validator', 'emitter', 'semantic']) {
      if (typeof evidence[kind] !== 'string') {
        throw new Error(`${family} lacks ${kind} evidence`);
      }
      verifyEvidence(evidence[kind]);
    }
    for (const operation of members) {
      if (familyOperations.has(operation)) {
        throw new Error(`operation belongs to multiple families: ${operation}`);
      }
      familyOperations.add(operation);
    }
  }
  const unexpectedFamily = Object.keys(inventory.families)
    .filter(family => !families.has(family));
  if (unexpectedFamily.length > 0) {
    throw new Error(`unknown instruction families: ${unexpectedFamily.join(', ')}`);
  }

  for (const operation of operations) {
    if (!familyOperations.has(operation)) {
      throw new Error(`accepted operation lacks an instruction family: ${operation}`);
    }
    const origins = Number(decodedOperations.has(operation)) +
      Number(lowered.has(operation));
    if (origins !== 1) {
      throw new Error(`operation must have exactly one accepted origin: ${operation}`);
    }
  }
  for (const operation of [...decodedOperations, ...lowered]) {
    if (!operationSet.has(operation)) {
      throw new Error(`coverage names an unknown operation: ${operation}`);
    }
  }

  const operandTypes = new Set([
    ...read('src/NetWasm.Compiler.Core/CilModel.cs')
      .matchAll(/public sealed record (\w+)(?:\([^;]*\))?\s*:\s*CilOperand/g),
  ].map(match => match[1]));
  for (const item of decoded) {
    if (item.operand.split('|').some(operand => !operandTypes.has(operand))) {
      throw new Error(
        `opcode ${item.opcode} uses unknown operand form ${item.operand}`);
    }
  }

  for (const rule of [...inventory.cfgRules, ...inventory.stackRules]) {
    if (!rule.id || !rule.evidence) {
      throw new Error('CFG/stack rule lacks an ID or evidence');
    }
    verifyEvidence(rule.evidence);
  }
  if (new Set(inventory.cfgRules.map(rule => rule.id)).size !==
      inventory.cfgRules.length ||
      new Set(inventory.stackRules.map(rule => rule.id)).size !==
      inventory.stackRules.length) {
    throw new Error('CFG/stack rule ID is duplicated');
  }

  const languageInventory = read('tests/end-to-end/corelib/CSHARP_LANGUAGE_SUPPORT.md');
  const missingKeywords = csharpKeywords.filter(
    keyword => !languageInventory.includes(`\`${keyword}\``));
  if (missingKeywords.length > 0) {
    throw new Error(`C# keywords lack classification: ${missingKeywords.join(', ')}`);
  }
  for (const suite of [
    'StatementCompilationTests.cs',
    'ControlTransferCompilationTests.cs',
    'PatternCompilationTests.cs',
    'ModernLanguageCompilationTests.cs',
    'LanguageSurfaceCompilationTests.cs',
    'LambdaCompilationTests.cs',
    'IteratorCompilationTests.cs',
    'AsyncCompilationTests.cs',
    'ValueTaskCompilationTests.cs',
    'LockCompilationTests.cs',
    'UnsupportedBoundaryCompilationTests.cs',
  ]) {
    read(`tests/NetWasm.Compiler.Tests/${suite}`);
  }

  const evidenceFiles = new Set();
  for (const family of inventory.semanticFamilies ?? []) {
    for (const path of family.existingEvidence ?? []) {
      verifyEvidence(path);
      evidenceFiles.add(path);
    }
  }
  const semantic = validateSemanticCoverage(inventory, operations, evidenceFiles);
  return {
    ...semantic,
    operationCount: operations.length,
    opcodeCount: decodedOpcodeNames.size,
    opcodeMappingCount: decoded.length,
    unsupportedOpcodeCount: unsupportedOpcodeNames.length,
    runtimeOpcodeCount: decodedOpcodeNames.size + unsupportedOpcodeNames.length,
    operandFormCount: new Set(decoded.map(item => item.operand)).size,
    familyCount: families.size,
    keywordCount: csharpKeywords.length,
  };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const path = process.argv[2] ??
    'compiler-qualification/cil-coverage-inventory.json';
  const result = validateCoverage(JSON.parse(read(path)));
  process.stdout.write(
    `CIL coverage inventory PASS ${result.operationCount} operations, ` +
    `${result.opcodeCount}/${result.runtimeOpcodeCount} supported opcodes, ` +
    `${result.unsupportedOpcodeCount} explicitly unsupported, ` +
    `${result.operandFormCount} operand forms, ` +
    `${result.familyCount} instruction families, ${result.keywordCount} C# keywords; ` +
    `${result.semanticFamilyCount} unqualified semantic families, ` +
    `${result.semanticOperationCount} explicitly mapped operations\n`);
}

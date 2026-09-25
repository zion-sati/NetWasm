#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { CompilerCaseRunCommand, CompilerCaseSelectionError, parseCompilerCaseOptions, planCompilerCaseRun } from './compiler-cases.mjs';
import {
  createCompilerCaseArtifacts, executeCompilerCaseTests, readCompilerCaseManifests,
  readCompilerCaseResults, writeCompilerCaseSummary,
} from './compiler-case-boundaries.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
try {
  const options = parseCompilerCaseOptions(process.argv.slice(2));
  if (options.help) {
    console.log('Usage: node eng/run-compiler-cases.mjs [--profile fast|family|extended] [--case ID | --family Sxx] [--list] [--artifacts NEW_ABSOLUTE_DIRECTORY]');
    console.log('Default profile: fast. Build src/NetWasm.CoreLib/NetWasm.CoreLib.csproj -c Release before source-based execution. --list previews selection only.');
    console.log('Selection covers registered corpus cases, not every legacy test or full semantic-family qualification.');
  } else {
    const inventory = JSON.parse(fs.readFileSync(path.join(repository, 'compiler-qualification/cil-coverage-inventory.json'), 'utf8'));
    const manifests = readCompilerCaseManifests(path.join(repository, 'tests/NetWasm.Compiler.Tests/Correctness/Fixtures'));
    const plan = planCompilerCaseRun(options, manifests, inventory.semanticFamilies.map(item => item.id));
    if (options.list) {
      console.log(JSON.stringify({ mode: 'selection-preview', ...plan }, null, 2));
    } else {
      const directory = createCompilerCaseArtifacts(options.artifacts);
      console.log(`Corpus profile ${plan.profile}; ${plan.cases.length} case(s); artifacts: ${directory}`);
      const command = new CompilerCaseRunCommand(
        (selection, artifacts) => executeCompilerCaseTests(repository, selection, artifacts, spawnSync),
        readCompilerCaseResults, writeCompilerCaseSummary);
      const summary = command.run(plan, directory);
      console.log(JSON.stringify({ exitCode: summary.exitCode, verification: summary.verification,
        passed: summary.results?.passed ?? 0, failed: summary.results?.failed ?? 0, skipped: summary.results?.skipped ?? 0 }));
      process.exitCode = summary.exitCode;
    }
  }
} catch (error) {
  // Framework/compiler/engine diagnostics stay in the owned artifact directory.
  console.error(error instanceof CompilerCaseSelectionError ? error.message :
    'Compiler-case command failed before a complete result; no retry was attempted. Use --help for usage.');
  process.exitCode = 1;
}

import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export function readCompilerCaseManifests(directory) {
  const manifests = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      manifests.push(...readCompilerCaseManifests(file));
    } else if (entry.name.endsWith('.case.json')) {
      if (!entry.isFile()) throw new Error('Case manifests must be regular files.');
      manifests.push(JSON.parse(fs.readFileSync(file, 'utf8')));
    }
  }
  return manifests;
}

export function createCompilerCaseArtifacts(directory) {
  if (directory === null) return fs.mkdtempSync(path.join(os.tmpdir(), 'netwasm-compiler-cases-'));
  if (!path.isAbsolute(directory)) throw new Error('Artifact directory must be absolute.');
  fs.mkdirSync(directory);
  return directory;
}

export function executeCompilerCaseTests(repository, plan, directory, execute) {
  const output = fs.openSync(path.join(directory, 'test.stdout'), 'wx');
  try {
    const error = fs.openSync(path.join(directory, 'test.stderr'), 'wx');
    try {
      const result = execute('dotnet', [
        'test', 'tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj', '-c', 'Release',
        '--filter', plan.filter, '--logger', 'trx;LogFileName=cases.trx', '--results-directory', directory,
      ], { cwd: repository, env: { ...process.env, NETWASM_CORPUS_PROFILE: plan.profile }, stdio: ['ignore', output, error] });
      return { status: result.status, signal: result.signal, errorCode: result.error?.code ?? null };
    } finally {
      fs.closeSync(error);
    }
  } finally {
    fs.closeSync(output);
  }
}

export function readCompilerCaseResults(directory) {
  return fs.readFileSync(path.join(directory, 'cases.trx'), 'utf8');
}

export function writeCompilerCaseSummary(directory, summary) {
  fs.writeFileSync(path.join(directory, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
}

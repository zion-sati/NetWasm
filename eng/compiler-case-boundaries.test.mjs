import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  createCompilerCaseArtifacts, executeCompilerCaseTests, readCompilerCaseManifests,
  readCompilerCaseResults, writeCompilerCaseSummary,
} from './compiler-case-boundaries.mjs';

function withDirectory(action) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'netwasm-case-boundary-test-'));
  try { return action(directory); }
  finally { fs.rmSync(directory, { recursive: true }); }
}

test('manifest adapter visits nested files without treating source or unrelated files as manifests', () => withDirectory(directory => {
  fs.mkdirSync(path.join(directory, 'nested'));
  fs.writeFileSync(path.join(directory, 'first.case.json'), '{"caseId":"first"}');
  fs.writeFileSync(path.join(directory, 'nested', 'second.case.json'), '{"caseId":"second"}');
  fs.writeFileSync(path.join(directory, 'Entry.cs.txt'), 'source');
  assert.deepEqual(readCompilerCaseManifests(directory).map(item => item.caseId).sort(), ['first', 'second']);
}));

test('manifest adapter rejects symlinked and malformed declarations', () => withDirectory(directory => {
  const real = path.join(directory, 'real.json');
  fs.writeFileSync(real, '{}');
  const linked = path.join(directory, 'linked.case.json');
  fs.symlinkSync(real, linked);
  assert.throws(() => readCompilerCaseManifests(directory), /regular files/);
  fs.unlinkSync(linked);
  fs.writeFileSync(path.join(directory, 'malformed.case.json'), 'invalid');
  assert.throws(() => readCompilerCaseManifests(directory), SyntaxError);
}));

test('explicit artifact roots must be new and absolute', () => withDirectory(parent => {
  const directory = path.join(parent, 'owned');
  assert.equal(createCompilerCaseArtifacts(directory), directory);
  assert.ok(fs.statSync(directory).isDirectory());
  assert.throws(() => createCompilerCaseArtifacts(directory), { code: 'EEXIST' });
  assert.throws(() => createCompilerCaseArtifacts('relative'), /absolute/);
}));

test('default artifact allocation creates distinct owned roots', () => {
  const first = createCompilerCaseArtifacts(null);
  const second = createCompilerCaseArtifacts(null);
  try {
    assert.notEqual(first, second);
    assert.ok(fs.statSync(first).isDirectory());
    assert.ok(fs.statSync(second).isDirectory());
  } finally {
    fs.rmdirSync(first);
    fs.rmdirSync(second);
  }
});

for (const launchFailure of [false, true]) {
  test(`process adapter preserves exact invocation, environment and first status: launchFailure=${launchFailure}`, () => withDirectory(directory => {
    let calls = 0;
    let descriptors;
    const plan = { profile: 'Family', filter: '(FullyQualifiedName=Tests.Run&DisplayName~case.id)' };
    const priorProfile = process.env.NETWASM_CORPUS_PROFILE;
    const result = executeCompilerCaseTests('/repo', plan, directory, (command, args, options) => {
      calls++;
      assert.equal(command, 'dotnet');
      assert.deepEqual(args, ['test', 'tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj', '-c', 'Release',
        '--filter', plan.filter, '--logger', 'trx;LogFileName=cases.trx', '--results-directory', directory]);
      assert.equal(options.cwd, '/repo');
      assert.notEqual(options.env, process.env);
      assert.equal(options.env.NETWASM_CORPUS_PROFILE, 'Family');
      assert.equal(options.env.PATH, process.env.PATH);
      assert.equal(options.stdio[0], 'ignore');
      descriptors = options.stdio.slice(1);
      fs.writeSync(descriptors[0], 'first stdout');
      fs.writeSync(descriptors[1], 'first stderr');
      return launchFailure ? { status: null, signal: null, error: { code: 'ENOENT' } } : { status: 7, signal: null };
    });
    assert.deepEqual(result, launchFailure ? { status: null, signal: null, errorCode: 'ENOENT' } : { status: 7, signal: null, errorCode: null });
    assert.equal(calls, 1);
    assert.equal(process.env.NETWASM_CORPUS_PROFILE, priorProfile);
    assert.equal(fs.readFileSync(path.join(directory, 'test.stdout'), 'utf8'), 'first stdout');
    assert.equal(fs.readFileSync(path.join(directory, 'test.stderr'), 'utf8'), 'first stderr');
    for (const descriptor of descriptors) assert.throws(() => fs.fstatSync(descriptor), { code: 'EBADF' });
    assert.throws(() => executeCompilerCaseTests('/repo', plan, directory, () => assert.fail()), { code: 'EEXIST' });
  }));
}

test('process adapter closes handles when the executor throws and retains the cause', () => withDirectory(directory => {
  const cause = new Error('launch boundary failed');
  let descriptors;
  assert.throws(() => executeCompilerCaseTests('/repo', { profile: 'Fast', filter: 'filter' }, directory, (command, args, options) => {
    descriptors = options.stdio.slice(1);
    throw cause;
  }), error => error === cause);
  for (const descriptor of descriptors) assert.throws(() => fs.fstatSync(descriptor), { code: 'EBADF' });
}));

test('result adapter preserves file contents and refuses to overwrite a summary', () => withDirectory(directory => {
  assert.throws(() => readCompilerCaseResults(directory), { code: 'ENOENT' });
  fs.writeFileSync(path.join(directory, 'cases.trx'), 'test result\r\n');
  assert.equal(readCompilerCaseResults(directory), 'test result\r\n');
  const summary = { exitCode: 1, verification: 'incomplete' };
  writeCompilerCaseSummary(directory, summary);
  assert.deepEqual(JSON.parse(fs.readFileSync(path.join(directory, 'summary.json'), 'utf8')), summary);
  assert.throws(() => writeCompilerCaseSummary(directory, { exitCode: 0 }), { code: 'EEXIST' });
  assert.deepEqual(JSON.parse(fs.readFileSync(path.join(directory, 'summary.json'), 'utf8')), summary);
}));

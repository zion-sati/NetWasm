import assert from 'node:assert/strict';
import test from 'node:test';
import {
  CompilerCaseRunCommand, CompilerCaseSelectionError, parseCompilerCaseOptions,
  planCompilerCaseRun, verifyCompilerCaseResults,
} from './compiler-cases.mjs';

const first = { schemaVersion: 1, caseId: 'numeric.first', testMethod: 'Tests.Numeric.First', featureIds: ['S06'] };
const second = { schemaVersion: 1, caseId: 'numeric.second', testMethod: 'Tests.Numeric.Second', featureIds: ['S07'] };
const features = ['S06', 'S07', 'S08'];
const defaults = () => parseCompilerCaseOptions([]);
const plan = () => planCompilerCaseRun(defaults(), [first], features);
const row = ({ id = first.caseId, method = first.testMethod, cell = 'Release-Wasm32-Direct', input = null, outcome = 'Passed' } = {}) =>
  `<UnitTestResult testName="${method}(${input === null ? '' : `input: ${input}, `}caseId: &quot;${id}&quot;, cell: &quot;${cell}&quot;)" outcome="${outcome}" />`;
const trx = (...rows) => `<TestRun id="test"><Results>${rows.join('')}</Results></TestRun>`;

test('options default to Fast and support explicit case, family, list and help requests', () => {
  assert.deepEqual(defaults(), { profile: 'Fast', caseId: null, family: null, artifacts: null, list: false, help: false });
  const selected = parseCompilerCaseOptions(['--profile', 'EXTENDED', '--case', 'numeric.first', '--artifacts', '/tmp/new-run', '--list']);
  assert.deepEqual(selected, { profile: 'Extended', caseId: 'numeric.first', family: null, artifacts: '/tmp/new-run', list: true, help: false });
  assert.ok(Object.isFrozen(selected));
  assert.equal(parseCompilerCaseOptions(['--family', 'S06', '--profile', 'family']).family, 'S06');
  assert.equal(parseCompilerCaseOptions(['--help']).help, true);
});

for (const args of [
  ['--list', '--list'], ['--unknown'], ['--profile'], ['--case', '--list'],
  ['--profile', 'invalid'], ['--profile', '0'], ['--profile', ' Fast'],
  ['--case', 'bad|filter'], ['--case', 'Case'], ['--family', 's06'],
  ['--case', 'numeric.first\n'], ['--family', 'S06\n'],
  ['--family', 'S06', '--case', 'numeric.first'],
]) {
  test(`invalid options fail: ${JSON.stringify(args)}`, () => {
    assert.throws(() => parseCompilerCaseOptions(args), CompilerCaseSelectionError);
  });
}

test('planning orders stable IDs, builds exact filters and freezes the selection snapshot', () => {
  const result = planCompilerCaseRun(defaults(), [second, first], features);
  assert.deepEqual(result.cases.map(item => item.caseId), ['numeric.first', 'numeric.second']);
  assert.equal(result.profile, 'Fast');
  assert.equal(result.scope, 'registered-corpus-cases');
  assert.equal(result.filter, '(FullyQualifiedName=Tests.Numeric.First&DisplayName~numeric.first)|(FullyQualifiedName=Tests.Numeric.Second&DisplayName~numeric.second)');
  assert.deepEqual(planCompilerCaseRun(defaults(), [first, second], features), result);
  assert.ok(Object.isFrozen(result));
  assert.ok(Object.isFrozen(result.cases));
  assert.ok(Object.isFrozen(result.cases[0]));
  assert.ok(Object.isFrozen(result.cases[0].featureIds));
  assert.notEqual(result.cases[0].featureIds, first.featureIds);
});

test('case and family selection do not include adjacent cases', () => {
  assert.deepEqual(planCompilerCaseRun(parseCompilerCaseOptions(['--case', second.caseId]), [first, second], features).cases.map(item => item.caseId), [second.caseId]);
  assert.deepEqual(planCompilerCaseRun(parseCompilerCaseOptions(['--family', 'S06']), [first, second], features).cases.map(item => item.caseId), [first.caseId]);
});

for (const patch of [
  { schemaVersion: 99 }, { caseId: null }, { caseId: 'bad|filter' },
  { caseId: 'numeric.first\n' }, { testMethod: 'Tests.Numeric.First\n' },
  { testMethod: null }, { testMethod: 'Tests.Run|Other' }, { featureIds: null },
  { featureIds: [] }, { featureIds: ['S06', 'S06'] }, { featureIds: ['S99'] },
]) {
  test(`invalid selection metadata fails: ${JSON.stringify(patch)}`, () => {
    assert.throws(() => planCompilerCaseRun(defaults(), [{ ...first, ...patch }], features), CompilerCaseSelectionError);
  });
}

test('unknown, empty and ambiguous selections fail rather than running zero tests', () => {
  assert.throws(() => planCompilerCaseRun(defaults(), [first], []), /feature inventory/);
  assert.throws(() => planCompilerCaseRun(defaults(), [first], ['S06', 'S06']), /feature inventory/);
  assert.throws(() => planCompilerCaseRun(defaults(), [first, first], features), /Duplicate case/);
  assert.throws(() => planCompilerCaseRun(parseCompilerCaseOptions(['--family', 'S99']), [first], features), /Unknown semantic family/);
  assert.throws(() => planCompilerCaseRun(parseCompilerCaseOptions(['--family', 'S08']), [first], features), /No registered cases/);
  assert.throws(() => planCompilerCaseRun(parseCompilerCaseOptions(['--case', 'missing.case']), [first], features), /No registered cases/);
  assert.throws(() => planCompilerCaseRun(defaults(), [], features), /No registered cases/);
});

test('result rows retain status and exact case, cell and input without inspecting diagnostics', () => {
  const result = verifyCompilerCaseResults(plan(), trx(
    row(), row({ input: '-2147483648', outcome: 'Failed' }), row({ cell: 'Release-Wasm64-Direct', outcome: 'NotExecuted' }),
  ));
  assert.deepEqual(result.rows, [
    { caseId: first.caseId, cell: 'Release-Wasm32-Direct', input: null, outcome: 'Passed' },
    { caseId: first.caseId, cell: 'Release-Wasm32-Direct', input: '-2147483648', outcome: 'Failed' },
    { caseId: first.caseId, cell: 'Release-Wasm64-Direct', input: null, outcome: 'NotExecuted' },
  ]);
  assert.equal(result.passed, 1);
  assert.equal(result.failed, 1);
  assert.equal(result.skipped, 1);
  assert.ok(Object.isFrozen(result.rows[0]));
});

for (const document of [
  null, '', '<TestRun id="test">', '</TestRun>', '<TestRun id="test"><!DOCTYPE x></TestRun>',
  '<TestRun id="test"><![CDATA[content]]></TestRun>', trx(),
  trx('<UnitTestResult outcome="Passed" />'),
  trx(row({ outcome: 'Unexpected' })),
  trx(row().replace('outcome="Passed"', '')),
  trx(row({ id: 'unrequested.case' })),
  trx(row({ method: 'Tests.Other.Run' })),
  trx(row().replace('caseId:', 'notACase:')),
  trx(row().replace('cell:', 'notACell:')),
  trx(row(), row()),
]) {
  test(`malformed or incomplete result is not success: ${String(document)}`, () => {
    assert.throws(() => verifyCompilerCaseResults(plan(), document), CompilerCaseSelectionError);
  });
}

test('every requested case must appear, not just a positive total', () => {
  const selection = planCompilerCaseRun(defaults(), [first, second], features);
  assert.throws(() => verifyCompilerCaseResults(selection, trx(row())), /no result rows/);
  assert.equal(verifyCompilerCaseResults(selection, trx(row(), row({ id: second.caseId, method: second.testMethod }))).passed, 2);
});

test('command preserves successful execution and writes one immutable summary', () => {
  const calls = [];
  const selection = plan();
  const command = new CompilerCaseRunCommand(
    (actual, directory) => { assert.equal(actual, selection); assert.equal(directory, '/run'); calls.push('execute'); return { status: 0, signal: null, errorCode: null }; },
    directory => { assert.equal(directory, '/run'); calls.push('read'); return trx(row()); },
    (directory, summary) => { assert.equal(directory, '/run'); assert.equal(summary.exitCode, 0); calls.push('write'); },
  );
  const summary = command.run(selection, '/run');
  assert.equal(summary.childStatus, 0);
  assert.equal(summary.verification, 'case-rows-verified');
  assert.equal(summary.verificationError, null);
  assert.equal(summary.results.passed, 1);
  assert.ok(Object.isFrozen(summary));
  assert.deepEqual(calls, ['execute', 'read', 'write']);
});

for (const [status, signal, document, expectedExit, expectedVerification] of [
  [7, null, trx(row({ outcome: 'Failed' })), 7, 'case-rows-verified'],
  [0, null, trx(row({ outcome: 'Failed' })), 1, 'case-rows-verified'],
  [0, null, trx(row({ outcome: 'NotExecuted' })), 1, 'case-rows-verified'],
  [0, null, trx(), 1, 'incomplete'],
  [7, null, trx(), 7, 'incomplete'],
  [null, 'SIGSEGV', trx(), 1, 'incomplete'],
]) {
  test(`command preserves failed, skipped and missing execution: ${status}/${signal}/${expectedVerification}`, () => {
    let executions = 0;
    let written;
    const command = new CompilerCaseRunCommand(
      () => { executions++; return { status, signal, errorCode: null }; },
      () => document,
      (directory, summary) => { written = summary; },
    );
    const result = command.run(plan(), '/run');
    assert.equal(result.exitCode, expectedExit);
    assert.equal(result.childStatus, status);
    assert.equal(result.childSignal, signal);
    assert.equal(result.verification, expectedVerification);
    assert.equal(result, written);
    assert.equal(executions, 1);
  });
}

test('missing result artifact preserves its cause and the original launch status', () => {
  const cause = new Error('result unavailable');
  const command = new CompilerCaseRunCommand(
    () => ({ status: null, signal: null, errorCode: 'ENOENT' }),
    () => { throw cause; },
    () => {},
  );
  const result = command.run(plan(), '/run');
  assert.equal(result.exitCode, 1);
  assert.equal(result.launchErrorCode, 'ENOENT');
  assert.equal(result.verificationError.message, cause.message);
  assert.equal(result.verificationError.stack, cause.stack);
});

test('execution and summary persistence failures are not swallowed or retried', () => {
  const cause = new Error('boundary failure');
  let reads = 0;
  const firstFailure = new CompilerCaseRunCommand(() => { throw cause; }, () => { reads++; }, () => assert.fail());
  assert.throws(() => firstFailure.run(plan(), '/run'), error => error === cause);
  assert.equal(reads, 0);
  const persistenceFailure = new CompilerCaseRunCommand(() => ({ status: 0, signal: null }), () => trx(row()), () => { throw cause; });
  assert.throws(() => persistenceFailure.run(plan(), '/run'), error => error === cause);
});

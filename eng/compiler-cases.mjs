const profiles = new Map([['fast', 'Fast'], ['family', 'Family'], ['extended', 'Extended']]);
// Unlike $, the final assertion cannot match before a trailing line terminator.
const caseIdPattern = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*(?![\s\S])/;
const methodPattern = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+(?![\s\S])/;

export class CompilerCaseSelectionError extends Error {}

export function parseCompilerCaseOptions(args) {
  const options = { profile: 'Fast', caseId: null, family: null, artifacts: null, list: false, help: false };
  const seen = new Set();
  const values = new Map([['--profile', 'profile'], ['--case', 'caseId'], ['--family', 'family'], ['--artifacts', 'artifacts']]);
  for (let index = 0; index < args.length; index++) {
    const flag = args[index];
    if (seen.has(flag)) throw new CompilerCaseSelectionError('Duplicate command option.');
    seen.add(flag);
    if (flag === '--list' || flag === '--help') {
      options[flag.slice(2)] = true;
      continue;
    }
    const property = values.get(flag);
    if (!property) throw new CompilerCaseSelectionError('Unknown command option.');
    const value = args[++index];
    if (!value || value.startsWith('--')) throw new CompilerCaseSelectionError('Command option requires a value.');
    options[property] = value;
  }
  options.profile = profiles.get(options.profile.toLowerCase());
  if (!options.profile) throw new CompilerCaseSelectionError('Profile must be fast, family or extended.');
  if (options.caseId !== null && !caseIdPattern.test(options.caseId)) throw new CompilerCaseSelectionError('Invalid case ID.');
  if (options.family !== null && !/^S\d{2}(?![\s\S])/.test(options.family)) throw new CompilerCaseSelectionError('Invalid family ID.');
  if (options.caseId !== null && options.family !== null) throw new CompilerCaseSelectionError('Select a case or family, not both.');
  return Object.freeze(options);
}

export function planCompilerCaseRun(options, manifests, featureIds) {
  const knownFeatures = new Set(featureIds);
  if (knownFeatures.size !== featureIds.length || knownFeatures.size === 0) throw new CompilerCaseSelectionError('Invalid feature inventory.');
  const ids = new Set();
  for (const manifest of manifests) {
    if (manifest.schemaVersion !== 1 || typeof manifest.caseId !== 'string' || !caseIdPattern.test(manifest.caseId) ||
        typeof manifest.testMethod !== 'string' || !methodPattern.test(manifest.testMethod) ||
        !Array.isArray(manifest.featureIds) || manifest.featureIds.length === 0 ||
        new Set(manifest.featureIds).size !== manifest.featureIds.length ||
        manifest.featureIds.some(id => !knownFeatures.has(id))) {
      throw new CompilerCaseSelectionError('Invalid case selection metadata.');
    }
    if (ids.has(manifest.caseId)) throw new CompilerCaseSelectionError('Duplicate case ID.');
    ids.add(manifest.caseId);
  }
  if (options.family !== null && !knownFeatures.has(options.family)) throw new CompilerCaseSelectionError('Unknown semantic family.');
  const selected = manifests.filter(item =>
    (options.caseId === null || item.caseId === options.caseId) &&
    (options.family === null || item.featureIds.includes(options.family)))
    .sort((left, right) => left.caseId.localeCompare(right.caseId, 'en'));
  if (selected.length === 0) throw new CompilerCaseSelectionError('No registered cases match the selection.');
  const cases = selected.map(item => Object.freeze({
    caseId: item.caseId,
    testMethod: item.testMethod,
    featureIds: Object.freeze([...item.featureIds]),
  }));
  return Object.freeze({
    schemaVersion: 1,
    scope: 'registered-corpus-cases',
    profile: options.profile,
    cases: Object.freeze(cases),
    // Do not embed literal quotes: dotnet test forwards this through MSBuild.
    // The result verifier checks exact case identity and rejects prefix spill.
    filter: cases.map(item => `(FullyQualifiedName=${item.testMethod}&DisplayName~${item.caseId})`).join('|'),
  });
}

// Adapter for the ordinary VSTest TRX UnitTestResult tags. Compiler output and
// stack traces are deliberately ignored; identifiers use the restricted corpus
// grammar. CDATA/DTD are rejected rather than interpreted as result records.
export function verifyCompilerCaseResults(plan, trx) {
  if (typeof trx !== 'string' || !trx.includes('<TestRun ') || !trx.includes('</TestRun>') ||
      trx.includes('<!DOCTYPE') || trx.includes('<![CDATA[')) throw new CompilerCaseSelectionError('Missing or unsupported TRX document.');
  const cases = new Map(plan.cases.map(item => [item.caseId, item]));
  const observedCases = new Set();
  const identities = new Set();
  const rows = [];
  for (const match of trx.matchAll(/<UnitTestResult\b[^>]*>/g)) {
    const tag = match[0];
    const name = tag.match(/\btestName="([^"]*)"/)?.[1];
    const outcome = tag.match(/\boutcome="([^"]*)"/)?.[1];
    if (!name || !['Passed', 'Failed', 'NotExecuted'].includes(outcome)) throw new CompilerCaseSelectionError('Unsupported TRX result row.');
    const caseId = name.match(/\bcaseId: &quot;([a-z][a-z0-9.-]*)&quot;,/)?.[1];
    const cell = name.match(/\bcell: &quot;([A-Za-z0-9-]+)&quot;/)?.[1];
    const input = name.match(/\binput: (-?\d+),/)?.[1] ?? null;
    const declaration = cases.get(caseId);
    if (!declaration || !name.startsWith(`${declaration.testMethod}(`) || !cell) throw new CompilerCaseSelectionError('Unrequested or unidentified result row.');
    const identity = JSON.stringify([caseId, cell, input]);
    if (identities.has(identity)) throw new CompilerCaseSelectionError('Duplicate result row.');
    identities.add(identity);
    observedCases.add(caseId);
    rows.push(Object.freeze({ caseId, cell, input, outcome }));
  }
  if (observedCases.size !== cases.size) throw new CompilerCaseSelectionError('A requested case has no result rows.');
  return Object.freeze({
    rows: Object.freeze(rows),
    passed: rows.filter(row => row.outcome === 'Passed').length,
    failed: rows.filter(row => row.outcome === 'Failed').length,
    skipped: rows.filter(row => row.outcome === 'NotExecuted').length,
  });
}

export class CompilerCaseRunCommand {
  constructor(execute, readResults, writeSummary) {
    this.execute = execute;
    this.readResults = readResults;
    this.writeSummary = writeSummary;
  }

  run(plan, directory) {
    const process = this.execute(plan, directory);
    let results = null;
    let verification = 'case-rows-verified';
    let verificationError = null;
    try {
      results = verifyCompilerCaseResults(plan, this.readResults(directory));
    } catch (error) {
      verification = 'incomplete';
      verificationError = { message: error.message, stack: error.stack };
    }
    const childExit = process.status ?? 1;
    const exitCode = childExit !== 0 ? childExit :
      verification !== 'case-rows-verified' || results.failed !== 0 || results.skipped !== 0 ? 1 : 0;
    const summary = Object.freeze({
      schemaVersion: 1, plan, childStatus: process.status, childSignal: process.signal,
      launchErrorCode: process.errorCode, verification, verificationError, results, exitCode,
    });
    this.writeSummary(directory, summary);
    return summary;
  }
}

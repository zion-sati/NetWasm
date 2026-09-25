import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, openSync, closeSync, readFileSync, writeFileSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import { cacheMatches, expected, resolveCompilerPath } from './cache-contract.mjs';

const root = resolve(import.meta.dirname, '../../..');
const output = process.argv[2];
if (process.argv.length !== 3 || !isAbsolute(output)) throw Error('absolute-new-artifact-directory-required');
mkdirSync(output);
const host = resolve(root, 'tests/NetWasm.Testing.CompilerHost/bin/Release/net10.0/NetWasm.Testing.CompilerHost.dll');
const corelib = resolve(root, 'src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll');
const oracle = resolve(root, 'tests/NetWasm.Compiler.Tests/Correctness/netwasm-oracle.mjs');
const source = resolve(import.meta.dirname, 'CacheFixture.cs');
const inputs = [-1000, -1, 0, 1, 1000, 2147483647, -2147483648];
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const json = path => JSON.parse(readFileSync(path, 'utf8'));
const save = (path, value) => writeFileSync(path, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const commands = [];
const observations = [];
function run(file, args, label) {
  const out = resolve(output, label + '.stdout');
  const err = resolve(output, label + '.stderr');
  const stdout = openSync(out, 'wx');
  const stderr = openSync(err, 'wx');
  const started = Date.now();
  let result;
  try { result = spawnSync(file, args, { cwd: root, stdio: ['ignore', stdout, stderr] }); }
  finally { closeSync(stdout); closeSync(stderr); }
  commands.push({ label, exit: result.status, signal: result.signal, milliseconds: Date.now() - started,
    stdout: { path: out, sha256: hash(out) }, stderr: { path: err, sha256: hash(err) } });
  if (result.status !== 0) throw Error('command-failed');
  return out;
}
const pinnedVersion = json(resolve(root, 'global.json')).sdk.version;
const sdkList = readFileSync(run('dotnet', ['--list-sdks'], 'sdk-list'), 'utf8');
const csc = resolveCompilerPath(sdkList, pinnedVersion);
const dependencySource = resolve(import.meta.dirname, 'CacheDependency.cs');
const artifacts = [host, corelib, csc, oracle, source, dependencySource, import.meta.filename,
  resolve(import.meta.dirname, 'cache-contract.mjs'), resolve(root, 'global.json')].map(path => ({path, sha256:hash(path)}));
const inputsPath = resolve(output, 'inputs.json');
save(inputsPath, inputs);
try {
  for (const optimized of [false, true]) {
    const profile = optimized ? 'release' : 'debug';
    const assemblies = [];
    const dependencies = [];
    for (const edited of [false, true]) {
      const directory = resolve(output, profile, edited ? 'reference-edited' : 'reference-original');
      mkdirSync(directory, {recursive:true});
      const assembly = resolve(directory, 'CacheDependency.dll');
      run('dotnet', [csc, '-nologo', '-noconfig', '-nostdlib', '-langversion:latest', '-warnaserror+',
        '-nullable:enable', '-deterministic+', optimized ? '-optimize+' : '-optimize-', '-target:library',
        '-runtimemetadataversion:v4.0.30319', '-reference:' + corelib, '-out:' + assembly,
        ...(edited ? ['-define:EDITED'] : []), dependencySource], profile + '-reference-' + (edited ? 'edited' : 'original') + '-csc');
      dependencies.push(assembly);
      artifacts.push({path:assembly,sha256:hash(assembly)});
    }
    for (const edited of [false, true]) {
      const directory = resolve(output, profile, edited ? 'edited' : 'original');
      mkdirSync(directory, { recursive: true });
      const assembly = resolve(directory, 'CacheFixture.dll');
      run('dotnet', [csc, '-nologo', '-noconfig', '-nostdlib', '-langversion:latest', '-warnaserror+',
        '-nullable:enable', '-deterministic+', optimized ? '-optimize+' : '-optimize-', '-target:library',
        '-runtimemetadataversion:v4.0.30319', '-reference:' + corelib, '-reference:' + dependencies[0], '-out:' + assembly,
        ...(edited ? ['-define:EDITED'] : []), source], profile + '-' + (edited ? 'edited' : 'original') + '-csc');
      assemblies.push(assembly);
      artifacts.push({path:assembly, sha256:hash(assembly)});
    }
    for (const target of [0, 1]) for (const workers of [1, 4]) {
      const key = `${profile}-${target}-${workers}`;
      const directory = resolve(output, key);
      mkdirSync(directory);
      const active = resolve(directory, 'CacheFixture.dll');
      const activeDependency = resolve(directory, 'CacheDependency.dll');
      const cache = resolve(output, `cache-${profile}-${workers}`);
      const createRequest = (label, enabled) => ({
        entryAssemblyPath: active, referencePaths: [corelib, activeDependency],
        entryTypeName: 'NetWasm.Correctness.CacheSemantics.EntryPoint', entryMethodName: 'Run',
        exports: [], target, diagnosticTracePath: null, sourcePaths: [], referenceAssemblyAliases: {},
        modulePath: resolve(directory, label + '.wasm'), collectCompilerMetrics: true,
        enableFrontendCache: enabled, intermediateOutputPath: cache,
      });
      for (const phase of ['disabled', 'cold-memory-warm', 'disk-warm', 'edited', 'reverted', 'reference-edited', 'reference-reverted']) {
        const edited = phase === 'edited';
        const referenceEdited = phase === 'reference-edited';
        copyFileSync(assemblies[edited ? 1 : 0], active);
        copyFileSync(dependencies[referenceEdited ? 1 : 0], activeDependency);
        const labels = phase === 'cold-memory-warm' ? ['cold', 'memory-warm'] : [phase];
        const steps = labels.map(label => ({label, request:createRequest(label, phase !== 'disabled'),
          responsePath:resolve(directory, label + '.response.json')}));
        const sequencePath = resolve(directory, phase + '.sequence.json');
        const receiptPath = resolve(directory, phase + '.receipt.json');
        save(sequencePath, {steps});
        run('dotnet', [host, '--workers', String(workers), '--sequence', sequencePath, receiptPath], key + '-' + phase);
        const receipt = json(receiptPath);
        if (receipt.Steps.length !== steps.length || receipt.Steps.some(step => step.ExitCode !== 0))
          throw Error('incomplete-sequence');
        for (const step of steps) {
          const response = json(step.responsePath);
          const execution = run('node', [oracle, step.request.modulePath, '@' + inputsPath,
            target === 0 ? 'wasm32' : 'wasm64', String(response.StaticDataEnd)], key + '-' + step.label + '-execute');
          const actual = json(execution);
          const values = Array.isArray(actual) ? actual : actual.observations;
          if (!Array.isArray(values) || values.length !== inputs.length) throw Error('incomplete-observations');
          const semanticPass = values.every((value, index) => value.kind === 'value' && value.value === expected(inputs[index], edited, referenceEdited));
          const metrics = response.CompilerMetrics?.FrontendCache;
          const cachePass = cacheMatches(step.label, metrics);
          observations.push({profile, target, workers, phase:step.label, edited, referenceEdited, semanticPass, cachePass,
            inputCount:inputs.length, metrics, assemblySha256:hash(active),
            referenceSha256:hash(activeDependency), module:{path:step.request.modulePath,sha256:hash(step.request.modulePath)},
            response:{path:step.responsePath,sha256:hash(step.responsePath)}});
          if (!semanticPass) throw Error('semantic-mismatch');
          if (!cachePass) throw Error('cache-contract-mismatch');
        }
      }
    }
  }
  for (const artifact of artifacts) if (hash(artifact.path) !== artifact.sha256) throw Error('input-changed');
  save(resolve(output, 'observed-results.json'), {schemaVersion:1, artifacts, commands, observations,
    limitations:['Arithmetic host oracle does not prove linked runtime or GC behavior.']});
  console.log(JSON.stringify({status:'PASS',cells:observations.length,observations:observations.length * inputs.length}));
} catch {
  save(resolve(output, 'incomplete-results.json'), {schemaVersion:1, artifacts, commands, observations});
  console.log(JSON.stringify({status:'FAIL',completedCells:observations.length,commands:commands.length}));
  process.exitCode = 1;
}

import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { closeSync, copyFileSync, cpSync, existsSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import { cases, matchesObservations } from './cases.mjs';

const root = resolve(import.meta.dirname, '../../..');
const [output, packages, libraries, selection] = process.argv.slice(2);
if (![output, packages, libraries].every(value => value && isAbsolute(value)) || process.argv.length > 6)
  throw Error('usage: run.mjs ABSOLUTE_NEW_OUTPUT ABSOLUTE_PACKAGE_CACHE ABSOLUTE_LIBRARIES_SOURCE [FAMILY]');
const families = selection ? cases.filter(item => item.id === selection) : cases;
if (!families.length) throw Error('unknown-generator-family');
mkdirSync(output);
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const describe = path => ({ path, sha256: hash(path) });
const json = path => JSON.parse(readFileSync(path, 'utf8'));
const save = (path, value) => writeFileSync(path, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const walk = directory => readdirSync(directory, { withFileTypes: true }).flatMap(item => {
  const path = resolve(directory, item.name);
  return item.isDirectory() ? (['bin', 'obj'].includes(item.name) ? [] : walk(path)) : [path];
});
const compiler = resolve(root, 'tests/NetWasm.Testing.CompilerHost/bin/Release/net10.0/NetWasm.Testing.CompilerHost.dll');
const desktop = resolve(root, 'tests/NetWasm.Testing.OracleHost/bin/Release/net10.0/NetWasm.Testing.OracleHost.dll');
const engine = resolve(root, 'tests/NetWasm.Compiler.Tests/Correctness/netwasm-oracle.mjs');
const corelib = resolve(root, 'src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll');
const cells = [], commands = [], inputs = [];
const record = path => { const identity = describe(path); inputs.push(identity); return identity; };
function run(file, args, label) {
  const stdoutPath = resolve(output, label + '.stdout'), stderrPath = resolve(output, label + '.stderr');
  const stdout = openSync(stdoutPath, 'wx'), stderr = openSync(stderrPath, 'wx');
  let result; const started = Date.now();
  try { result = spawnSync(file, args, { cwd: output, env: { ...process.env, NUGET_PACKAGES: resolve(output, 'desktop-packages') },
    stdio: ['ignore', stdout, stderr] }); }
  finally { closeSync(stdout); closeSync(stderr); }
  commands.push({ label, exit: result.status, signal: result.signal, milliseconds: Date.now() - started,
    stdout: describe(stdoutPath), stderr: describe(stderrPath) });
  return { ok: result.status === 0, path: stdoutPath };
}
function packageDirectory(name) {
  const id = 'netwasm.' + name.toLowerCase(), directory = resolve(packages, id, '0.4.1');
  record(resolve(directory, id + '.0.4.1.nupkg'));
  return directory;
}
const aliases = Object.fromEntries(['System.Private.CoreLib', 'System.Runtime', 'System.Collections',
  'System.Collections.Concurrent', 'System.ObjectModel', 'System.Reflection', 'System.Runtime.InteropServices',
  'System.Text', 'System.Runtime.CompilerServices.Unsafe', 'System.Buffers', 'System.Memory', 'System.ComponentModel',
  'System.Threading', 'System.Threading.Tasks', 'System.Threading.Tasks.Extensions', 'System.Diagnostics.Debug', 'System.Console']
  .map(name => [name, 'NetWasm.CoreLib']));
let incomplete = false;
try {
  copyFileSync(resolve(root, 'global.json'), resolve(output, 'global.json'));
  writeFileSync(resolve(output, 'NuGet.Config'), '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>', { flag: 'wx' });
  for (const path of [import.meta.filename, resolve(import.meta.dirname, 'cases.mjs'), corelib, engine,
    resolve(output, 'global.json'), resolve(output, 'NuGet.Config')]) record(path);
  for (const host of [compiler, desktop])
    for (const path of walk(resolve(host, '..')).filter(path => /\.(dll|json)$/.test(path))) record(path);
  if (families.some(family => family.contracts))
    for (const name of ['DependencyInjection', 'DependencyInjection.Abstractions', 'Configuration.Binder', 'Configuration.Abstractions'])
      for (const path of walk(resolve(libraries, 'src/NetWasm.Microsoft.Extensions.' + name)).filter(path => path.endsWith('.cs'))) record(path);
  for (const family of families) {
    const references = [corelib, ...family.libraries.map(name => {
      const path = resolve(packageDirectory(name), 'lib/NetWasm,Version=v0.1', name + '.dll'); record(path); return path;
    })];
    for (const name of family.analyzerPackages ?? [])
      for (const path of walk(resolve(packageDirectory(name), 'analyzers/dotnet/cs'))) record(path);
    for (const configuration of ['Debug', 'Release']) {
      const key = family.id + '-' + configuration, directory = resolve(output, key);
      const source = resolve(directory, 'fixtures', family.id);
      mkdirSync(source, { recursive: true });
      const original = resolve(root, 'tests/end-to-end', family.folder);
      for (const name of [family.assembly + '.csproj', family.contracts || family.id === 'logging' ? 'EntryPoint.cs' : 'SourceGenerationFixture.cs']) {
        const target = resolve(source, name);
        copyFileSync(resolve(original, name), target); record(target);
      }
      if (family.contracts) {
        const contracts = resolve(directory, 'contracts');
        cpSync(resolve(import.meta.dirname, 'contracts'), contracts, { recursive: true });
        for (const path of walk(contracts)) record(path);
      }
      const inputPath = resolve(directory, 'inputs.json'); save(inputPath, family.values); record(inputPath);
      const build = run('dotnet', ['build', resolve(source, family.assembly + '.csproj'), '-c', configuration,
        '--disable-build-servers', '--nologo', '-m:1', '-p:LibrariesSnapshot=' + libraries, '-p:PortedPackageRoot=' + packages], key + '-build');
      if (!build.ok) { cells.push({ family: family.id, configuration, stage: 'desktop-build', pass: false }); continue; }
      const generatedRoot = resolve(source, 'obj/generated');
      const generated = existsSync(generatedRoot) ? walk(generatedRoot).filter(path => path.endsWith('.cs')) : [];
      if (!generated.length) { cells.push({ family: family.id, configuration, stage: 'generation', pass: false }); continue; }
      generated.forEach(record);
      const bin = resolve(source, 'bin', configuration, 'net10.0'), assembly = resolve(bin, family.assembly + '.dll');
      const identity = record(assembly), localReferences = family.contracts ? [resolve(bin, 'Generator.Contracts.dll')] : [];
      localReferences.forEach(record);
      const args = family.desktop === 'stdout' ? [assembly, ...family.values.map(String)] :
        [desktop, assembly, family.type, 'Run', '@' + inputPath, 'false', ...(family.desktopReferences ?? []).map(name => resolve(bin, name))];
      const oracle = run('dotnet', args, key + '-desktop');
      const observed = oracle.ok ? (family.desktop === 'stdout'
        ? readFileSync(oracle.path, 'utf8').trim().split(/\r?\n/).map(value => ({ Kind: 'value', Value: Number(value) })) : json(oracle.path)) : [];
      const desktopPass = oracle.ok && matchesObservations(observed, family.expected, true);
      cells.push({ family: family.id, configuration, stage: 'desktop', pass: desktopPass, assembly: identity });
      if (!desktopPass) continue;
      for (const target of [0, 1]) {
        const cellKey = key + '-' + target, modulePath = resolve(directory, target + '.wasm');
        const requestPath = resolve(directory, target + '.request.json'), responsePath = resolve(directory, target + '.response.json');
        if (hash(assembly) !== identity.sha256) throw Error('desktop-assembly-changed');
        save(requestPath, { entryAssemblyPath: assembly, referencePaths: [...references, ...localReferences],
          entryTypeName: family.type, entryMethodName: 'Run', exports: [{ name: 'run', typeName: family.type, methodName: 'Run' }],
          target, diagnosticTracePath: null, sourcePaths: [], modulePath, collectCompilerMetrics: true,
          referenceAssemblyAliases: { ...aliases, ...Object.fromEntries(family.libraries.map(name => [name, name])), ...family.aliases },
          witPath: resolve(root, 'wit/netwasm-platform-1.0.0'), witWorld: 'netwasm:platform@1.0.0/platform' });
        const compilation = run('dotnet', [compiler, requestPath, responsePath], cellKey + '-compile');
        if (!compilation.ok) { cells.push({ family: family.id, configuration, target, stage: 'compile', pass: false, assembly: identity }); continue; }
        const response = json(responsePath);
        const execution = run('node', [engine, modulePath, '@' + inputPath, target === 0 ? 'wasm32' : 'wasm64', String(response.StaticDataEnd)], cellKey + '-execute');
        const pass = execution.ok && matchesObservations(json(execution.path), family.expected);
        cells.push({ family: family.id, configuration, target, stage: 'execute', pass, assembly: identity,
          module: describe(modulePath), response: describe(responsePath) });
      }
    }
  }
  for (const input of inputs) if (hash(input.path) !== input.sha256) throw Error('input-changed');
} catch { incomplete = true; }
save(resolve(output, 'observed-results.json'), { schemaVersion: 1, inputs, commands, cells, incomplete,
  limitations: ['Raw simulated host, not linked collector qualification.',
    'DI and binder desktop contracts compile port source; not package-only desktop compatibility.',
    'Co-located internal binder section-value interface does not qualify explicit-null-provider discrimination.'] });
const passed = !incomplete && cells.length === families.length * 6 && cells.every(cell => cell.pass);
console.log(JSON.stringify({ passed, incomplete, cells: cells.length, failed: cells.filter(cell => !cell.pass).length }));
process.exitCode = passed ? 0 : 1;

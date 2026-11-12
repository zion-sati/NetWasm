#!/usr/bin/env python3
"""Compare the runtime performance of two NetWasm package sets.

Each workload is published through the SDK for every selected optimization
preset with a baseline and a candidate package set, run under Wasmtime in
interleaved samples, and checked against the checksum the same program
produces on desktop .NET. Results go to <output>/summary.json.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent
REPOSITORY = ROOT.parents[1]
# Each scenario: workload directory, command-line arguments.
SCENARIOS = {
    'transaction': ('transaction', ['preallocated', '100000', '101']),
    'transaction-full': ('transaction', ['full', '100000', '101']),
    'object-graph': ('gc', ['object-graph', '100000']),
    'ui-churn': ('gc', ['ui-churn', '500']),
    'binary-trees': ('gc', ['binary-trees', '14']),
    'nullable-boxes': ('gc', ['nullable-boxes', '2000000']),
    'conversions': ('net11', ['conversions', '12000']),
    'constant-strings': ('net11', ['constant-strings', '100']),
    'sparse-set': ('net11', ['sparse-set', '4000']),
    'offset-bounds': ('net11', ['offset-bounds', '10000']),
    'from-end': ('net11', ['from-end', '10000']),
    'empty-span-guard': ('net11', ['empty-span-guard', '800']),
    'implied-branches': ('net11', ['implied-branches', '6000']),
    'generic-virtual': ('net11', ['generic-virtual', '1000']),
    'default-interface': ('net11', ['default-interface', '1000']),
    'activator-exact': ('net11', ['activator-exact', '100']),
    'short-copies': ('corelib', ['short-copies', '50000']),
    'integer-formatting': ('corelib', ['integer-formatting', '20000']),
    'nbody': ('compute', ['nbody', '1000000']),
    'sort': ('compute', ['sort', '10']),
    'async-completed': ('async', ['async-completed', '200000']),
    'async-valuetask-completed': ('async', ['async-valuetask-completed', '200000']),
    'async-chain': ('async', ['async-chain', '20000']),
    'yield': ('async', ['yield', '100000']),
    'value-task-source': ('async', ['value-task-source', '100000']),
    'ping-pong': ('async', ['ping-pong', '50000']),
    'async-iterator': ('async', ['async-iterator', '200000']),
    'when-all': ('async', ['when-all', '10000']),
    'crc32': ('compute', ['crc32', '16']),
}
COLLECTORS = ['Compact', 'NetWasm', 'Bump']
NAMES = {'transaction': 'Transaction', 'gc': 'GcWorkloads', 'net11': 'Net11Patterns', 'corelib': 'CoreLibPaths',
         'compute': 'ComputeKernels', 'async': 'AsyncPatterns'}


def run(command, cwd, env, log):
    with open(log, 'w') as output:
        completed = subprocess.run(command, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT)
    if completed.returncode != 0:
        raise SystemExit(f'{" ".join(command)} failed; see {log}')


def isolated_environment(root):
    env = dict(os.environ, NUGET_PACKAGES=str(root / 'packages'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1')
    for key in ['NETWASM_EMSDK_ROOT', 'NETWASM_NODE_PATH', 'NETWASM_WASM_LD_PATH', 'NETWASM_WASM_OPT_PATH']:
        env.pop(key, None)
    return env


def publish(side, feed, version, workload, optimization, root, sdk, environment=None, collector=None,
            properties=()):
    """Publishes one workload for one package set and preset, with the runtime
    pack's default collector unless one is named; returns the component."""
    cell = root / side / f'{workload}-{optimization.lower()}'
    cell.mkdir(parents=True)
    name = NAMES[workload]
    shutil.copyfile(ROOT / 'workloads' / workload / 'Program.cs', cell / 'Program.cs')
    (cell / f'{name}.csproj').write_text(
        '<Project Sdk="NetWasm.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>netwasm0.1</TargetFramework><Nullable>enable</Nullable>'
        '</PropertyGroup></Project>\n')
    (cell / 'global.json').write_text(json.dumps({
        'sdk': {'version': sdk, 'rollForward': 'disable'}, 'msbuild-sdks': {'NetWasm.Sdk': version}}))
    (cell / 'NuGet.Config').write_text(
        '<configuration><packageSources><clear/>'
        f'<add key="{side}" value="{escape(str(feed), {chr(34): "&quot;"})}"/>'
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    env = dict(isolated_environment(root / side), **(environment or {}))
    selection = [f'-p:NetWasmGarbageCollector={collector}'] if collector else []
    selection += [f'-p:{value}' for value in properties]
    run(['dotnet', 'publish', f'{name}.csproj', '-c', 'Release', f'-p:NetWasmOptimization={optimization}',
         '-p:NetWasmOptimizerConverge=true', *selection, '-o', 'out', '--disable-build-servers', '--nologo'], cell, env, cell / 'publish.log')
    return cell / 'out' / f'{name}.wasm'


def overlay_runtime(packages, version, runtime):
    """Replaces a restored runtime pack's native runtime with a regenerated one:
    the runtime directory that regenerate-runtime-pack.sh writes, including the
    runtime-pack.json whose digests the SDK verifies."""
    target = packages / 'netwasm.runtime.pack' / version.lower() / 'runtime'
    shutil.rmtree(target)
    shutil.copytree(runtime, target)


def desktop_reference(workload, root, sdk, framework):
    """Runs the workload on desktop .NET and returns the project directory."""
    cell = root / 'desktop' / workload
    cell.mkdir(parents=True)
    name = NAMES[workload]
    shutil.copyfile(ROOT / 'workloads' / workload / 'Program.cs', cell / 'Program.cs')
    (cell / f'{name}.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        f'<TargetFramework>{framework}</TargetFramework><Nullable>enable</Nullable>'
        '<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup></Project>\n')
    (cell / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable'}}))
    env = isolated_environment(root / 'desktop')
    run(['dotnet', 'build', f'{name}.csproj', '-c', 'Release', '--nologo'], cell, env, cell / 'build.log')
    return cell / 'bin' / 'Release' / framework / f'{name}.dll'


def parse(text):
    """Returns (milliseconds, checksum) from a workload's first result line."""
    for line in text.splitlines():
        fields = line.split('|')
        if fields[0] in ('PROBE', 'GCWORK', 'NET11', 'CORELIB', 'COMPUTE', 'ASYNC'):
            ticks, frequency, checksum = int(fields[2]), int(fields[3]), fields[4]
            return ticks * 1000 / frequency, checksum
    raise ValueError(f'no result line in: {text[:200]}')


def execute(command):
    completed = subprocess.run(command, capture_output=True, text=True)
    if completed.returncode != 0:
        raise SystemExit(f'{" ".join(map(str, command))} exited {completed.returncode}: '
                         f'{completed.stdout[-400:]}{completed.stderr[-400:]}')
    return parse(completed.stdout)


def brotli_size(path):
    node = shutil.which('node')
    script = ("const z=require('zlib');const b=require('fs').readFileSync(process.argv[1]);"
              "console.log(z.brotliCompressSync(b,{params:{[z.constants.BROTLI_PARAM_QUALITY]:11,"
              "[z.constants.BROTLI_PARAM_LGWIN]:22}}).length)")
    return int(subprocess.run([node, '-e', script, str(path)], capture_output=True, text=True,
                              check=True).stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--candidate', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=15)
    parser.add_argument('--optimizations', nargs='+', default=['O3', 'Oz'])
    parser.add_argument('--scenarios', nargs='+', choices=SCENARIOS, default=list(SCENARIOS))
    parser.add_argument('--baseline-collector', choices=COLLECTORS,
                        help="the baseline's collector; the runtime pack default when omitted")
    parser.add_argument('--candidate-collector', choices=COLLECTORS,
                        help="the candidate's collector; the runtime pack default when omitted")
    parser.add_argument('--baseline-runtime', type=Path,
                        help='a regenerated runtime pack runtime directory for the baseline side')
    parser.add_argument('--candidate-runtime', type=Path,
                        help='a regenerated runtime pack runtime directory for the candidate side')
    parser.add_argument('--baseline-property', action='append', default=[], metavar='NAME=VALUE',
                        help="an MSBuild property for the baseline's publishes; repeatable")
    parser.add_argument('--candidate-property', action='append', default=[], metavar='NAME=VALUE',
                        help="an MSBuild property for the candidate's publishes; repeatable")
    parser.add_argument('--semantic-changes', nargs='*', default=[],
                        help='scenarios whose baseline result may differ from desktop .NET 11')
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    toolchain = json.loads((REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    sdk10, sdk11 = toolchain['sdk10BuildVersion'], toolchain['sdk11Version']
    wasmtime = shutil.which('wasmtime')
    sides = {'baseline': args.baseline, 'candidate': args.candidate}
    collectors = {'baseline': args.baseline_collector, 'candidate': args.candidate_collector}
    runtimes = {'baseline': args.baseline_runtime, 'candidate': args.candidate_runtime}
    properties = {'baseline': args.baseline_property, 'candidate': args.candidate_property}
    workloads = sorted({SCENARIOS[name][0] for name in args.scenarios})

    references = {}
    for workload in workloads:
        assembly = desktop_reference(workload, root, sdk11, 'net11.0')
        for name in args.scenarios:
            if SCENARIOS[name][0] == workload:
                references[name] = execute(['dotnet', str(assembly), *SCENARIOS[name][1]])[1]

    components = {}
    for side, (feed, version) in sides.items():
        if runtimes[side]:
            # Restore the side's packages once, then swap in the regenerated runtime.
            packages = root / side / 'packages'
            publish(f'{side}-restore', Path(feed).resolve(), version, workloads[0], args.optimizations[0], root,
                    sdk10, {'NUGET_PACKAGES': str(packages)}, collector=collectors[side],
                    properties=properties[side])
            overlay_runtime(packages, version, runtimes[side].resolve())
        for optimization in args.optimizations:
            for workload in workloads:
                components[side, optimization, workload] = publish(
                    side, Path(feed).resolve(), version, workload, optimization, root, sdk10,
                    collector=collectors[side], properties=properties[side])

    results = []
    for optimization in args.optimizations:
        for name in args.scenarios:
            workload, arguments = SCENARIOS[name]
            times = {side: [] for side in sides}
            checksums = {side: set() for side in sides}
            for sample in range(args.samples):
                order = list(sides) if sample % 2 == 0 else list(reversed(sides))
                for side in order:
                    milliseconds, checksum = execute(
                        [wasmtime, 'run', str(components[side, optimization, workload]), *arguments])
                    times[side].append(milliseconds)
                    checksums[side].add(checksum)
            row = {'scenario': name, 'optimization': optimization, 'reference': references[name]}
            for side in sides:
                component = components[side, optimization, workload]
                row[side] = {
                    'medianMs': statistics.median(times[side]),
                    'minMs': min(times[side]), 'maxMs': max(times[side]),
                    'checksums': sorted(checksums[side]),
                    'matchesDesktop': checksums[side] == {references[name]},
                    'rawBytes': component.stat().st_size, 'brotliBytes': brotli_size(component),
                    'sha256': hashlib.sha256(component.read_bytes()).hexdigest(),
                }
            row['ratio'] = row['candidate']['medianMs'] / row['baseline']['medianMs']
            results.append(row)
            print(f"{optimization} {name}: baseline {row['baseline']['medianMs']:.3f} ms, "
                  f"candidate {row['candidate']['medianMs']:.3f} ms, ratio {row['ratio']:.3f}, "
                  f"desktop match {row['baseline']['matchesDesktop']}/{row['candidate']['matchesDesktop']}",
                  flush=True)
            (root / 'summary.json').write_text(json.dumps({
                'collectors': collectors, 'properties': properties, 'runtimes': {side: str(path) if path else None for side, path in runtimes.items()},
                'results': results}, indent=2))

    failures = [row for row in results if not row['candidate']['matchesDesktop'] or
                not row['baseline']['matchesDesktop'] and row['scenario'] not in args.semantic_changes]
    for row in failures:
        print(f"checksum mismatch: {row['optimization']} {row['scenario']}", flush=True)
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())

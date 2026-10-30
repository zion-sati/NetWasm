#!/usr/bin/env python3
"""Compare NetWasm's garbage collectors on one package set.

Every scenario is published once per collector with the same compiler preset,
checked against the checksum the program produces on desktop .NET, timed in
fresh `wasmtime run` processes whose collector order rotates each sample, and
then run twice under memory-host to record its peak aggregate linear-memory
capacity. Results go to <output>/summary.json and a Markdown table is printed.

--desktop adds desktop .NET 11 to the same rotation, as the default tiered JIT
(`jit`) and NativeAOT (`aot`), for scale. Desktop runs native code for the host
CPU, so it has no linear memory or component size.

--mono-aot adds Mono, compiled ahead of time for wasi-wasm with Wasm SIMD and
run by the same Wasmtime: the same .NET-on-Wasm comparison. It names a .NET SDK
root with the wasi-experimental workload and the wasi-sdk it requires.

This produces the figures in docs/runtime-garbage-collection.md.
"""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess

_spec = importlib.util.spec_from_file_location(
    'runtime_performance', Path(__file__).resolve().parent / 'run.py')
run = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(run)

DEFAULT_SCENARIOS = ['transaction-full', 'object-graph', 'ui-churn']
# Compile each module on one thread so a sample's startup does not load every
# core; the timed region starts after compilation.
SERIAL = ['-C', 'parallel-compilation=n']
# The workloads print their timing line, then their collector counters.
COUNTERS = {
    'GC': ['collections', 'allocatedBytes', 'totalMemoryBytes'],
    'GCWORK': ['allocatedBytes', 'collections', 'retainedBytes', 'retainedModels'],
}


def measure(command):
    """Runs one component and returns (milliseconds, checksum, counters)."""
    completed = subprocess.run(command, capture_output=True, text=True)
    if completed.returncode != 0:
        raise SystemExit(f'{" ".join(map(str, command))} exited {completed.returncode}: '
                         f'{completed.stdout[-400:]}{completed.stderr[-400:]}')
    milliseconds, checksum = run.parse(completed.stdout)
    counters = {}
    for line in completed.stdout.splitlines():
        fields = line.split('|')
        if fields[0] == 'GC':
            counters = dict(zip(COUNTERS['GC'], map(int, fields[1:])))
        elif fields[0] == 'GCWORK':
            counters = dict(zip(COUNTERS['GCWORK'], map(int, fields[5:])))
    return milliseconds, checksum, counters


def peak_memory(host, command):
    """Returns the component's peak aggregate linear-memory capacity in bytes."""
    completed = subprocess.run([host, *map(str, command)], capture_output=True, text=True)
    if completed.returncode != 0:
        raise SystemExit(f'memory-host {" ".join(map(str, command))} exited {completed.returncode}: '
                         f'{completed.stderr[-400:]}')
    for line in completed.stderr.splitlines():
        if line.startswith('NETWASM-MEMORY|'):
            return int(line.split('|')[1])
    raise SystemExit(f'memory-host printed no capacity for {command}')


def desktop_native(project, root):
    """Publishes the desktop reference project with NativeAOT; returns the executable."""
    rid = {('Darwin', 'arm64'): 'osx-arm64', ('Linux', 'x86_64'): 'linux-x64',
           ('Linux', 'aarch64'): 'linux-arm64'}[platform.system(), platform.machine()]
    name = project.stem
    run.run(['dotnet', 'publish', project.name, '-c', 'Release', '-r', rid, '-p:PublishAot=true',
             '-o', 'aot', '--nologo'], project.parent, run.isolated_environment(root / 'desktop'),
            project.parent / 'publish-aot.log')
    return project.parent / 'aot' / name


def mono_aot(workload, root, dotnet_root, wasi_sdk):
    """Publishes the workload with Mono AOT for wasi-wasm; returns the bundled module."""
    cell = root / 'mono-aot' / workload
    cell.mkdir(parents=True)
    name = run.NAMES[workload]
    shutil.copyfile(run.ROOT / 'workloads' / workload / 'Program.cs', cell / 'Program.cs')
    dotnet = dotnet_root / 'dotnet'
    sdk = subprocess.run([dotnet, '--version'], capture_output=True, text=True, check=True,
                         cwd=dotnet_root).stdout.strip()
    framework = f"net{sdk.split('.')[0]}.0"
    # Wasm SIMD, as NetWasm uses it: WasiApp.targets leaves -msimd128 out of
    # the clang flags, so without it the SIMD bitcode fails to compile.
    # .NET 11's WorkloadManifest.Wasi.targets enables the workload through
    # WasiNativeWorkload10 while its wasi-experimental pack sets
    # WasiNativeWorkload11.
    (cell / f'{name}.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        f'<TargetFramework>{framework}</TargetFramework><RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>'
        '<RunAOTCompilation>true</RunAOTCompilation><WasmSingleFileBundle>true</WasmSingleFileBundle>'
        '<InvariantGlobalization>true</InvariantGlobalization><Nullable>enable</Nullable>'
        '<WasmEnableSIMD>true</WasmEnableSIMD><WasiClangFlags>-msimd128</WasiClangFlags>'
        + ('<WasiNativeWorkload10>true</WasiNativeWorkload10>' if framework == 'net11.0' else '') +
        '<Optimize>true</Optimize><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup></Project>\n')
    (cell / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable'}}))
    # wasi-sdk's clang runs any wasm-opt on PATH after linking; keep only the
    # toolchain the Mono workload expects.
    env = dict(run.isolated_environment(root / 'mono-aot'), DOTNET_ROOT=str(dotnet_root),
               DOTNET_MULTILEVEL_LOOKUP='0', WASI_SDK_PATH=str(wasi_sdk),
               PATH=os.pathsep.join([str(dotnet_root), '/usr/bin', '/bin', '/usr/sbin', '/sbin']))
    run.run([str(dotnet), 'publish', f'{name}.csproj', '-c', 'Release', '--nologo'], cell, env,
            cell / 'publish.log')
    return cell / 'bin' / 'Release' / framework / 'wasi-wasm' / 'AppBundle' / f'{name}.wasm'


def build_memory_host(root):
    target = root / 'memory-host-target'
    run.run(['cargo', 'build', '--release', '--locked', '--manifest-path',
             str(run.ROOT / 'memory-host' / 'Cargo.toml')], run.ROOT,
            dict(os.environ, CARGO_TARGET_DIR=str(target)), root / 'memory-host-build.log')
    return target / 'release' / 'netwasm-memory-host'


def host_description():
    description = {'machine': platform.machine(), 'system': platform.platform()}
    if platform.system() == 'Darwin':
        description['cpu'] = subprocess.run(['sysctl', '-n', 'machdep.cpu.brand_string'],
                                            capture_output=True, text=True).stdout.strip()
    return description


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--optimization', default='O3')
    parser.add_argument('--collectors', nargs='+', choices=run.COLLECTORS,
                        default=['Bump', 'NetWasm', 'Compact'])
    parser.add_argument('--scenarios', nargs='+', choices=run.SCENARIOS, default=DEFAULT_SCENARIOS)
    parser.add_argument('--warmups', type=int, default=3)
    parser.add_argument('--samples', type=int, default=15)
    parser.add_argument('--desktop', nargs='*', choices=['jit', 'aot'], default=[],
                        help='also time desktop .NET 11 with the tiered JIT and/or NativeAOT')
    parser.add_argument('--mono-aot', nargs=2, type=Path, metavar=('DOTNET_ROOT', 'WASI_SDK'),
                        help='also time Mono AOT for wasi-wasm under the same Wasmtime')
    parser.add_argument('--memory-host', type=Path,
                        help='a built netwasm-memory-host; built from memory-host/ when omitted')
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    toolchain = json.loads((run.REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    sdk10, sdk11 = toolchain['sdk10BuildVersion'], toolchain['sdk11Version']
    wasmtime = shutil.which('wasmtime')
    host = args.memory_host.resolve() if args.memory_host else build_memory_host(root)
    feed, version = Path(args.feed[0]).resolve(), args.feed[1]
    workloads = sorted({run.SCENARIOS[name][0] for name in args.scenarios})

    references = {}
    commands = {}
    for workload in workloads:
        assembly = run.desktop_reference(workload, root, sdk11, 'net11.0')
        for name in args.scenarios:
            if run.SCENARIOS[name][0] == workload:
                references[name] = run.execute(['dotnet', str(assembly), *run.SCENARIOS[name][1]])[1]
        if 'jit' in args.desktop:
            commands['desktop-jit', workload] = ['dotnet', str(assembly)]
        if 'aot' in args.desktop:
            project = assembly.parents[3] / f'{assembly.stem}.csproj'
            commands['desktop-aot', workload] = [str(desktop_native(project, root))]

    components = {}
    for collector in args.collectors:
        for workload in workloads:
            components[collector, workload] = run.publish(
                collector.lower(), feed, version, workload, args.optimization, root, sdk10,
                collector=collector)
            commands[collector, workload] = [wasmtime, 'run', *SERIAL, str(components[collector, workload])]
    participants = list(args.collectors)
    if args.mono_aot:
        dotnet_root, wasi_sdk = (path.resolve() for path in args.mono_aot)
        for workload in workloads:
            components['mono-aot', workload] = mono_aot(workload, root, dotnet_root, wasi_sdk)
            commands['mono-aot', workload] = [wasmtime, 'run', *SERIAL, str(components['mono-aot', workload])]
        participants.append('mono-aot')
    participants += [f'desktop-{mode}' for mode in args.desktop]

    load = {'before': os.getloadavg()}
    results = []
    for name in args.scenarios:
        workload, arguments = run.SCENARIOS[name]
        times = {collector: [] for collector in participants}
        checksums = {collector: set() for collector in participants}
        counters = {collector: [] for collector in participants}
        for sample in range(args.warmups + args.samples):
            shift = sample % len(participants)
            for collector in participants[shift:] + participants[:shift]:
                milliseconds, checksum, observed = measure([*commands[collector, workload], *arguments])
                checksums[collector].add(checksum)
                if sample >= args.warmups:
                    times[collector].append(milliseconds)
                    if observed not in counters[collector]:
                        counters[collector].append(observed)
        for collector in participants:
            component = components.get((collector, workload))
            capacities = ({peak_memory(host, [component, *arguments]) for _ in range(2)}
                          if component else set())
            row = {
                'scenario': name, 'collector': collector, 'reference': references[name],
                'medianMs': statistics.median(times[collector]),
                'minMs': min(times[collector]), 'maxMs': max(times[collector]),
                'samplesMs': times[collector],
                'checksums': sorted(checksums[collector]),
                'matchesDesktop': checksums[collector] == {references[name]},
                'counters': counters[collector],
                'peakLinearMemoryBytes': sorted(capacities),
                'rawBytes': component.stat().st_size if component else None,
                'brotliBytes': run.brotli_size(component) if component else None,
                'sha256': hashlib.sha256(component.read_bytes()).hexdigest() if component else None,
            }
            results.append(row)
            memory = f", {max(capacities) / 1048576:g} MiB" if capacities else ''
            print(f"{name} {collector}: {row['medianMs']:.2f} ms "
                  f"(min {row['minMs']:.2f}, max {row['maxMs']:.2f}){memory}, "
                  f"desktop match {row['matchesDesktop']}", flush=True)
    load['after'] = os.getloadavg()

    summary = {
        'feed': str(feed), 'version': version, 'optimization': args.optimization,
        'warmups': args.warmups, 'samples': args.samples,
        'wasmtime': subprocess.run([wasmtime, '--version'], capture_output=True, text=True).stdout.strip(),
        'host': host_description(), 'loadAverage': load, 'results': results,
    }
    if args.mono_aot:
        dotnet = args.mono_aot[0].resolve() / 'dotnet'
        summary['monoAot'] = {
            'sdk': subprocess.run([dotnet, '--version'], capture_output=True, text=True,
                                  cwd=args.mono_aot[0]).stdout.strip(),
            'workloads': subprocess.run([dotnet, 'workload', 'list'], capture_output=True, text=True,
                                        cwd=args.mono_aot[0]).stdout.strip(),
            'wasiSdk': (args.mono_aot[1] / 'VERSION').read_text().splitlines()[0],
        }
    (root / 'summary.json').write_text(json.dumps(summary, indent=2))

    print(f"\n| Collector | {' | '.join(args.scenarios)} |")
    print(f"| --- |{' ---: |' * len(args.scenarios)}")
    for collector in participants:
        cells = [next(row for row in results if row['scenario'] == name and row['collector'] == collector)
                 for name in args.scenarios]
        print(f"| {collector} | " + ' | '.join(
            f"{row['medianMs']:.2f} ms" + (f", {max(row['peakLinearMemoryBytes']) / 1048576:g} MiB"
                                           if row['peakLinearMemoryBytes'] else '')
            for row in cells) + ' |')

    failures = [row for row in results
                if not row['matchesDesktop']
                or row['rawBytes'] is not None and len(row['peakLinearMemoryBytes']) != 1]
    for row in failures:
        print(f"unrepeatable or mismatched: {row['collector']} {row['scenario']}", flush=True)
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())

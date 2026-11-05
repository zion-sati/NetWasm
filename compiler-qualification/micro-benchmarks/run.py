#!/usr/bin/env python3
"""Times dotnet/performance's micro-benchmarks, and NetWasm's own workloads, on
desktop .NET 11, Mono AOT and NetWasm, and writes every case's times and the
per-family table.

The generator reads the benchmark sources of a dotnet/performance checkout at
PERFORMANCE_COMMIT and writes a reflection-free table of every case: each
[Params] value, [GlobalSetup] and [Arguments] set as plain C#. Every runtime
compiles the same sources and table:

- desktop: .NET 11's JIT without tiering (DOTNET_TieredCompilation=0);
- mono: Mono AOT for wasi-wasm with Wasm SIMD, under Wasmtime;
- netwasm: NetWasm at O3 with NetWasmGC, under Wasmtime.

The cases every runtime lists are timed in chunks. Each chunk runs on the three
runtimes one after another, and the order rotates from chunk to chunk and round
to round; each runtime keeps its fastest time per case. A runner times a case by
a warm-up call, then a batch doubled until it lasts 20 ms, and reports the
fastest of five batches as nanoseconds per call.

usage: run.py --performance <dotnet/performance checkout> --feed <feed> <version>
              --mono-aot <dotnet root> <wasi-sdk root> --output <new directory>
              [--libraries VERSION] [--rounds N] [--chunk N] [--filter TEXT] [--limit N]

An empty --feed directory restores the released NetWasm packages from NuGet.org."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path
from xml.sax.saxutils import escape

import table

ROOT = Path(__file__).resolve().parent
REPOSITORY = ROOT.parents[1]
PERFORMANCE_COMMIT = 'ceb046992aa203f881d7c8a29d50a105805ecb42'
SOURCES = [
    'benchmarks/micro/runtime',
    'benchmarks/micro/libraries/System.Runtime.Intrinsics',
    'benchmarks/micro/libraries/System.Numerics.Vectors',
    'benchmarks/micro/libraries/System.Memory',
]
# The benchmarks guard newer APIs with the .NET version symbols, which the NetWasm
# framework does not set, so every runtime compiles the same code.
VERSION_SYMBOLS = ('NETCOREAPP;NET5_0_OR_GREATER;NET6_0_OR_GREATER;NET7_0_OR_GREATER;NET8_0_OR_GREATER;'
                   'NET9_0_OR_GREATER;NET10_0_OR_GREATER')


def run(command, cwd, env, log):
    with open(log, 'w') as output:
        result = subprocess.run([str(part) for part in command], cwd=cwd, env=env, stdout=output,
                                stderr=subprocess.STDOUT)
    if result.returncode != 0:
        raise SystemExit(f'{command[0]} failed with exit {result.returncode}; see {log}')


def isolated(root, **overrides):
    """A package cache and CLI home of the run's own, so nothing outside it is restored or read."""
    env = dict(os.environ, NUGET_PACKAGES=str(root / 'packages'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1')
    env.update(overrides)
    return env


def write_project(cell, sdk, body, properties=''):
    cell.mkdir(parents=True)
    (cell / 'Micro.csproj').write_text(
        f'<Project Sdk="{sdk}">\n'
        f'  <Import Project="{escape(str(ROOT / "Benchmarks.props"))}" />\n'
        '  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n'
        f'{properties}  </PropertyGroup>\n{body}</Project>\n')


def generate(performance, output, env):
    """Writes the case table, and the exclusions as Compile removals, under output/generated."""
    generated = output / 'generated'
    generated.mkdir()
    source = output / 'generator-source'
    shutil.copytree(ROOT / 'generator', source)
    run(['dotnet', 'build', 'Generator.csproj', '-c', 'Release', '-o', output / 'generator', '--nologo'],
        source, env, output / 'generator-build.log')
    run(['dotnet', output / 'generator' / 'Generator.dll', generated / 'Benchmarks.g.cs', ROOT / 'exclusions.txt',
         ROOT / 'shim', performance / 'benchmarks' / 'micro' / 'Categories.cs',
         *[performance / source for source in SOURCES], ROOT / 'workloads'],
        ROOT, env, output / 'generate.log')
    (generated / 'Exclusions.g.props').write_text(exclusion_props((ROOT / 'exclusions.txt').read_text()))
    return generated


def exclusion_props(exclusions):
    """The exclusions as Compile removals: a file, or every file under a directory ending in '/'."""
    removals = []
    for line in exclusions.splitlines():
        entry = line.split('#')[0].strip()
        if entry:
            pattern = f'{entry}**' if entry.endswith('/') else entry
            removals.append(f'    <Compile Remove="$(DotnetPerformanceSource)/benchmarks/micro/{escape(pattern)}" />\n')
    return '<Project>\n  <ItemGroup>\n' + ''.join(removals) + '  </ItemGroup>\n</Project>\n'


def build_desktop(output, performance, generated, sdk, env):
    cell = output / 'desktop'
    write_project(cell, 'Microsoft.NET.Sdk', '', '    <TargetFramework>net11.0</TargetFramework>\n')
    (cell / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable'}}))
    run(['dotnet', 'build', 'Micro.csproj', '-c', 'Release', f'-p:DotnetPerformanceSource={performance}',
         f'-p:MicroBenchmarkGenerated={generated}', '--nologo'], cell, env, cell / 'build.log')
    return ['dotnet', str(cell / 'bin' / 'Release' / 'net11.0' / 'Micro.dll')]


def build_mono(output, performance, generated, dotnet_root, wasi_sdk):
    cell = output / 'mono'
    dotnet = dotnet_root / 'dotnet'
    sdk = subprocess.run([dotnet, '--version'], capture_output=True, text=True, check=True,
                         cwd=dotnet_root).stdout.strip()
    framework = f"net{sdk.split('.')[0]}.0"
    # Wasm SIMD, as NetWasm uses it: WasiApp.targets leaves -msimd128 out of the clang
    # flags, so without it the SIMD bitcode fails to compile. .NET 11's
    # WorkloadManifest.Wasi.targets enables the workload through WasiNativeWorkload10
    # while its wasi-experimental pack sets WasiNativeWorkload11.
    write_project(cell, 'Microsoft.NET.Sdk', '',
                  f'    <TargetFramework>{framework}</TargetFramework>\n'
                  '    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>\n'
                  '    <RunAOTCompilation>true</RunAOTCompilation>\n'
                  '    <WasmSingleFileBundle>true</WasmSingleFileBundle>\n'
                  '    <InvariantGlobalization>true</InvariantGlobalization>\n'
                  '    <WasmEnableSIMD>true</WasmEnableSIMD>\n'
                  '    <WasiClangFlags>-msimd128</WasiClangFlags>\n'
                  + ('    <WasiNativeWorkload10>true</WasiNativeWorkload10>\n' if framework == 'net11.0' else '')
                  + '    <Optimize>true</Optimize>\n')
    (cell / 'global.json').write_text(json.dumps({'sdk': {'version': sdk, 'rollForward': 'disable'}}))
    # wasi-sdk's clang runs any wasm-opt on PATH after linking; keep only the toolchain
    # the Mono build expects. .NET 11's targets append bin/clang to the path as written.
    env = isolated(output / 'mono-home', DOTNET_ROOT=str(dotnet_root), DOTNET_MULTILEVEL_LOOKUP='0',
                   WASI_SDK_PATH=str(wasi_sdk) + os.sep,
                   PATH=os.pathsep.join([str(dotnet_root), '/usr/bin', '/bin', '/usr/sbin', '/sbin']))
    run([dotnet, 'publish', 'Micro.csproj', '-c', 'Release', f'-p:DotnetPerformanceSource={performance}',
         f'-p:MicroBenchmarkGenerated={generated}', '--nologo'], cell, env, cell / 'publish.log')
    return cell / 'bin' / 'Release' / framework / 'wasi-wasm' / 'AppBundle' / 'Micro.wasm'


def build_netwasm(output, performance, generated, feed, version, libraries, sdk, env):
    cell = output / 'netwasm'
    write_project(cell, 'NetWasm.Sdk',
                  '  <ItemGroup>\n'
                  f'    <PackageReference Include="NetWasm.System.Text.RegularExpressions" Version="{libraries}" />\n'
                  f'    <PackageReference Include="NetWasm.System.Linq" Version="{libraries}" />\n'
                  '  </ItemGroup>\n',
                  '    <TargetFramework>netwasm0.1</TargetFramework>\n'
                  f'    <DefineConstants>$(DefineConstants);{VERSION_SYMBOLS}</DefineConstants>\n')
    (cell / 'global.json').write_text(json.dumps({
        'sdk': {'version': sdk, 'rollForward': 'latestFeature'}, 'msbuild-sdks': {'NetWasm.Sdk': version}}))
    (cell / 'NuGet.Config').write_text(
        '<configuration><packageSources><clear/>'
        f'<add key="candidate" value="{escape(str(feed), {chr(34): "&quot;"})}"/>'
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    run(['dotnet', 'build', 'Micro.csproj', '-c', 'Release', '-p:NetWasmOptimization=O3',
         '-p:NetWasmGarbageCollector=NetWasm', f'-p:DotnetPerformanceSource={performance}',
         f'-p:MicroBenchmarkGenerated={generated}', '--nologo'], cell, env, cell / 'build.log')
    return cell / 'bin' / 'Release' / 'netwasm0.1' / 'Micro.wasm'


def precompiled(wasmtime, wasm):
    cwasm = wasm.with_suffix('.cwasm')
    subprocess.run([wasmtime, 'compile', '-o', str(cwasm), str(wasm)], check=True)
    return [wasmtime, 'run', '--allow-precompiled', str(cwasm)]


def interleave(sides, output, rounds, size, filter_text, limit):
    """Times every case all sides list; returns {side: {case: fastest ns}} and the common cases."""
    def invoke(side, *arguments):
        command, env = sides[side]
        return subprocess.run([*command, *arguments], capture_output=True, text=True, env=env)

    names = list(sides)
    listed = {side: [line for line in invoke(side, '', 'list').stdout.splitlines() if line] for side in names}
    common = [case for case in listed[names[0]] if all(case in set(listed[side]) for side in names)
              and filter_text in case][:limit]
    chunks = [common[index:index + size] for index in range(0, len(common), size)]
    best = {side: {} for side in names}
    begin = time.time()
    with open(output / 'timing.log', 'w') as log:
        log.write(f'{len(common)} common cases in {len(chunks)} chunks of {size}, {rounds} rounds; listed '
                  + ', '.join(f'{side} {len(listed[side])}' for side in names) + '\n')
        for round_index in range(rounds):
            for index, chunk in enumerate(chunks):
                wanted = set(chunk)
                offset = (index + round_index) % len(names)
                for side in names[offset:] + names[:offset]:
                    start, before = time.time(), os.getloadavg()[0]
                    result = invoke(side, '|'.join(chunk))
                    timed = 0
                    for line in result.stdout.splitlines():
                        case, _, value = line.rpartition('\t')
                        if case not in wanted:
                            continue
                        try:
                            nanoseconds = float(value)
                        except ValueError:
                            log.write(f'{side}\t{case}\t{value}\n')
                            continue
                        timed += 1
                        if case not in best[side] or nanoseconds < best[side][case]:
                            best[side][case] = nanoseconds
                    log.write(f'round {round_index} chunk {index} {side}: {timed}/{len(chunk)} in '
                              f'{time.time() - start:.1f}s, exit {result.returncode}, '
                              f'load {before:.2f}-{os.getloadavg()[0]:.2f}\n')
                    log.flush()
                if index % 10 == 9:
                    print(f'round {round_index + 1}/{rounds}: chunk {index + 1}/{len(chunks)} after '
                          f'{time.time() - begin:.0f}s', flush=True)
    return best, common


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--performance', type=Path, required=True, help='a dotnet/performance checkout')
    parser.add_argument('--feed', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--libraries', help='NetWasm.System.Linq and RegularExpressions version (default: VERSION)')
    parser.add_argument('--mono-aot', nargs=2, type=Path, metavar=('DOTNET_ROOT', 'WASI_SDK'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--rounds', type=int, default=2)
    parser.add_argument('--chunk', type=int, default=50)
    parser.add_argument('--filter', default='', help='time only the cases whose id contains it')
    parser.add_argument('--limit', type=int, help='time only the first N common cases')
    arguments = parser.parse_args()

    checkout = arguments.performance.resolve()
    head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=checkout, capture_output=True, text=True,
                          check=True).stdout.strip()
    if head != PERFORMANCE_COMMIT:
        raise SystemExit(f'{checkout} is at {head}; check out {PERFORMANCE_COMMIT}')
    wasmtime = shutil.which('wasmtime')
    if wasmtime is None:
        raise SystemExit('wasmtime is not on PATH')
    output = arguments.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    # Projects under output take nothing from a repository around it.
    for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
        (output / name).write_text('<Project />\n')
    feed, version = Path(arguments.feed[0]).resolve(), arguments.feed[1]
    toolchain = json.loads((REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    performance = checkout / 'src'
    env = isolated(output)

    generated = generate(performance, output, env)
    desktop = build_desktop(output, performance, generated, toolchain['sdk11Version'], env)
    mono = build_mono(output, performance, generated, *(path.resolve() for path in arguments.mono_aot))
    netwasm = build_netwasm(output, performance, generated, feed, version, arguments.libraries or version,
                            toolchain['sdk10BuildVersion'], env)
    sides = {
        'desktop': (desktop, dict(env, DOTNET_TieredCompilation='0')),
        'mono': (precompiled(wasmtime, mono), None),
        'netwasm': (precompiled(wasmtime, netwasm), None),
    }
    best, common = interleave(sides, output, arguments.rounds, arguments.chunk, arguments.filter,
                              arguments.limit)
    results = output / 'results.tsv'
    with open(results, 'w') as out:
        out.write('case\tdesktop_ns\tmono_ns\tnetwasm_ns\n')
        for case in common:
            out.write(case + ''.join(f"\t{best[side].get(case, '')}" for side in sides) + '\n')
    removed = table.read_cases(ROOT / 'mono-removed.txt')
    print(table.write(results, output / 'table.md', removed))
    (output / 'summary.json').write_text(json.dumps({
        'netwasm': version, 'libraries': arguments.libraries or version, 'performance': PERFORMANCE_COMMIT,
        'mono': subprocess.run([arguments.mono_aot[0] / 'dotnet', '--version'], capture_output=True,
                               text=True).stdout.strip(),
        'desktop': toolchain['sdk11Version'],
        'wasmtime': subprocess.run([wasmtime, '--version'], capture_output=True, text=True).stdout.strip(),
        'rounds': arguments.rounds, 'cases': len(common)}, indent=2) + '\n')
    print(f'wrote {results} and {output / "table.md"}')


if __name__ == '__main__':
    sys.exit(main())

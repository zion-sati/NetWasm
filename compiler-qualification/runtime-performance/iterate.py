#!/usr/bin/env python3
"""Iterate quickly on compiler output and engine behaviour.

Each iteration rebuilds only the compiler tasks from a source tree, overlays
them on a disposable copy of a base package set's restored cache, republishes
one workload, times selected scenarios against the base under Wasmtime, and
disassembles each scenario's function as Wasmtime compiles it. A wasm-opt
stand-in keeps a named copy of the final core module; the run fails unless
that copy matches the product output once custom sections are removed. Both
sides use NetWasmGC, the throughput collector, unless --collector names another.

Measurements here guide iteration. Report final results from run.py with real
package feeds.

  python3 compiler-qualification/runtime-performance/iterate.py \\
    --base <feed> <version> --output <directory> --label <name> \\
    --workload net11 --scenarios conversions from-end --optimization O3
"""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess

ROOT = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location('runtime_performance', ROOT / 'run.py')
run = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(run)

# The managed method that each scenario times.
FUNCTIONS = {
    'transaction': 'Program::Aggregate',
    'object-graph': 'Program::ObjectGraph',
    'ui-churn': 'Program::UiChurn',
    'binary-trees': 'Program::BinaryTrees',
    'nullable-boxes': 'Program::NullableBoxes',
    'conversions': 'Scenarios::Conversions',
    'constant-strings': 'Scenarios::ConstantStrings',
    'sparse-set': 'Scenarios::SparseSet',
    'offset-bounds': 'Scenarios::OffsetBounds',
    'from-end': 'Scenarios::FromEnd',
    'empty-span-guard': 'Scenarios::EmptySpanGuard',
    'implied-branches': 'Scenarios::ImpliedBranches',
    'generic-virtual': 'Scenarios::GenericVirtual',
    'default-interface': 'Scenarios::DefaultInterface',
    'activator-exact': 'Scenarios::ActivatorExact',
    'short-copies': 'Scenarios::ShortCopies',
    'integer-formatting': 'Scenarios::IntegerFormatting',
    'nbody': 'NBody::Advance',
    'sort': 'Sorting::Sort',
    'crc32': 'Crc32::Run',
}


def build_tasks(source):
    project = source / 'src' / 'NetWasm.Compiler.Tasks' / 'NetWasm.Compiler.Tasks.csproj'
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo', '-v:q'], check=True)
    return project.parent / 'bin' / 'Release' / 'net10.0'


def build_corelib(source):
    project = source / 'src' / 'NetWasm.CoreLib' / 'NetWasm.CoreLib.csproj'
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo', '-v:q'], check=True)
    return project.parent / 'bin' / 'Release' / 'net10.0' / 'NetWasm.CoreLib.dll'


def overlay_corelib(packages, version, built):
    """Replaces the runtime CoreLib with a fresh build, or restores the packaged one when built is None."""
    for runtime in packages.glob(f'netwasm.runtime.*/{version.lower()}/runtime/NetWasm.CoreLib.dll'):
        pristine = runtime.with_name('NetWasm.CoreLib.dll.packaged')
        if not pristine.exists():
            shutil.copy2(runtime, pristine)
        shutil.copy2(built or pristine, runtime)


def overlay(packages, version, built):
    """Replaces the restored compiler tasks with a fresh build of the same files."""
    target = packages / 'netwasm.compiler.tasks' / version.lower() / 'tasks' / 'net10.0'
    for file in target.iterdir():
        replacement = built / file.name
        if not replacement.exists():
            raise SystemExit(f'{file.name} is missing from {built}')
        shutil.copy2(replacement, file)


def named_environment(packages, named):
    tools = next(packages.glob('netwasm.hosttools.*/*/tools/bin'))
    return {'NUGET_PACKAGES': str(packages), 'NETWASM_NAMED_WASM_DIR': str(named),
            'NETWASM_WASM_OPT_PATH': str(ROOT / 'named-wasm-opt.py'), 'NETWASM_REAL_WASM_OPT': str(tools / 'wasm-opt'),
            'NETWASM_WASM_MERGE_PATH': str(ROOT / 'named-wasm-merge.py'),
            'NETWASM_REAL_WASM_MERGE': str(tools / 'wasm-merge')}


def stamped(path, digest):
    """Whether a cache stamp records this workload source digest."""
    return path.exists() and path.read_text() == digest


def publish_pair(side, feed, version, workload, optimization, root, sdk, packages, collector):
    """Publishes the timed product cell and a named twin; returns both cells."""
    product = root / side / f'{workload}-{optimization.lower()}'
    named = root / f'{side}-named' / f'{workload}-{optimization.lower()}'
    for cell in (product, named):
        shutil.rmtree(cell, ignore_errors=True)
    run.publish(side, feed, version, workload, optimization, root, sdk, {'NUGET_PACKAGES': str(packages)},
                collector=collector)
    run.publish(f'{side}-named', feed, version, workload, optimization, root, sdk,
                named_environment(packages, named / 'named'), collector=collector)
    return product, named


def stripped(path):
    return subprocess.run(['wasm-tools', 'strip', '--all', str(path)], capture_output=True, check=True).stdout


def named_core(product, twin, name):
    """Returns the named copy of the timed core module, after proving it is the same code."""
    timed = stripped(product / 'out' / f'{name}-component.core.wasm')
    if stripped(twin / 'out' / f'{name}-component.core.wasm') != timed:
        raise SystemExit(f'the named build in {twin} produced different code from {product}')
    named = next((path for path in sorted((twin / 'named').glob('*.named.wasm')) if stripped(path) == timed), None)
    if named is None:
        raise SystemExit(f'no named core module in {twin} matches the timed one')
    return named


def disassemble(core, functions, directory):
    """Writes each function's native code and returns loop statistics for it."""
    directory.mkdir(parents=True, exist_ok=True)
    compiled = directory / 'core.cwasm'
    subprocess.run(['wasmtime', 'compile', str(core), '-o', str(compiled)], check=True)
    text = subprocess.run(['wasmtime', 'objdump', '--color', 'never', '--addresses', str(compiled)],
                          capture_output=True, text=True, check=True).stdout
    sections = re.split(r'\n(?=[0-9a-f]+ wasm\[)', text)
    report = {}
    for scenario, function in functions.items():
        section = next((s for s in sections if s.split('\n', 1)[0].endswith(f'::{function}:')), None)
        if section is None:
            report[scenario] = None
            continue
        (directory / f'{scenario}.s').write_text(section)
        report[scenario] = statistics_for(section)
    return report


def statistics_for(section):
    instructions = []
    for line in section.split('\n')[1:]:
        match = re.match(r'\s*([0-9a-f]+):\s+(\S+)\s*(.*)', line)
        if match:
            instructions.append((int(match[1], 16), match[2], match[3]))

    def counts(window):
        return {
            'instructions': len(window),
            'stackLoads': sum(1 for _, op, args in window if op.startswith('ld') and '[sp' in args),
            'stackStores': sum(1 for _, op, args in window if op.startswith('st') and '[sp' in args),
            'conditionalBranches': sum(1 for _, op, _ in window if op.startswith(('b.', 'cbz', 'cbnz', 'tbz', 'tbnz'))),
            'calls': sum(1 for _, op, _ in window if op in ('bl', 'blr')),
        }

    loops = []
    for address, op, args in instructions:
        target = re.search(r'#0x([0-9a-f]+)', args)
        if (op == 'b' or op.startswith(('b.', 'cbz', 'cbnz', 'tbz', 'tbnz'))) and target and int(target[1], 16) <= address:
            loops.append((int(target[1], 16), address))
    innermost = [loop for loop in loops if not any(o != loop and loop[0] <= o[0] and o[1] <= loop[1] for o in loops)]
    return {'function': counts(instructions),
            'loops': [dict(counts([i for i in instructions if start <= i[0] <= end]), start=hex(start))
                      for start, end in innermost]}


def time(components, scenarios, samples):
    results = {}
    for scenario in scenarios:
        arguments = run.SCENARIOS[scenario][1]
        times = {side: [] for side in components}
        checksums = {side: set() for side in components}
        for sample in range(samples):
            order = list(components) if sample % 2 == 0 else list(reversed(components))
            for side in order:
                milliseconds, checksum = run.execute(['wasmtime', 'run', str(components[side]), *arguments])
                times[side].append(milliseconds)
                checksums[side].add(checksum)
        results[scenario] = {side: {'medianMs': statistics.median(times[side]), 'checksums': sorted(checksums[side])}
                             for side in components}
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--base', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--source', type=Path, default=run.REPOSITORY, help='tree whose compiler is measured')
    parser.add_argument('--output', type=Path, required=True, help='iteration root, reused across iterations')
    parser.add_argument('--label', required=True)
    parser.add_argument('--workload', choices=sorted(run.NAMES), required=True)
    parser.add_argument('--scenarios', nargs='+', choices=FUNCTIONS, required=True)
    parser.add_argument('--optimization', default='O3')
    parser.add_argument('--samples', type=int, default=7)
    parser.add_argument('--collector', choices=run.COLLECTORS, default='NetWasm',
                        help='the collector both sides use; NetWasmGC by default, the throughput collector')
    parser.add_argument('--corelib', action='store_true', help='also overlay a CoreLib built from the source tree')
    args = parser.parse_args()
    if any(run.SCENARIOS[scenario][0] != args.workload for scenario in args.scenarios):
        raise SystemExit(f'every scenario must belong to the {args.workload} workload')
    root = args.output.resolve()
    feed, version = Path(args.base[0]).resolve(), args.base[1]
    toolchain = json.loads((run.REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    sdk10, sdk11 = toolchain['sdk10BuildVersion'], toolchain['sdk11Version']
    cell_name = f'{args.workload}-{args.optimization.lower()}'
    name = run.NAMES[args.workload]

    base_packages = root / 'packages' / 'base'
    # Cells are kept per collector so a cached base never stands in for another collector's.
    cells = root / args.collector.lower()
    base_cell, base_twin = cells / 'base' / cell_name, cells / 'base-named' / cell_name
    # A changed workload source invalidates its cached base cells and desktop references.
    workload_digest = hashlib.sha256((run.ROOT / 'workloads' / args.workload / 'Program.cs').read_bytes()).hexdigest()
    base_stamp = cells / 'base' / f'{cell_name}.sha256'
    if not (base_twin / 'named').exists() or not stamped(base_stamp, workload_digest):
        if not base_packages.exists():
            # Restore once so the cache holds the real Binaryen tools.
            run.publish('restore', feed, version, args.workload, args.optimization, root, sdk10,
                        {'NUGET_PACKAGES': str(base_packages)}, collector=args.collector)
        publish_pair('base', feed, version, args.workload, args.optimization, cells, sdk10, base_packages,
                     args.collector)
        base_stamp.write_text(workload_digest)
    references_path = root / 'desktop' / f'{args.workload}.json'
    references_stamp = root / 'desktop' / f'{args.workload}.sha256'
    if not references_path.exists() or not stamped(references_stamp, workload_digest):
        shutil.rmtree(root / 'desktop' / args.workload, ignore_errors=True)
        assembly = run.desktop_reference(args.workload, root, sdk11, 'net11.0')
        references = {scenario: run.execute(['dotnet', str(assembly), *arguments])[1]
                      for scenario, (workload, arguments) in run.SCENARIOS.items() if workload == args.workload}
        references_path.write_text(json.dumps(references))
        references_stamp.write_text(workload_digest)
    references = json.loads(references_path.read_text())

    candidate_packages = root / 'packages' / 'candidate'
    if not candidate_packages.exists():
        shutil.copytree(base_packages, candidate_packages, symlinks=True)
    overlay(candidate_packages, version, build_tasks(args.source.resolve()))
    overlay_corelib(candidate_packages, version, build_corelib(args.source.resolve()) if args.corelib else None)
    candidate_cell, candidate_twin = publish_pair('candidate', feed, version, args.workload, args.optimization,
                                                  cells, sdk10, candidate_packages, args.collector)

    components = {'base': base_cell / 'out' / f'{name}.wasm', 'candidate': candidate_cell / 'out' / f'{name}.wasm'}
    timing = time(components, args.scenarios, args.samples)
    functions = {scenario: FUNCTIONS[scenario] for scenario in args.scenarios}
    iteration = root / 'iterations' / args.label
    native = {side: disassemble(named_core(cell, twin, name), functions, iteration / side)
              for side, cell, twin in (('base', base_cell, base_twin), ('candidate', candidate_cell, candidate_twin))}
    report = {'label': args.label, 'workload': args.workload, 'optimization': args.optimization,
              'collector': args.collector,
              'sizes': {side: component.stat().st_size for side, component in components.items()},
              'timing': timing, 'native': native}
    (iteration / 'report.json').write_text(json.dumps(report, indent=2))
    failed = False
    for scenario in args.scenarios:
        result = timing[scenario]
        matches = {side: result[side]['checksums'] == [references[scenario]] for side in result}
        failed |= not matches['candidate']
        loops = {side: (native[side][scenario] or {}).get('loops', []) for side in native}
        hottest = {side: max(loops[side], key=lambda loop: loop['instructions'], default=None) for side in loops}
        print(f"{scenario:17} base {result['base']['medianMs']:8.2f} ms  candidate {result['candidate']['medianMs']:8.2f} ms"
              f"  ratio {result['candidate']['medianMs'] / result['base']['medianMs']:.3f}"
              f"  desktop {'match' if matches['candidate'] else 'MISMATCH'}")
        for side in ('base', 'candidate'):
            function = (native[side][scenario] or {}).get('function')
            print(f"  {side:9} function {function}" if function else f"  {side:9} function not found")
            for loop in loops[side]:
                print(f"    loop {loop['start']}: {loop['instructions']} insts, {loop['stackLoads']}/{loop['stackStores']}"
                      f" stack loads/stores, {loop['conditionalBranches']} branches, {loop['calls']} calls")
    print(f'report: {iteration / "report.json"}')
    return 1 if failed else 0



if __name__ == '__main__':
    raise SystemExit(main())

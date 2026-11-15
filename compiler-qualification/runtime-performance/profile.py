#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

"""Attribute a workload's time to functions with Wasmtime's guest profiler.

Publishes each workload as the timed product plus a twin that keeps names at
every Binaryen step (see named-wasm-opt.py), profiles the product component under `wasmtime run
--profile=guest` over several runs, and names its main module's functions from
the twin, whose code must be identical. Each function is reported with the
share of samples in which it is the leaf (self) and in which it is anywhere on
the stack (inclusive). Results go to <output>/<scenario>.json.
"""

import argparse
from collections import Counter, defaultdict
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


run = load('runtime_performance', 'run.py')
iterate = load('runtime_iteration', 'iterate.py')


def unsigned(data, offset):
    result = shift = 0
    while True:
        byte = data[offset]
        offset += 1
        result |= (byte & 0x7f) << shift
        shift += 7
        if byte < 0x80:
            return result, offset


def skip_limits(data, offset):
    flags, offset = unsigned(data, offset)
    _, offset = unsigned(data, offset)
    if flags & 1:
        _, offset = unsigned(data, offset)
    return offset


def defined_function_names(core):
    """Returns a core module's function names by defined-function index, the
    index Wasmtime uses to label a function it has no name for."""
    data = core.read_bytes()
    offset, imported, names = 8, 0, {}
    while offset < len(data):
        section = data[offset]
        size, offset = unsigned(data, offset + 1)
        end = offset + size
        if section == 2:
            count, cursor = unsigned(data, offset)
            for _ in range(count):
                for _ in range(2):
                    length, cursor = unsigned(data, cursor)
                    cursor += length
                kind = data[cursor]
                cursor += 1
                if kind == 0:
                    imported += 1
                    _, cursor = unsigned(data, cursor)
                elif kind == 1:
                    cursor = skip_limits(data, cursor + 1)
                elif kind == 2:
                    cursor = skip_limits(data, cursor)
                elif kind == 3:
                    cursor += 2
                elif kind == 4:
                    _, cursor = unsigned(data, cursor + 1)
                else:
                    raise SystemExit(f'unsupported import kind {kind} in {core}')
        elif section == 0:
            length, cursor = unsigned(data, offset)
            if data[cursor:cursor + length] == b'name':
                cursor += length
                while cursor < end:
                    kind = data[cursor]
                    subsection, cursor = unsigned(data, cursor + 1)
                    if kind == 1:
                        count, entry = unsigned(data, cursor)
                        for _ in range(count):
                            index, entry = unsigned(data, entry)
                            length, entry = unsigned(data, entry)
                            names[index] = data[entry:entry + length].decode()
                            entry += length
                    cursor += subsection
        offset = end
    return {index - imported: name for index, name in names.items() if index >= imported}


def attribute(profile, names):
    """Returns (samples, self counts, inclusive counts) by function name. The
    resource with the most functions is the main module; its frames take their
    names from the named twin."""
    thread = profile['threads'][0]
    strings = thread['stringArray']
    stacks, frames, functions = thread['stackTable'], thread['frameTable'], thread['funcTable']
    by_resource = defaultdict(set)
    for function, resource in enumerate(functions['resource']):
        by_resource[resource].add(function)
    main = max(by_resource, key=lambda resource: len(by_resource[resource]))

    def label(function):
        text = strings[functions['name'][function]]
        if functions['resource'][function] == main and text.startswith('<wasm function '):
            return names.get(int(text[len('<wasm function '):-1]), text)
        return text

    name = [label(function) for function in range(len(functions['name']))]
    own, inclusive, samples = Counter(), Counter(), 0
    for stack in thread['samples']['stack']:
        if stack is None:
            continue
        samples += 1
        on_stack = []
        while stack is not None:
            on_stack.append(name[frames['func'][stacks['frame'][stack]]])
            stack = stacks['prefix'][stack]
        own[on_stack[0]] += 1
        inclusive.update(set(on_stack))
    return samples, own, inclusive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--collector', choices=run.COLLECTORS)
    parser.add_argument('--runtime', type=Path, help='a regenerated runtime pack runtime directory')
    parser.add_argument('--optimization', default='O3')
    parser.add_argument('--scenarios', nargs='+', choices=run.SCENARIOS, required=True)
    parser.add_argument('--interval', default='100us', help='the guest profiler sampling interval')
    parser.add_argument('--runs', type=int, default=20, help='profiled runs aggregated per scenario')
    parser.add_argument('--top', type=int, default=25)
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    toolchain = json.loads((run.REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    sdk10 = toolchain['sdk10BuildVersion']
    feed, version = Path(args.feed[0]).resolve(), args.feed[1]
    workloads = sorted({run.SCENARIOS[name][0] for name in args.scenarios})

    packages = root / 'packages'
    run.publish('restore', feed, version, workloads[0], args.optimization, root, sdk10,
                {'NUGET_PACKAGES': str(packages)}, collector=args.collector)
    if args.runtime:
        run.overlay_runtime(packages, version, args.runtime.resolve())
    components = {}
    for workload in workloads:
        cell = f'{workload}-{args.optimization.lower()}'
        product = run.publish('product', feed, version, workload, args.optimization, root, sdk10,
                              {'NUGET_PACKAGES': str(packages)}, collector=args.collector)
        run.publish('twin', feed, version, workload, args.optimization, root, sdk10,
                    dict(iterate.named_environment(packages, root / 'twin' / cell / 'named'), NETWASM_KEEP_NAMES='1'),
                    collector=args.collector)
        named = iterate.named_core(root / 'product' / cell, root / 'twin' / cell, run.NAMES[workload])
        components[workload] = product, defined_function_names(named)

    for name in args.scenarios:
        workload, arguments = run.SCENARIOS[name]
        component, names = components[workload]
        samples, own, inclusive = 0, Counter(), Counter()
        for _ in range(args.runs):
            path = root / f'{name}.profile.json'
            subprocess.run(['wasmtime', 'run', f'--profile=guest,{path},{args.interval}',
                            str(component), *arguments], check=True, capture_output=True)
            counted, run_own, run_inclusive = attribute(json.loads(path.read_text()), names)
            samples, own, inclusive = samples + counted, own + run_own, inclusive + run_inclusive
        report = {'scenario': name, 'collector': args.collector, 'runs': args.runs, 'samples': samples,
                  'functions': [{'name': function, 'self': own[function] / samples,
                                 'inclusive': inclusive[function] / samples}
                                for function in sorted(inclusive, key=lambda f: (-own[f], -inclusive[f]))]}
        (root / f'{name}.json').write_text(json.dumps(report, indent=2))
        print(f'{name}: {samples} samples over {args.runs} runs')
        for row in report['functions'][:args.top]:
            print(f"  {row['self']:6.1%} self  {row['inclusive']:6.1%} incl  {row['name']}")
    shutil.rmtree(packages)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

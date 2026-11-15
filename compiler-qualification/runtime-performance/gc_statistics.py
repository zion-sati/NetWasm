#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

"""Report NetWasmGC allocator statistics for a workload's timed region.

Needs a runtime regenerated with NETWASM_GC_STATISTICS defined, which reports
cumulative counters on stderr each time the allocated-bytes counter is read.
Every workload reads it on both sides of its timed region, so the last two
reports bracket that region: event counts are their difference, and memory
fields are the state at its end. Results go to <output>/statistics.json.
This build is diagnostic only; never time it.
"""

import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess

_spec = importlib.util.spec_from_file_location(
    'runtime_performance', Path(__file__).resolve().parent / 'run.py')
run = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(run)

# Fields that describe the heap when read rather than counting events.
STATE = {'object_count', 'large_object_count', 'live_payload_bytes', 'small_header_bytes',
         'small_padding_bytes', 'small_occupied_bytes', 'reserved_run_bytes', 'free_slot_bytes', 'block_count',
         'block_metadata_bytes', 'block_tail_bytes', 'empty_block_count', 'unused_arena_bytes', 'arena_bytes',
         'arena_header_bytes', 'large_bytes', 'directory_bytes'}


def reports(stderr):
    lines = [line.split('|')[1:] for line in stderr.splitlines() if line.startswith('NWGC-STATS|')]
    return [{key: int(value) for key, value in (field.split('=') for field in line)} for line in lines]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', nargs=2, metavar=('FEED', 'VERSION'), required=True)
    parser.add_argument('--runtime', type=Path, required=True, help='a statistics-enabled runtime directory')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--optimization', default='O3')
    parser.add_argument('--scenarios', nargs='+', choices=run.SCENARIOS,
                        default=['transaction-full', 'object-graph', 'ui-churn'])
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    toolchain = json.loads((run.REPOSITORY / 'eng' / 'csharp15-toolchain.json').read_text())
    feed, version = Path(args.feed[0]).resolve(), args.feed[1]
    workloads = sorted({run.SCENARIOS[name][0] for name in args.scenarios})
    packages = root / 'packages'
    environment = {'NUGET_PACKAGES': str(packages)}
    run.publish('restore', feed, version, workloads[0], args.optimization, root, toolchain['sdk10BuildVersion'],
                environment, collector='NetWasm')
    run.overlay_runtime(packages, version, args.runtime.resolve())
    components = {workload: run.publish('statistics', feed, version, workload, args.optimization, root,
                                        toolchain['sdk10BuildVersion'], environment, collector='NetWasm')
                  for workload in workloads}

    results = {}
    for name in args.scenarios:
        workload, arguments = run.SCENARIOS[name]
        completed = subprocess.run(['wasmtime', 'run', str(components[workload]), *arguments],
                                   capture_output=True, text=True, check=True)
        found = reports(completed.stderr)
        if len(found) < 2:
            raise SystemExit(f'{name} printed {len(found)} statistics reports; is the runtime instrumented?')
        before, after = found[-2], found[-1]
        results[name] = {key: after[key] if key in STATE else after[key] - before[key] for key in after}
    (root / 'statistics.json').write_text(json.dumps(results, indent=2))
    keys = list(next(iter(results.values())))
    print(f"| Statistic | {' | '.join(args.scenarios)} |")
    print(f"| --- |{' ---: |' * len(args.scenarios)}")
    for key in keys:
        print(f"| {key} | " + ' | '.join(f'{results[name][key]:,}' for name in args.scenarios) + ' |')
    shutil.rmtree(packages)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

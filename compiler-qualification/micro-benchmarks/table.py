#!/usr/bin/env python3
"""Writes the per-family micro-benchmark table, and every case's times, as Markdown.

usage: table.py <results.tsv> <output.md> [mono-removed cases file]

The results have run.py's columns: case, desktop_ns, mono_ns and netwasm_ns. Cases
that any runtime did not time are left out. Cases listed in the removed file, one
per line, are ones whose work Mono AOT deleted: they carry a dagger and stay out of
the NetWasm / Mono AOT means. Each mean is a geometric mean of per-case ratios."""

from __future__ import annotations

import csv
import math
import sys
from pathlib import Path

FAMILIES = [
    ('NetWasm workloads', lambda case: case.startswith('NetWasm.Workloads.')),
    ('Runtime', lambda case: not case.startswith('System.') or case.startswith('System.Tests.')),
    ('Math', lambda case: case.startswith('System.MathBenchmarks.')),
    ('`System.Memory` spans', lambda case: case.startswith('System.Memory.')),
    ('`System.Numerics` vectors', lambda case: case.startswith('System.Numerics.')),
    ('`System.Runtime.Intrinsics`', lambda case: case.startswith('System.Runtime.Intrinsics.')),
]


def read_cases(path):
    return {line.split('#')[0].strip() for line in Path(path).read_text().splitlines()
            if line.split('#')[0].strip()}


def read_rows(results):
    """(case, netwasm, mono, desktop) for every case all three runtimes timed."""
    rows = []
    with open(results) as source:
        for row in csv.DictReader(source, delimiter='\t'):
            try:
                values = float(row['netwasm_ns']), float(row['mono_ns']), float(row['desktop_ns'])
            except (KeyError, ValueError):
                continue
            if all(value > 0 for value in values):
                rows.append((row['case'], *values))
    return rows


def family(case):
    for name, matches in FAMILIES:
        if matches(case):
            return name
    return 'Runtime'


def geomean(values):
    return math.exp(sum(math.log(value) for value in values) / len(values))


def nanoseconds(value):
    return f'{value:,.1f}' if value < 1000 else f'{value:,.0f}'


def render(rows, removed):
    groups = {name: [] for name, _ in FAMILIES}
    for row in rows:
        groups[family(row[0])].append(row)

    def means(members):
        return (geomean([netwasm / mono for case, netwasm, mono, _ in members if case not in removed]),
                geomean([netwasm / desktop for _, netwasm, _, desktop in members]))

    lines = ['| Family | Cases | NetWasm / Mono AOT | NetWasm / desktop .NET 11 JIT |',
             '| --- | ---: | ---: | ---: |']
    for name, members in groups.items():
        if members:
            mono, desktop = means(members)
            lines.append(f'| {name} | {len(members):,} | {mono:.2f} | {desktop:.2f} |')
    mono, desktop = means(rows)
    lines.append(f'| All | {len(rows):,} | {mono:.2f} | {desktop:.2f} |')

    sections = []
    for name, members in groups.items():
        if not members:
            continue
        body = ['| Case | NetWasm (ns) | Mono AOT (ns) | Desktop JIT (ns) | NetWasm / Mono AOT | NetWasm / desktop |',
                '| --- | ---: | ---: | ---: | ---: | ---: |']
        for case, netwasm, mono, desktop in sorted(members):
            mark = ' †' if case in removed else ''
            body.append(f'| `{case.replace("|", chr(92) + "|")}`{mark} | {nanoseconds(netwasm)} | {nanoseconds(mono)} | '
                        f'{nanoseconds(desktop)} | {netwasm / mono:.2f} | {netwasm / desktop:.2f} |')
        sections.append(f'## {name}\n\n' + '\n'.join(body))
    return '\n'.join(lines) + '\n\n' + '\n\n'.join(sections) + '\n'


def write(results, output, removed):
    rows = read_rows(results)
    if not rows:
        raise SystemExit(f'{results} has no case that every runtime timed')
    Path(output).write_text(render(rows, removed))
    counts = {}
    for row in rows:
        counts[family(row[0])] = counts.get(family(row[0]), 0) + 1
    return f'{len(rows)} cases; ' + ', '.join(f'{name}: {count}' for name, count in counts.items())


if __name__ == '__main__':
    if len(sys.argv) not in (3, 4):
        raise SystemExit(__doc__)
    print(write(sys.argv[1], sys.argv[2], read_cases(sys.argv[3]) if len(sys.argv) == 4 else set()))

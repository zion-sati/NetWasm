#!/usr/bin/env python3
"""Merge Coverlet JSON reports by summing line and branch hits.

The compiler tests run in shards, each with its own Coverlet report, and the
other compiler test projects add theirs; the coverage baseline is checked
against the merged report.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


def merge_coverlet_reports(paths: list[Path], output: Path) -> None:
    merged: dict[str, object] = {}
    for path in paths:
        try:
            report = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise ValueError(f"Cannot read Coverlet report {path}: {error}") from error
        if not isinstance(report, dict):
            raise ValueError(f"Coverlet report {path} is not a JSON object.")
        merge_coverlet_report(merged, report, path)

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(f".{output.name}.tmp")
    temporary.write_text(json.dumps(merged, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output)


def merge_coverlet_report(
    merged: dict[str, object],
    report: dict[str, object],
    source: Path,
) -> None:
    for module_name, files_value in report.items():
        if not isinstance(files_value, dict):
            raise ValueError(f"Invalid file map for {module_name} in {source}.")
        module = merged.setdefault(module_name, {})
        if not isinstance(module, dict):
            raise ValueError(f"Conflicting module {module_name} in {source}.")
        for file_name, classes_value in files_value.items():
            if not isinstance(classes_value, dict):
                raise ValueError(f"Invalid class map for {file_name} in {source}.")
            classes = module.setdefault(file_name, {})
            if not isinstance(classes, dict):
                raise ValueError(f"Conflicting source file {file_name} in {source}.")
            for class_name, methods_value in classes_value.items():
                if not isinstance(methods_value, dict):
                    raise ValueError(f"Invalid method map for {class_name} in {source}.")
                methods = classes.setdefault(class_name, {})
                if not isinstance(methods, dict):
                    raise ValueError(f"Conflicting class {class_name} in {source}.")
                for method_name, coverage_value in methods_value.items():
                    merge_method_coverage(
                        methods,
                        method_name,
                        coverage_value,
                        source,
                    )


def merge_method_coverage(
    methods: dict[str, object],
    method_name: str,
    coverage_value: object,
    source: Path,
) -> None:
    if not isinstance(coverage_value, dict):
        raise ValueError(f"Invalid coverage for {method_name} in {source}.")
    lines_value = coverage_value.get("Lines")
    branches_value = coverage_value.get("Branches")
    if not isinstance(lines_value, dict) or not isinstance(branches_value, list):
        raise ValueError(f"Invalid Coverlet method shape for {method_name} in {source}.")

    coverage = methods.setdefault(method_name, {"Lines": {}, "Branches": []})
    if not isinstance(coverage, dict):
        raise ValueError(f"Conflicting method {method_name} in {source}.")
    lines = coverage.get("Lines")
    branches = coverage.get("Branches")
    if not isinstance(lines, dict) or not isinstance(branches, list):
        raise ValueError(f"Conflicting method shape for {method_name} in {source}.")

    for line, value in lines_value.items():
        hits = hit_count(value, "line", method_name, source)
        existing = lines.get(line, 0)
        if not isinstance(existing, int) or isinstance(existing, bool):
            raise ValueError(f"Conflicting line hit count for {method_name} in {source}.")
        lines[line] = existing + hits

    indexed_branches: dict[tuple[object, ...], dict[str, object]] = {}
    for branch in branches:
        if not isinstance(branch, dict):
            raise ValueError(f"Invalid branch coverage for {method_name} in {source}.")
        indexed_branches[branch_identity(branch, method_name, source)] = branch
    for branch_value in branches_value:
        if not isinstance(branch_value, dict):
            raise ValueError(f"Invalid branch coverage for {method_name} in {source}.")
        identity = branch_identity(branch_value, method_name, source)
        hits = hit_count(branch_value.get("Hits"), "branch", method_name, source)
        if identity in indexed_branches:
            existing = indexed_branches[identity]
            existing_hits = existing.get("Hits")
            if not isinstance(existing_hits, int) or isinstance(existing_hits, bool):
                raise ValueError(
                    f"Conflicting branch hit count for {method_name} in {source}."
                )
            existing["Hits"] = existing_hits + hits
        else:
            copy = dict(branch_value, Hits=hits)
            branches.append(copy)
            indexed_branches[identity] = copy


def hit_count(value: object, kind: str, method_name: str, source: Path) -> int:
    # Coverlet counts hits in a 32-bit integer, so code run more than 2^31
    # times in one report has a negative count, its count modulo 2^32.
    if not isinstance(value, int) or isinstance(value, bool) or not -(2**31) <= value < 2**32:
        raise ValueError(f"Invalid {kind} hit count for {method_name} in {source}.")
    return value + 2**32 if value < 0 else value


def branch_identity(
    branch: dict[str, object],
    method_name: str,
    source: Path,
) -> tuple[object, ...]:
    fields = ("Line", "Offset", "EndOffset", "Path", "Ordinal")
    if any(field not in branch for field in fields):
        raise ValueError(f"Incomplete branch coverage for {method_name} in {source}.")
    return tuple(branch[field] for field in fields)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("reports", type=Path, nargs="+")
    args = parser.parse_args(argv)
    try:
        merge_coverlet_reports(args.reports, args.output)
    except ValueError as error:
        parser.error(str(error))
    print(f"Merged {len(args.reports)} Coverlet reports into {args.output}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

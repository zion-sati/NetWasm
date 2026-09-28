#!/usr/bin/env python3
"""Merge compiler-test TRX results into deterministic class timing weights."""

from __future__ import annotations

import argparse
from collections import defaultdict
from decimal import Decimal, InvalidOperation
import json
from pathlib import Path
import xml.etree.ElementTree as ElementTree


TEST_NAMESPACE = "NetWasm.Compiler.Tests."


def parse_duration(value: str) -> Decimal:
    try:
        hours, minutes, seconds = value.split(":", 2)
        return Decimal(hours) * 3600 + Decimal(minutes) * 60 + Decimal(seconds)
    except (InvalidOperation, ValueError) as error:
        raise ValueError(f"invalid TRX duration: {value!r}") from error


def test_class(test_name: str) -> str | None:
    qualified_method = test_name.split("(", 1)[0]
    if not qualified_method.startswith(TEST_NAMESPACE) or "." not in qualified_method:
        return None
    return qualified_method.rsplit(".", 1)[0]


def merge(paths: list[Path]) -> dict[str, object]:
    durations: dict[str, Decimal] = defaultdict(Decimal)
    cases: dict[str, int] = defaultdict(int)
    for path in sorted(paths):
        try:
            root = ElementTree.parse(path).getroot()
        except (ElementTree.ParseError, OSError) as error:
            raise ValueError(f"cannot read TRX timing file {path}: {error}") from error
        for result in root.findall(".//{*}UnitTestResult"):
            class_name = test_class(result.attrib.get("testName", ""))
            if class_name is None:
                continue
            duration = result.attrib.get("duration")
            if duration is None:
                raise ValueError(f"TRX result has no duration in {path}: {result.attrib}")
            durations[class_name] += parse_duration(duration)
            cases[class_name] += 1

    if not cases:
        raise ValueError("TRX inputs contained no NetWasm compiler test results")
    return {
        "schemaVersion": 1,
        "classes": {
            name: {
                "cases": cases[name],
                "durationSeconds": float(durations[name]),
            }
            for name in sorted(cases)
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", type=Path, nargs="+")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = merge(args.inputs)
    except ValueError as error:
        parser.error(str(error))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Fails a test shard that did not run everything its plan selected.

create-compiler-test-shard-filter.py --plan-output records the shard's
selection: whole test methods, and single cases of the theories it splits. A
filter that matches less than it should, such as a display name VSTest cannot
select, would otherwise drop those tests from the run without a failure.

usage: check-compiler-test-shard.py <plan.json> <results.trx>..."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ElementTree

TRX_NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def load_plan(path: Path) -> list[tuple[str, str | None]]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read the shard plan: {error}") from error
    items = value.get("items") if isinstance(value, dict) else None
    if not isinstance(items, list) or not items:
        raise ValueError("The shard plan selects nothing.")
    plan = []
    for item in items:
        method = item.get("method") if isinstance(item, dict) else None
        display_name = item.get("displayName") if isinstance(item, dict) else None
        if not isinstance(method, str) or not (display_name is None or isinstance(display_name, str)):
            raise ValueError(f"Invalid shard plan item: {item!r}")
        plan.append((method, display_name))
    return plan


def load_results(paths: list[Path]) -> set[str]:
    names: set[str] = set()
    for path in paths:
        try:
            root = ElementTree.parse(path).getroot()
        except (OSError, ElementTree.ParseError) as error:
            raise ValueError(f"Cannot read test results {path}: {error}") from error
        names.update(
            result.get("testName", "")
            for result in root.iter(f"{TRX_NAMESPACE}UnitTestResult")
        )
    return names


def missing_items(
    plan: list[tuple[str, str | None]], results: set[str]
) -> list[str]:
    """The planned methods and cases without a result.

    A whole method ran when any of its results did: xUnit lists a theory whose
    data it cannot serialize as one test and reports each row it runs.
    """
    missing = []
    for method, display_name in plan:
        if display_name is not None:
            if display_name not in results:
                missing.append(display_name)
        elif not any(name == method or name.startswith(f"{method}(") for name in results):
            missing.append(method)
    return missing


def main() -> int:
    parser = argparse.ArgumentParser(description="Check that a test shard ran its whole plan.")
    parser.add_argument("plan", type=Path)
    parser.add_argument("results", type=Path, nargs="+")
    args = parser.parse_args()
    try:
        missing = missing_items(load_plan(args.plan), load_results(args.results))
    except ValueError as error:
        parser.error(str(error))
    if missing:
        print(
            f"The shard did not run {len(missing)} of the tests its filter selects:",
            file=sys.stderr,
        )
        for name in missing[:20]:
            print(f"  {name}", file=sys.stderr)
        if len(missing) > 20:
            print(f"  and {len(missing) - 20} more", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

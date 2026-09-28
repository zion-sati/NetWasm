#!/usr/bin/env python3
"""Verify that every gate required by the classified CI scope completed."""

from __future__ import annotations

import argparse


GATES = (
    "impact",
    "docs",
    "ci-infrastructure",
    "test",
    "timings",
    "package",
    "host-tools",
    "verify-packages",
    "runtime-pack-hosts",
)


def verify(scope: str, results: dict[str, str]) -> None:
    if results.get("impact") != "success":
        raise ValueError(f"impact classification failed: {results}")
    if scope == "docs":
        expected = {
            "docs": "success",
            "ci-infrastructure": "skipped",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        }
    elif scope == "ci":
        expected = {
            "docs": "skipped",
            "ci-infrastructure": "success",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        }
    elif scope == "full":
        expected = {
            "docs": "skipped",
            "ci-infrastructure": "skipped",
            "test": "success",
            "timings": "success",
            "package": "success",
            "host-tools": "success",
            "verify-packages": "success",
            "runtime-pack-hosts": "success",
        }
    elif scope == "reuse":
        expected = {
            "docs": "skipped",
            "ci-infrastructure": "skipped",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        }
    else:
        raise ValueError(f"unsupported CI impact scope: {scope!r}")

    failures = {
        name: (results.get(name), result)
        for name, result in expected.items()
        if results.get(name) != result
    }
    if failures:
        raise ValueError(f"required CI gates did not pass: {failures}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scope", required=True)
    for gate in GATES:
        parser.add_argument(f"--{gate}", required=True)
    args = parser.parse_args()
    results = {gate: getattr(args, gate.replace("-", "_")) for gate in GATES}
    try:
        verify(args.scope, results)
    except ValueError as error:
        parser.error(str(error))
    print(f"All required {args.scope} CI gates passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

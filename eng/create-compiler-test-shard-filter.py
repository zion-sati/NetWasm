#!/usr/bin/env python3

from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass
import json
import math
from pathlib import Path
import sys
from urllib.parse import quote


TEST_NAMESPACE = "NetWasm.Compiler.Tests."
DEFAULT_SPLIT_THRESHOLD_SECONDS = 300.0
FILTER_OPERATOR_CHARACTERS = frozenset("()&|=!~")


@dataclass(frozen=True)
class ShardItem:
    method_name: str
    display_name: str | None
    cases: int
    weight: float


def parse_test_cases(lines: list[str]) -> dict[str, list[str]]:
    cases: dict[str, list[str]] = {}
    for line in lines:
        if not line.startswith("    "):
            continue

        display_name = line.strip()
        if not display_name.startswith(TEST_NAMESPACE):
            continue

        qualified_method = display_name.split("(", 1)[0]
        if "." not in qualified_method:
            continue

        cases.setdefault(qualified_method, []).append(display_name)

    if not cases:
        raise ValueError("The test listing contained no NetWasm compiler tests.")

    return cases


def parse_test_method_counts(lines: list[str]) -> Counter[str]:
    return Counter({
        method_name: len(display_names)
        for method_name, display_names in parse_test_cases(lines).items()
    })


def create_shards(
    method_counts: Counter[str],
    shard_count: int,
    method_weights: dict[str, float] | None = None,
) -> list[list[str]]:
    if shard_count < 1:
        raise ValueError("Shard count must be at least one.")

    shards: list[list[str]] = [[] for _ in range(shard_count)]
    weights = (
        {method_name: float(count) for method_name, count in method_counts.items()}
        if method_weights is None
        else method_weights
    )
    if set(weights) != set(method_counts):
        raise ValueError("Shard weights must cover every discovered test method exactly once.")
    if any(weight <= 0 for weight in weights.values()):
        raise ValueError("Shard weights must be positive.")

    loads = [0.0] * shard_count
    for method_name in sorted(
        method_counts,
        key=lambda name: (-weights[name], name),
    ):
        shard_index = min(
            range(shard_count),
            key=lambda candidate: (loads[candidate], candidate),
        )
        shards[shard_index].append(method_name)
        loads[shard_index] += weights[method_name]

    return shards


def load_timing_weights(
    path: Path,
    method_counts: Counter[str],
) -> tuple[dict[str, float], int]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read compiler-test timing history: {error}") from error
    if not isinstance(value, dict) or value.get("schemaVersion") not in (2, 3):
        raise ValueError("Compiler-test timing history has an unsupported schema.")
    methods = value.get("methods")
    if not isinstance(methods, dict):
        raise ValueError("Compiler-test timing history has no method map.")

    known: dict[str, tuple[float, int]] = {}
    for method_name, timing in methods.items():
        if method_name not in method_counts:
            continue
        if not isinstance(timing, dict):
            raise ValueError(f"Invalid timing history for {method_name}.")
        duration = timing.get("durationSeconds")
        cases = timing.get("cases")
        if (
            not isinstance(duration, (int, float))
            or isinstance(duration, bool)
            or duration < 0
            or not math.isfinite(duration)
            or not isinstance(cases, int)
            or isinstance(cases, bool)
            or cases <= 0
        ):
            raise ValueError(f"Invalid timing history for {method_name}.")
        if duration > 0:
            known[method_name] = (float(duration), cases)

    if not known:
        raise ValueError("Compiler-test timing history matches no discovered test methods.")
    seconds_per_case = sum(item[0] for item in known.values()) / sum(
        item[1] for item in known.values()
    )
    weights = {
        method_name: known[method_name][0]
        if method_name in known
        else count * seconds_per_case
        for method_name, count in method_counts.items()
    }
    return weights, len(method_counts) - len(known)


def load_case_timing_weights(path: Path) -> dict[str, float]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read compiler-test timing history: {error}") from error
    if not isinstance(value, dict) or value.get("schemaVersion") != 3:
        return {}
    cases = value.get("cases")
    if not isinstance(cases, dict):
        raise ValueError("Compiler-test timing history has no case map.")

    weights: dict[str, float] = {}
    for display_name, duration in cases.items():
        if not isinstance(display_name, str):
            raise ValueError("Invalid compiler-test case timing history.")
        if (
            not isinstance(duration, (int, float))
            or isinstance(duration, bool)
            or duration < 0
            or not math.isfinite(duration)
        ):
            raise ValueError(f"Invalid timing history for {display_name}.")
        if duration > 0:
            weights[display_name] = float(duration)
    return weights


def case_filter_arguments(method_name: str, display_name: str) -> str | None:
    prefix = f"{method_name}("
    if not display_name.startswith(prefix) or not display_name.endswith(")"):
        return None
    arguments = display_name[len(prefix):-1]
    if not arguments or any(
        character in arguments for character in FILTER_OPERATOR_CHARACTERS
    ):
        return None
    return arguments


def create_execution_items(
    test_cases: dict[str, list[str]],
    method_weights: dict[str, float],
    case_weights: dict[str, float] | None = None,
    split_threshold_seconds: float = DEFAULT_SPLIT_THRESHOLD_SECONDS,
) -> list[ShardItem]:
    if split_threshold_seconds <= 0 or not math.isfinite(split_threshold_seconds):
        raise ValueError("Split threshold must be a positive finite duration.")
    if set(method_weights) != set(test_cases):
        raise ValueError("Method weights must cover every discovered test method exactly once.")

    known_cases = case_weights or {}
    items: list[ShardItem] = []
    for method_name in sorted(test_cases):
        display_names = test_cases[method_name]
        method_weight = method_weights[method_name]
        arguments = [case_filter_arguments(method_name, name) for name in display_names]
        can_split = (
            len(display_names) > 1
            and len(display_names) == len(set(display_names))
            and method_weight > split_threshold_seconds
            and all(value is not None for value in arguments)
        )
        if not can_split:
            items.append(ShardItem(method_name, None, len(display_names), method_weight))
            continue

        fallback_weight = method_weight / len(display_names)
        for display_name in display_names:
            items.append(ShardItem(
                method_name,
                display_name,
                1,
                known_cases.get(display_name, fallback_weight),
            ))
    return items


def create_execution_shards(
    items: list[ShardItem],
    shard_count: int,
) -> list[list[ShardItem]]:
    if shard_count < 1:
        raise ValueError("Shard count must be at least one.")
    if not items:
        raise ValueError("The execution plan contains no tests.")
    if any(item.weight <= 0 or not math.isfinite(item.weight) for item in items):
        raise ValueError("Shard weights must be positive finite durations.")

    shards: list[list[ShardItem]] = [[] for _ in range(shard_count)]
    loads = [0.0] * shard_count
    for item in sorted(
        items,
        key=lambda value: (-value.weight, value.method_name, value.display_name or ""),
    ):
        shard_index = min(
            range(shard_count),
            key=lambda candidate: (loads[candidate], candidate),
        )
        shards[shard_index].append(item)
        loads[shard_index] += item.weight
    return shards


def create_vstest_filter(items: list[str | ShardItem]) -> str:
    if not items:
        raise ValueError("The selected shard contains no test methods.")

    clauses: list[str] = []
    for value in items:
        item = value if isinstance(value, ShardItem) else ShardItem(value, None, 1, 1.0)
        if item.display_name is None:
            clauses.append(f"FullyQualifiedName={item.method_name}")
            continue
        arguments = case_filter_arguments(item.method_name, item.display_name)
        if arguments is None:
            raise ValueError(f"Cannot select discovered test case exactly: {item.display_name}")
        clauses.append(
            f"(FullyQualifiedName={item.method_name}"
            f"&DisplayName~{quote(arguments, safe='.-_')})"
        )
    return "|".join(sorted(clauses))


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Create a deterministic VSTest filter for one compiler-test shard."
        )
    )
    parser.add_argument("test_listing", type=Path)
    parser.add_argument("--shard-count", type=int, required=True)
    parser.add_argument("--shard-index", type=int, required=True)
    parser.add_argument("--timings", type=Path)
    parser.add_argument(
        "--split-threshold-seconds",
        type=float,
        default=DEFAULT_SPLIT_THRESHOLD_SECONDS,
    )
    args = parser.parse_args()

    if args.shard_index < 0 or args.shard_index >= args.shard_count:
        parser.error("--shard-index must be within the configured shard count")

    try:
        test_cases = parse_test_cases(
            args.test_listing.read_text(encoding="utf-8").splitlines()
        )
        method_counts = Counter({name: len(cases) for name, cases in test_cases.items()})
        weights = None
        case_weights: dict[str, float] = {}
        fallback_methods = len(method_counts)
        if args.timings is not None:
            weights, fallback_methods = load_timing_weights(args.timings, method_counts)
            case_weights = load_case_timing_weights(args.timings)
        method_weights = weights or {
            name: float(count) for name, count in method_counts.items()
        }
        items = create_execution_items(
            test_cases,
            method_weights,
            case_weights,
            args.split_threshold_seconds,
        )
        shards = create_execution_shards(items, args.shard_count)
        selected_items = shards[args.shard_index]
        selected_cases = sum(item.cases for item in selected_items)
        selected_weight = sum(item.weight for item in selected_items)
        print(create_vstest_filter(selected_items))
        print(
            (
                f"compiler-test shard {args.shard_index + 1}/{args.shard_count}: "
                f"{len(selected_items)} scheduling units, {selected_cases} discovered cases, "
                f"projected weight {selected_weight:.3f}; "
                f"{fallback_methods} methods use discovered-case fallback"
            ),
            file=sys.stderr,
        )
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3

from __future__ import annotations

import argparse
from collections import Counter
import json
import math
from pathlib import Path
import sys


TEST_NAMESPACE = "NetWasm.Compiler.Tests."


def parse_test_class_counts(lines: list[str]) -> Counter[str]:
    counts: Counter[str] = Counter()
    for line in lines:
        if not line.startswith("    "):
            continue

        display_name = line.strip()
        if not display_name.startswith(TEST_NAMESPACE):
            continue

        qualified_method = display_name.split("(", 1)[0]
        if "." not in qualified_method:
            continue

        class_name = qualified_method.rsplit(".", 1)[0]
        counts[class_name] += 1

    if not counts:
        raise ValueError("The test listing contained no NetWasm compiler tests.")

    return counts


def create_shards(
    class_counts: Counter[str],
    shard_count: int,
    class_weights: dict[str, float] | None = None,
) -> list[list[str]]:
    if shard_count < 1:
        raise ValueError("Shard count must be at least one.")

    shards: list[list[str]] = [[] for _ in range(shard_count)]
    weights = (
        {class_name: float(count) for class_name, count in class_counts.items()}
        if class_weights is None
        else class_weights
    )
    if set(weights) != set(class_counts):
        raise ValueError("Shard weights must cover every discovered test class exactly once.")
    if any(weight <= 0 for weight in weights.values()):
        raise ValueError("Shard weights must be positive.")

    loads = [0.0] * shard_count
    for class_name in sorted(
        class_counts,
        key=lambda name: (-weights[name], name),
    ):
        shard_index = min(
            range(shard_count),
            key=lambda candidate: (loads[candidate], candidate),
        )
        shards[shard_index].append(class_name)
        loads[shard_index] += weights[class_name]

    return shards


def load_timing_weights(
    path: Path,
    class_counts: Counter[str],
) -> tuple[dict[str, float], int]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Cannot read compiler-test timing history: {error}") from error
    if not isinstance(value, dict) or value.get("schemaVersion") != 1:
        raise ValueError("Compiler-test timing history has an unsupported schema.")
    classes = value.get("classes")
    if not isinstance(classes, dict):
        raise ValueError("Compiler-test timing history has no class map.")

    known: dict[str, tuple[float, int]] = {}
    for class_name, timing in classes.items():
        if class_name not in class_counts:
            continue
        if not isinstance(timing, dict):
            raise ValueError(f"Invalid timing history for {class_name}.")
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
            raise ValueError(f"Invalid timing history for {class_name}.")
        if duration > 0:
            known[class_name] = (float(duration), cases)

    if not known:
        raise ValueError("Compiler-test timing history matches no discovered test classes.")
    seconds_per_case = sum(item[0] for item in known.values()) / sum(
        item[1] for item in known.values()
    )
    weights = {
        class_name: known[class_name][0]
        if class_name in known
        else count * seconds_per_case
        for class_name, count in class_counts.items()
    }
    return weights, len(class_counts) - len(known)


def create_vstest_filter(class_names: list[str]) -> str:
    if not class_names:
        raise ValueError("The selected shard contains no test classes.")

    return "|".join(
        f"FullyQualifiedName~{class_name}." for class_name in sorted(class_names)
    )


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
    args = parser.parse_args()

    if args.shard_index < 0 or args.shard_index >= args.shard_count:
        parser.error("--shard-index must be within the configured shard count")

    try:
        class_counts = parse_test_class_counts(
            args.test_listing.read_text(encoding="utf-8").splitlines()
        )
        weights = None
        fallback_classes = len(class_counts)
        if args.timings is not None:
            weights, fallback_classes = load_timing_weights(args.timings, class_counts)
        shards = create_shards(class_counts, args.shard_count, weights)
        selected_classes = shards[args.shard_index]
        selected_cases = sum(class_counts[name] for name in selected_classes)
        selected_weight = sum(
            weights[name] if weights is not None else class_counts[name]
            for name in selected_classes
        )
        print(create_vstest_filter(selected_classes))
        print(
            (
                f"compiler-test shard {args.shard_index + 1}/{args.shard_count}: "
                f"{len(selected_classes)} classes, {selected_cases} discovered cases, "
                f"projected weight {selected_weight:.3f}; "
                f"{fallback_classes} classes use discovered-case fallback"
            ),
            file=sys.stderr,
        )
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

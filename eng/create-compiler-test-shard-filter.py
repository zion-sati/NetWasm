#!/usr/bin/env python3

from __future__ import annotations

import argparse
from collections import Counter
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
) -> list[list[str]]:
    if shard_count < 1:
        raise ValueError("Shard count must be at least one.")

    shards: list[list[str]] = [[] for _ in range(shard_count)]
    loads = [0] * shard_count
    for class_name, count in sorted(
        class_counts.items(),
        key=lambda item: (-item[1], item[0]),
    ):
        shard_index = min(
            range(shard_count),
            key=lambda candidate: (loads[candidate], candidate),
        )
        shards[shard_index].append(class_name)
        loads[shard_index] += count

    return shards


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
    args = parser.parse_args()

    if args.shard_index < 0 or args.shard_index >= args.shard_count:
        parser.error("--shard-index must be within the configured shard count")

    try:
        class_counts = parse_test_class_counts(
            args.test_listing.read_text(encoding="utf-8").splitlines()
        )
        shards = create_shards(class_counts, args.shard_count)
        selected_classes = shards[args.shard_index]
        selected_cases = sum(class_counts[name] for name in selected_classes)
        print(create_vstest_filter(selected_classes))
        print(
            (
                f"compiler-test shard {args.shard_index + 1}/{args.shard_count}: "
                f"{len(selected_classes)} classes, {selected_cases} discovered cases"
            ),
            file=sys.stderr,
        )
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

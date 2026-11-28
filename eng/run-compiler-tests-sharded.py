#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT
"""Runs NetWasm.Compiler.Tests in isolated, duration-balanced processes: one per logical CPU, each running its tests
one at a time. Every test compiles a whole program, so one test process running many of them in parallel fragments
its large object heap until the GC collects back to back, and is far slower than several processes; `dotnet test` on
the project refuses an unfiltered run and points here (NetWasmTestsInOneProcess). --filter takes `dotnet test`'s
filter syntax and runs only the tests it selects.

It builds the test project first unless --no-build.

usage: eng/run-compiler-tests-sharded.py [--filter FILTER] [--results-directory DIRECTORY] [--shard-count N] [--no-build]"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time


DEFAULT_CONFIGURATION = "Release"
SHARD_COUNT_ENVIRONMENT_VARIABLE = "NETWASM_COMPILER_TEST_PROCESSES"
BUILD_CONFIGURATION_ENVIRONMENT_VARIABLE = "NETWASM_TEST_BUILD_CONFIGURATION"
REPOSITORY_ROOT_ENVIRONMENT_VARIABLE = "NETWASM_TEST_REPOSITORY_ROOT"
# The property that lets one test process run an unfiltered selection (tests/NetWasm.Compiler.Tests): each shard is one
# process by design, and selects its tests through a run settings filter rather than --filter.
ONE_PROCESS_PROPERTY = "-p:NetWasmTestsInOneProcess=true"
DEPENDENCY_ROOT_ENVIRONMENT_VARIABLE = "NETWASM_DEPENDENCY_ROOT"


def load_engineering_module(source: Path, file_name: str, module_name: str):
    script = source / "eng" / file_name
    spec = importlib.util.spec_from_file_location(module_name, script)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {script}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def load_shard_module(source: Path):
    return load_engineering_module(
        source, "create-compiler-test-shard-filter.py", "compiler_test_shards")


# The memory one shard process needs at its peak, with the compiler, Node.js and wasm-opt it starts: eight shards of
# the whole project peaked at 43 GiB together on Linux x64.
MEMORY_PER_SHARD = 8 * 1024 ** 3


def physical_memory() -> int | None:
    try:
        return os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    except (AttributeError, OSError, ValueError):
        return None


def resolve_shard_count(method_count: int, value: str | None = None, memory: int | None = None) -> int:
    requested = value
    if requested is None:
        requested = os.environ.get(SHARD_COUNT_ENVIRONMENT_VARIABLE)
    if requested is None:
        # One shard per logical CPU, each running its tests one at a time (see run_shard): a
        # test spawns the compiler and Node.js, so shards times in-shard parallelism, not shards
        # alone, is what competes for the CPUs. No more than the machine's memory holds at their
        # peak (MEMORY_PER_SHARD), so the default never runs it out of memory.
        workers = os.cpu_count() or 1
        memory = physical_memory() if memory is None else memory
        if memory is not None:
            workers = max(1, min(workers, memory // MEMORY_PER_SHARD))
    else:
        try:
            workers = int(requested)
        except ValueError as error:
            raise ValueError(
                f"{SHARD_COUNT_ENVIRONMENT_VARIABLE} must be a positive integer."
            ) from error
        if workers < 1:
            raise ValueError(
                f"{SHARD_COUNT_ENVIRONMENT_VARIABLE} must be a positive integer."
            )
    return min(workers, method_count)


def test_command(
    dotnet: str,
    project: Path,
    configuration: str,
    *arguments: str,
) -> list[str]:
    return [
        dotnet,
        "test",
        str(project),
        "-c",
        configuration,
        "--no-build",
        "--no-restore",
        "--disable-build-servers",
        "--nologo",
        ONE_PROCESS_PROPERTY,
        *arguments,
    ]


def find_test_output(project: Path, configuration: str) -> Path:
    candidates = [
        path.parent
        for path in (project.parent / "bin" / configuration).glob(
            "*/NetWasm.Compiler.Tests.dll"
        )
        if (path.parent / "NetWasm.Compiler.Tests.runtimeconfig.json").is_file()
    ]
    if len(candidates) != 1:
        raise ValueError(
            "Expected exactly one built NetWasm.Compiler.Tests output directory, "
            f"found {len(candidates)}."
        )
    return candidates[0]


def run_shard(
    *,
    index: int,
    total: int,
    items: list[object],
    source: Path,
    results: Path,
    dotnet: str,
    configuration: str,
    shard_module,
    coverage: bool,
    shared_test_output: Path | None,
    dependency_root: Path,
) -> dict[str, object]:
    shard_root = results / f"shard-{index + 1:02d}"
    if shard_root.exists():
        shutil.rmtree(shard_root)
    temporary = shard_root / "tmp"
    receipts = shard_root / "linked-receipts"
    test_results = shard_root / "test-results"
    for directory in (temporary, receipts, test_results):
        directory.mkdir(parents=True, exist_ok=True)

    log_path = shard_root / "test.log"
    environment = os.environ.copy()
    environment["TMPDIR"] = str(temporary)
    environment["NETWASM_CORPUS_RECEIPTS"] = str(receipts)
    environment[BUILD_CONFIGURATION_ENVIRONMENT_VARIABLE] = configuration
    environment[REPOSITORY_ROOT_ENVIRONMENT_VARIABLE] = str(source)
    environment[DEPENDENCY_ROOT_ENVIRONMENT_VARIABLE] = str(dependency_root)
    settings_path = shard_root / "filter.runsettings"
    shard_module.write_run_settings(settings_path, items)
    project = source / "tests" / "NetWasm.Compiler.Tests" / "NetWasm.Compiler.Tests.csproj"
    isolated_test_output = shard_root / "test-bin"
    coverage_arguments: tuple[str, ...] = ()
    # A shard is one worker: xUnit runs its tests serially, as the shard count assumes.
    run_settings: tuple[str, ...] = ("xUnit.MaxParallelThreads=1",)
    if coverage:
        if shared_test_output is None:
            raise ValueError("A built test output is required when collecting coverage.")
        shutil.copytree(shared_test_output, isolated_test_output)
        coverage_arguments = (
            f"-p:OutputPath={isolated_test_output}{os.sep}",
            "--collect",
            "XPlat Code Coverage",
        )
        run_settings += (
            "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=json",
        )
    command = test_command(
        dotnet,
        project,
        configuration,
        "--settings",
        str(settings_path),
        "--results-directory",
        str(test_results),
        "--logger",
        f"trx;LogFileName=compiler-{index + 1:02d}.trx",
        *coverage_arguments,
        "--",
        *run_settings,
    )

    started = time.monotonic()
    try:
        with log_path.open("w", encoding="utf-8") as log:
            completed = subprocess.run(
                command,
                cwd=source,
                env=environment,
                stdout=log,
                stderr=subprocess.STDOUT,
                text=True,
                check=False,
            )
    finally:
        if isolated_test_output.exists():
            shutil.rmtree(isolated_test_output)
    coverage_reports = list(test_results.glob("*/coverage.json")) if coverage else []
    if completed.returncode == 0 and coverage and len(coverage_reports) != 1:
        with log_path.open("a", encoding="utf-8") as log:
            log.write(
                f"\nExpected one Coverlet JSON report, found {len(coverage_reports)}.\n"
            )
        exit_code = 1
    else:
        exit_code = completed.returncode
    outcome: dict[str, object] = {
        "index": index,
        "total": total,
        "methods": len({item.method_name for item in items}),
        "schedulingUnits": len(items),
        "cases": sum(item.cases for item in items),
        "seconds": round(time.monotonic() - started, 3),
        "exitCode": exit_code,
        "log": str(log_path),
    }
    if len(coverage_reports) == 1:
        outcome["coverage"] = str(coverage_reports[0])
    return outcome


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Run NetWasm compiler tests in isolated, duration-balanced local processes."
        )
    )
    repository = Path(__file__).resolve().parents[1]
    parser.add_argument("--source", type=Path, default=repository)
    parser.add_argument("--results-directory", type=Path,
                        default=repository / "artifacts" / "compiler-tests")
    # dotnet test's filter syntax: only the tests it selects run.
    parser.add_argument("--filter")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--configuration", default=DEFAULT_CONFIGURATION)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--shard-count", type=int)
    parser.add_argument("--dependency-root", type=Path)
    parser.add_argument("--coverage-output", type=Path)
    # The pull request tier: a file of test classes, one per line (eng/compiler-test-pr-tier.txt), as CI selects them.
    parser.add_argument("--classes", type=Path)
    args = parser.parse_args()

    source = args.source.resolve()
    results = args.results_directory.resolve()
    results.mkdir(parents=True, exist_ok=True)
    dependency_root = (
        args.dependency_root.resolve()
        if args.dependency_root is not None
        else results / "native-dependencies"
    )
    dependency_root.mkdir(parents=True, exist_ok=True)
    shard_module = load_shard_module(source)
    project = source / "tests" / "NetWasm.Compiler.Tests" / "NetWasm.Compiler.Tests.csproj"
    listing_path = results / "compiler-tests.list"
    if not args.no_build:
        build = subprocess.run(
            [args.dotnet, "build", str(project), "-c", args.configuration, "--nologo", "-v", "q",
             "--disable-build-servers"],
            cwd=source, env=os.environ.copy(), check=False,
        )
        if build.returncode != 0:
            return build.returncode

    listing = subprocess.run(
        test_command(
            args.dotnet,
            project,
            args.configuration,
            "--list-tests",
            *(("--filter", args.filter) if args.filter else ()),
        ),
        cwd=source,
        env=os.environ.copy(),
        capture_output=True,
        text=True,
        check=False,
    )
    listing_path.write_text(listing.stdout + listing.stderr, encoding="utf-8")
    if listing.returncode != 0:
        sys.stderr.write(listing_path.read_text(encoding="utf-8"))
        return listing.returncode

    try:
        test_cases = shard_module.parse_test_cases(
            listing_path.read_text(encoding="utf-8").splitlines()
        )
        if args.classes is not None:
            test_cases = shard_module.select_classes(
                test_cases, shard_module.load_class_list(args.classes.resolve())
            )
        method_counts = shard_module.Counter({
            name: len(cases) for name, cases in test_cases.items()
        })
        requested = str(args.shard_count) if args.shard_count is not None else None
        shard_count = resolve_shard_count(len(method_counts), requested)
        timings_path = source / "eng" / "compiler-test-durations.json"
        weights, fallback_methods = shard_module.load_timing_weights(
            timings_path,
            method_counts,
        )
        case_weights = shard_module.load_case_timing_weights(timings_path)
        items = shard_module.create_execution_items(
            test_cases,
            weights,
            case_weights,
        )
        shards = shard_module.create_execution_shards(items, shard_count)
        shared_test_output = (
            find_test_output(project, args.configuration)
            if args.coverage_output is not None
            else None
        )
    except (OSError, UnicodeError, ValueError) as error:
        parser.error(str(error))

    selected_cases: list[str] = []
    for shard in shards:
        for item in shard:
            if item.display_name is None:
                selected_cases.extend(test_cases[item.method_name])
            else:
                selected_cases.append(item.display_name)
    discovered_cases = [name for cases in test_cases.values() for name in cases]
    if sorted(selected_cases) != sorted(discovered_cases):
        parser.error("Compiler-test shards do not cover discovered test cases exactly once.")

    print(
        f"Running {sum(method_counts.values())} compiler tests from "
        f"{len(method_counts)} methods in {shard_count} isolated processes "
        f"({fallback_methods} methods use discovered-case timing fallback).",
        flush=True,
    )
    started = time.monotonic()
    outcomes: list[dict[str, object]] = []
    with ThreadPoolExecutor(max_workers=shard_count) as executor:
        futures = [
            executor.submit(
                run_shard,
                index=index,
                total=shard_count,
                items=items,
                source=source,
                results=results,
                dotnet=args.dotnet,
                configuration=args.configuration,
                shard_module=shard_module,
                coverage=args.coverage_output is not None,
                shared_test_output=shared_test_output,
                dependency_root=dependency_root,
            )
            for index, items in enumerate(shards)
        ]
        for future in as_completed(futures):
            outcome = future.result()
            outcomes.append(outcome)
            print(
                f"Compiler shard {int(outcome['index']) + 1}/{shard_count} "
                f"finished in {outcome['seconds']:.1f}s "
                f"with exit code {outcome['exitCode']}.",
                flush=True,
            )

    outcomes.sort(key=lambda item: int(item["index"]))
    summary = {
        "schemaVersion": 1,
        "shardCount": shard_count,
        "discoveredMethods": len(method_counts),
        "discoveredCases": sum(method_counts.values()),
        "durationSeconds": round(time.monotonic() - started, 3),
        "shards": outcomes,
    }
    (results / "summary.json").write_text(
        json.dumps(summary, indent=2) + "\n",
        encoding="utf-8",
    )

    if args.coverage_output is not None:
        coverage_output = args.coverage_output.resolve()
        coverage_reports = [
            Path(str(outcome["coverage"]))
            for outcome in outcomes
        ]
        if coverage_output.is_file():
            coverage_reports.insert(0, coverage_output)
        try:
            load_engineering_module(
                source, "merge-coverlet-reports.py", "coverlet_reports"
            ).merge_coverlet_reports(coverage_reports, coverage_output)
        except ValueError as error:
            print(f"Cannot merge compiler coverage: {error}", file=sys.stderr)
            return 1

    failed = [outcome for outcome in outcomes if outcome["exitCode"] != 0]
    if failed:
        for outcome in failed:
            log_path = Path(str(outcome["log"]))
            print(
                f"\nCompiler shard {int(outcome['index']) + 1}/{shard_count} failed; "
                f"log follows ({log_path}):",
                file=sys.stderr,
            )
            sys.stderr.write(log_path.read_text(encoding="utf-8"))
        return 1

    print(
        f"All compiler shards passed in {summary['durationSeconds']:.1f}s. "
        f"Logs and TRX files: {results}",
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

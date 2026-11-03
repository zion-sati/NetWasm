#!/usr/bin/env python3
"""Builds DoomSharp's render benchmark at every optimization preset with each
collector against candidate packages, runs it, and requires every batch to
render the pixel checksum desktop .NET renders.

usage: qualify-doomsharp.py <doomsharp checkout> <package directory> <version>

The checkout is the commit eng/doomsharp.json pins. Its tools/NetWasmBench reducer
first renders the same frames on desktop .NET and writes the benchmark's WAD
data; each cell then builds its own copy of tools/NetWasmBench beside it, so the
engine sources resolve as they do for the original."""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

PRESETS = ("O0", "O1", "O2", "O3", "Os", "Oz")
COLLECTORS = ("NetWasm", "Compact")
FRAMES = 120
BATCHES = 2
BATCH = re.compile(r"^batch (\d+) map \S+ frames (\d+) ms ([0-9.]+) checksum (-?\d+)$", re.MULTILINE)


def cells() -> list[tuple[str, str]]:
    return [(preset, collector) for preset in PRESETS for collector in COLLECTORS]


def checksums(output: str) -> list[str]:
    """Each batch's checksum, in batch order, from the benchmark's output."""
    batches = [(int(match[1]), int(match[2]), match[4]) for match in BATCH.finditer(output)]
    if [batch for batch, _, _ in batches] != list(range(len(batches))):
        raise ValueError(f"batches out of order: {output!r}")
    if any(frames != FRAMES for _, frames, _ in batches):
        raise ValueError(f"a batch rendered other than {FRAMES} frames: {output!r}")
    return [checksum for _, _, checksum in batches]


def run(command: list[str], cwd: Path, environment: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=cwd, env=environment, capture_output=True, text=True, check=False)


def qualify(bench: Path, preset: str, collector: str, expected: str, environment: dict[str, str]) -> str | None:
    project = bench.parent / f"Bench-{preset}-{collector}"
    shutil.rmtree(project, ignore_errors=True)
    shutil.copytree(bench, project, ignore=shutil.ignore_patterns("bin", "obj", "Reducer", "e1m1.wad"))
    properties = [f"-p:NetWasmOptimization={preset}", f"-p:NetWasmGarbageCollector={collector}"]
    build = run(["dotnet", "build", "DoomBench.csproj", "-c", "Release", "--disable-build-servers", "-nologo",
                 *properties], project, environment)
    if build.returncode != 0:
        errors = sorted({line.strip() for line in build.stdout.splitlines() if ": error " in line})
        return "build failed:\n    " + "\n    ".join(errors[:10] or [build.stdout[-2000:]])
    executed = run(["dotnet", "run", "--project", "DoomBench.csproj", "-c", "Release", "--no-build",
                    "--disable-build-servers", *properties, "--", str(FRAMES), str(BATCHES)], project, environment)
    try:
        rendered = checksums(executed.stdout)
    except ValueError as error:
        return str(error)
    if executed.returncode != 0 or len(rendered) != BATCHES or any(value != expected for value in rendered):
        return (f"exit {executed.returncode}, checksums {rendered}, expected {expected} in each of {BATCHES}\n"
                f"{executed.stdout[-2000:]}{executed.stderr[-2000:]}")
    shutil.rmtree(project)
    return None


def configure(bench: Path, packages: Path, version: str) -> None:
    """Points the benchmark at the candidate SDK and packages, and stops MSBuild's
    search for Directory.Build files at it, so a checkout inside another
    repository, as CI's is inside NetWasm's, takes none of that repository's
    build properties, such as its package version defaults."""
    (bench / "global.json").write_text(json.dumps(
        {"sdk": {"version": "10.0.100", "rollForward": "latestFeature"},
         "msbuild-sdks": {"NetWasm.Sdk": version}}, indent=2) + "\n")
    (bench / "NuGet.Config").write_text(
        "<configuration>\n  <packageSources>\n    <clear />\n"
        f"    <add key=\"candidate\" value=\"{packages}\" />\n"
        "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />\n"
        "  </packageSources>\n</configuration>\n")
    for name in ("Directory.Build.props", "Directory.Build.targets"):
        (bench / name).write_text("<Project />\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("checkout", type=Path)
    parser.add_argument("packages", type=Path)
    parser.add_argument("version")
    parser.add_argument("--parallel", type=int, default=4)
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    pin = json.loads((root / "eng" / "doomsharp.json").read_text())
    checkout = arguments.checkout.resolve()
    bench = checkout / "tools" / "NetWasmBench"
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=checkout, capture_output=True, text=True,
                          check=True).stdout.strip()
    if head != pin["commit"]:
        raise SystemExit(f"{checkout} is at {head}, not the pinned {pin['commit']}")

    reduced = run(["dotnet", "run", "--project", "Reducer", "-c", "Release", "--",
                   str(checkout / "assets" / "freedoom" / "freedoom1.wad"), str(bench)], bench, dict(os.environ))
    if reduced.returncode != 0 or f"checksum {pin['checksum']} " not in reduced.stdout:
        raise SystemExit(f"desktop .NET did not render checksum {pin['checksum']}:\n{reduced.stdout}{reduced.stderr}")

    configure(bench, arguments.packages.resolve(), arguments.version)
    environment = dict(os.environ, NUGET_PACKAGES=str(bench.parent / ".packages"))

    # One restore before the parallel builds share the package folder.
    first, *rest = cells()
    results = {first: qualify(bench, *first, pin["checksum"], environment)}
    with concurrent.futures.ThreadPoolExecutor(arguments.parallel) as pool:
        futures = {cell: pool.submit(qualify, bench, *cell, pin["checksum"], environment) for cell in rest}
        results |= {cell: future.result() for cell, future in futures.items()}
    for (preset, collector), failure in results.items():
        print(f"{preset} {collector}: {'checksum ' + pin['checksum'] if failure is None else 'FAILED: ' + failure}")
    failed = [cell for cell, failure in results.items() if failure is not None]
    print(f"DoomSharp qualification {'FAIL' if failed else 'PASS'}: {len(results) - len(failed)} of {len(results)} cells")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

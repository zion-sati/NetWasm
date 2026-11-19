#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

"""Builds each open-source system under test in eng/open-source-suts against candidate
packages and requires every build to print what desktop .NET prints.

usage: qualify-open-source-suts.py <package directory> <version> [system ...]

Each system is a Program.cs over stock packages from nuget.org, pinned in its
packages.txt to the versions it was tested with ("Id Version" per line, exact
versions only); packages.desktop.txt and packages.netwasm.txt, where present, add
the packages only one side takes, such as a NetWasm port of a desktop package. It runs once on desktop .NET (net11.0), then as a net10.0-netwasm
program: wasm32 (a component) and wasm64 (raw Wasm), each in Debug and Release.
Every run's exit code and standard output must equal desktop's."""

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
import tempfile
from typing import NamedTuple

SYSTEMS = Path(__file__).resolve().parent / "open-source-suts"
TARGETS = ("wasm32", "wasm64")
CONFIGURATIONS = ("Debug", "Release")
PIN = re.compile(r"^([A-Za-z0-9_.-]+) (\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?)$")
TIMEOUT_SECONDS = 1200


class Run(NamedTuple):
    exit_code: int | str
    stdout: str
    log: str


def pins(text: str) -> list[tuple[str, str]]:
    """The packages.txt pins: one exact "Id Version" per line; blank lines are skipped."""
    result = []
    for line in text.splitlines():
        if not line.strip():
            continue
        match = PIN.match(line.strip())
        if match is None:
            raise ValueError(f"not an exact package pin: {line!r}")
        result.append((match[1], match[2]))
    return result


def project(name: str, packages: list[tuple[str, str]], netwasm: bool) -> str:
    """A project over the system's sources: the NetWasm SDK's net10.0-netwasm, or desktop's net11.0."""
    sdk = "NetWasm.Sdk" if netwasm else "Microsoft.NET.Sdk"
    framework = "net10.0-netwasm" if netwasm else "net11.0"
    references = "".join(f'    <PackageReference Include="{package}" Version="{version}" />\n'
                         for package, version in packages)
    return (f'<Project Sdk="{sdk}">\n'
            "  <PropertyGroup>\n"
            "    <OutputType>Exe</OutputType>\n"
            f"    <TargetFramework>{framework}</TargetFramework>\n"
            # Neither the program's assembly nor its package identity may be one of its packages' (assembly and
            # package names ignore case, so a program named sep would be Sep's own).
            f"    <AssemblyName>OpenSourceSut.{name}</AssemblyName>\n"
            "    <Nullable>enable</Nullable>\n"
            "    <ImplicitUsings>disable</ImplicitUsings>\n"
            "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n"
            "    <InvariantGlobalization>true</InvariantGlobalization>\n"
            # The pins are the versions tested; an advisory about one is not the program's output.
            "    <NuGetAudit>false</NuGetAudit>\n"
            "  </PropertyGroup>\n"
            + (f"  <ItemGroup>\n{references}  </ItemGroup>\n" if references else "")
            + "</Project>\n")


def target_properties(target: str) -> list[str]:
    """wasm32 builds the default component; wasm64 has no component model, so it is raw Wasm."""
    return [f"-p:NetWasmTarget={target}"] + (["-p:NetWasmRawWasm=true"] if target == "wasm64" else [])


def configure(root: Path, packages: Path, version: str) -> None:
    """Points the work root at the candidate SDK and packages, and stops MSBuild's search
    for Directory.Build and Directory.Packages files there, so no enclosing repository's
    build properties or central package management reach the systems."""
    root.mkdir(parents=True, exist_ok=True)
    (root / "global.json").write_text(json.dumps(
        {"sdk": {"version": "11.0.100", "rollForward": "latestFeature"},
         "msbuild-sdks": {"NetWasm.Sdk": version}}, indent=2) + "\n")
    (root / "NuGet.Config").write_text(
        "<configuration>\n  <packageSources>\n    <clear />\n"
        f"    <add key=\"candidate\" value=\"{packages}\" />\n"
        "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />\n"
        "  </packageSources>\n</configuration>\n")
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        (root / name).write_text("<Project />\n")


def system_pins(system: Path, netwasm: bool) -> list[tuple[str, str]]:
    """The shared pins, then those of the side being built."""
    side = system / ("packages.netwasm.txt" if netwasm else "packages.desktop.txt")
    return pins((system / "packages.txt").read_text()) + (pins(side.read_text()) if side.is_file() else [])


def stage(system: Path, directory: Path, netwasm: bool) -> Path:
    """Copies the system's sources into a fresh project directory and writes its project."""
    shutil.rmtree(directory, ignore_errors=True)
    directory.mkdir(parents=True)
    for source in system.glob("*.cs"):
        shutil.copy2(source, directory / source.name)
    path = directory / f"{system.name}.csproj"
    path.write_text(project(system.name, system_pins(system, netwasm), netwasm))
    return path


def run(command: list[str], cwd: Path, environment: dict[str, str]) -> Run:
    try:
        completed = subprocess.run(command, cwd=cwd, env=environment, capture_output=True, text=True,
                                   timeout=TIMEOUT_SECONDS, check=False)
        return Run(completed.returncode, completed.stdout, completed.stdout + completed.stderr)
    except subprocess.TimeoutExpired as error:
        return Run("timeout", "", f"timed out after {TIMEOUT_SECONDS} s\n{error.stdout or ''}")


def difference(expected: str, actual: str) -> str:
    """The first line where two outputs part, with its line number."""
    expected_lines, actual_lines = expected.splitlines(), actual.splitlines()
    for number, (left, right) in enumerate(zip(expected_lines, actual_lines), 1):
        if left != right:
            return f"line {number}: desktop {left!r}, NetWasm {right!r}"
    number = min(len(expected_lines), len(actual_lines)) + 1
    return f"line {number}: desktop has {len(expected_lines)} lines, NetWasm {len(actual_lines)}"


def build_errors(log: str) -> str:
    errors = sorted({line.strip() for line in log.splitlines() if ": error " in line})
    return "\n    ".join(errors[:10] or [log[-2000:]])


def desktop(system: Path, root: Path, environment: dict[str, str]) -> Run | str:
    """What desktop .NET prints, or why it could not be built."""
    project_path = stage(system, root / system.name / "desktop", netwasm=False)
    build = run(["dotnet", "build", str(project_path), "-c", "Release", "--disable-build-servers", "-nologo"],
                root, environment)
    if build.exit_code != 0:
        return "desktop build failed:\n    " + build_errors(build.log)
    return run(["dotnet", "run", "--project", str(project_path), "-c", "Release", "--no-build",
                "--disable-build-servers"], root, environment)


def qualify(system: Path, target: str, expected: Run, root: Path, environment: dict[str, str]) -> dict[str, str | None]:
    """Each configuration's failure for one target, or None where it printed what desktop printed."""
    project_path = stage(system, root / system.name / target, netwasm=True)
    properties = target_properties(target)
    results: dict[str, str | None] = {}
    for configuration in CONFIGURATIONS:
        cell = f"{target} {configuration}"
        build = run(["dotnet", "build", str(project_path), "-c", configuration, "--disable-build-servers", "-nologo",
                     *properties], root, environment)
        if build.exit_code != 0:
            results[cell] = "build failed:\n    " + build_errors(build.log)
            continue
        executed = run(["dotnet", "run", "--project", str(project_path), "-c", configuration, "--no-build",
                        "--disable-build-servers", *properties], root, environment)
        if (executed.exit_code, executed.stdout) == (expected.exit_code, expected.stdout):
            results[cell] = None
        elif executed.exit_code != expected.exit_code:
            results[cell] = (f"exit {executed.exit_code}, desktop {expected.exit_code}; "
                             f"{difference(expected.stdout, executed.stdout)}\n{executed.log[-2000:]}")
        else:
            results[cell] = difference(expected.stdout, executed.stdout)
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("packages", type=Path)
    parser.add_argument("version")
    parser.add_argument("systems", nargs="*")
    parser.add_argument("--parallel", type=int, default=4)
    parser.add_argument("--work", type=Path, help="work directory (default: a new temporary directory)")
    parser.add_argument("--systems-root", type=Path, default=SYSTEMS, help="directory of systems (default: %(default)s)")
    arguments = parser.parse_args()
    systems_root = arguments.systems_root.resolve()
    available = sorted(path.name for path in systems_root.iterdir() if (path / "packages.txt").is_file())
    unknown = sorted(set(arguments.systems) - set(available))
    if unknown:
        raise SystemExit(f"no such system: {', '.join(unknown)} (systems: {', '.join(available)})")
    systems = [systems_root / name for name in (arguments.systems or available)]
    root = (arguments.work or Path(tempfile.mkdtemp(prefix="netwasm-open-source-suts-"))).resolve()
    configure(root, arguments.packages.resolve(), arguments.version)
    environment = dict(os.environ, NUGET_PACKAGES=str(root / ".packages"), DOTNET_CLI_TELEMETRY_OPTOUT="1")

    # Desktop runs one system at a time, restoring every pinned package, and the first NetWasm cell restores the
    # SDK's, before the parallel builds share the package folder.
    failures: dict[tuple[str, str], str | None] = {}
    expected: dict[str, Run] = {}
    for system in systems:
        baseline = desktop(system, root, environment)
        if isinstance(baseline, str):
            failures[(system.name, "desktop")] = baseline
        elif baseline.exit_code == "timeout" or not baseline.stdout:
            failures[(system.name, "desktop")] = f"desktop .NET printed nothing (exit {baseline.exit_code}):\n" \
                                                 f"{baseline.log[-2000:]}"
        else:
            expected[system.name] = baseline
    cells = [(system, target) for system in systems if system.name in expected for target in TARGETS]
    if cells:
        first, *rest = cells
        failures |= {(first[0].name, cell): failure
                     for cell, failure in qualify(*first, expected[first[0].name], root, environment).items()}
        with concurrent.futures.ThreadPoolExecutor(arguments.parallel) as pool:
            futures = {(system.name, target): pool.submit(qualify, system, target, expected[system.name], root,
                                                          environment)
                       for system, target in rest}
            for (name, _), future in futures.items():
                failures |= {(name, cell): failure for cell, failure in future.result().items()}
    for (name, cell), failure in sorted(failures.items()):
        print(f"{name} {cell}: {'matches desktop' if failure is None else 'FAILED: ' + failure}")
    failed = [key for key, failure in failures.items() if failure is not None]
    print(f"Open-source SUT qualification {'FAIL' if failed else 'PASS'}: "
          f"{len(failures) - len(failed)} of {len(failures)} cells")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

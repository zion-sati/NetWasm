#!/usr/bin/env python3
"""Build one pinned native host-tools NuGet package without shared output mutation."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
MAX_PACKAGE_BYTES = 240_000_000


def preserve_unix_executable_modes(path: Path, rid: str) -> None:
    if rid.startswith("win-"):
        return
    temporary = path.with_suffix(".nupkg.tmp")
    try:
        with zipfile.ZipFile(path) as source, zipfile.ZipFile(temporary, "w") as target:
            for original in source.infolist():
                entry = zipfile.ZipInfo(original.filename, original.date_time)
                entry.compress_type = original.compress_type
                entry.create_system = 3
                entry.external_attr = (
                    stat.S_IFREG | (0o755 if original.filename.startswith("tools/bin/") else 0o644)
                ) << 16
                entry.flag_bits = original.flag_bits
                target.writestr(entry, source.read(original), compresslevel=9)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def publish_compiler(rid: str, version: str, environment: dict[str, str]) -> Path:
    """Publish nwc with Native AOT for rid, which must be this host's, as version.

    The build has its own fixed artifacts root, so it shares no output with other
    builds, and the paths its objects record are the same on every repeat; Apple's
    linker records object times unless ZERO_AR_DATE is set.
    """
    output = ROOT / "artifacts/host-tools" / f"nwc-{rid}"
    shutil.rmtree(output, ignore_errors=True)
    environment = environment | {"ZERO_AR_DATE": "1"}
    subprocess.run(
        [
            "dotnet", "publish",
            str(ROOT / "src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj"),
            "-c", "Release",
            "-r", rid,
            "-p:PublishAot=true",
            f"-p:Version={version}",
            "-p:ContinuousIntegrationBuild=true",
            "--artifacts-path", str(output / "artifacts"),
            "-o", str(output / "publish"),
            "--disable-build-servers",
            "--nologo",
        ],
        check=True,
        cwd=ROOT,
        env=environment,
    )
    compiler = output / "publish" / ("nwc.exe" if rid.startswith("win-") else "nwc")
    if not compiler.is_file():
        raise ValueError(f"Native AOT nwc was not produced: {compiler}")
    return compiler


def build_many(
    rid: str, versions: list[str], output: Path, cache: Path,
) -> list[Path]:
    if not versions or len(versions) != len(set(versions)):
        raise ValueError("Host-tools package versions must be a non-empty unique list.")
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"Host-tools package output must be empty: {output}")
    output.mkdir(parents=True, exist_ok=True)
    commit = subprocess.check_output(
        ["git", "-C", str(ROOT), "rev-parse", "HEAD"], text=True
    ).strip()
    epoch = subprocess.check_output(
        ["git", "-C", str(ROOT), "show", "-s", "--format=%ct", "HEAD"], text=True
    ).strip()
    environment = os.environ.copy()
    environment["SOURCE_DATE_EPOCH"] = epoch
    with tempfile.TemporaryDirectory(prefix="netwasm-host-tools-") as temporary:
        results = []
        for version in versions:
            # nwc reports the version of the package it ships in, so each
            # version stages its own build of it.
            stage = Path(temporary) / f"stage-{version}"
            compiler = publish_compiler(rid, version, environment)
            subprocess.run(
                [sys.executable, str(ROOT / "eng/stage-host-tools.py"),
                 "--rid", rid, "--version", version,
                 "--output", str(stage), "--cache", str(cache), "--probe",
                 "--compiler", str(compiler)],
                check=True,
                cwd=ROOT,
            )
            subprocess.run(
                [
                    "dotnet", "pack",
                    str(ROOT / "src/NetWasm.HostTools/NetWasm.HostTools.csproj"),
                    "-c", "Release",
                    "--disable-build-servers",
                    "--nologo",
                    "-o", str(output),
                    f"-p:PackageVersion={version}",
                    f"-p:NetWasmHostRid={rid}",
                    f"-p:NetWasmHostToolsStagingRoot={stage}",
                    f"-p:RepositoryCommit={commit}",
                    "-p:ContinuousIntegrationBuild=true",
                ],
                check=True,
                cwd=ROOT,
                env=environment,
            )
            result = output / f"NetWasm.HostTools.{rid}.{version}.nupkg"
            if not result.is_file():
                raise ValueError(f"Host-tools package was not produced: {result}")
            preserve_unix_executable_modes(result, rid)
            if result.stat().st_size > MAX_PACKAGE_BYTES:
                raise ValueError(f"Host-tools package exceeds {MAX_PACKAGE_BYTES} bytes: {result}")
            with zipfile.ZipFile(result) as archive:
                manifest = json.loads(archive.read("tools/host-tools-manifest.json"))
                if manifest["hostRid"] != rid or manifest["packageVersion"] != version:
                    raise ValueError("Packed host-tools manifest does not match the requested identity.")
                for item in manifest["files"]:
                    if item["path"] not in archive.namelist():
                        raise ValueError(f"Missing packed host-tool file: {item['path']}")
            subprocess.run(
                [
                    sys.executable,
                    str(ROOT / "eng/verify-host-tools-package.py"),
                    "--package", str(result),
                    "--rid", rid,
                    "--version", version,
                ],
                check=True,
                cwd=ROOT,
            )
            print(f"Packed {result.name}: {result.stat().st_size:,} bytes")
            results.append(result)
        return results


def build(
    rid: str, version: str, output: Path, cache: Path,
) -> Path:
    return build_many(rid, [version], output, cache)[0]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--version", required=True, action="append", dest="versions")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    arguments = parser.parse_args()
    build_many(
        arguments.rid, arguments.versions,
        arguments.output.resolve(), arguments.cache.resolve(),
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3

"""Project a release version into a clean, disposable Git worktree."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from pathlib import Path


VERSION_PATTERN = re.compile(
    r"^[0-9]+\.[0-9]+\.[0-9]+"
    r"(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
    r"(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$"
)
VERSION_FILE = Path("eng/NetWasm.ReleaseVersion.txt")
XML_FIELDS = {
    Path("eng/NetWasm.PackageVersions.props"): (
        "NetWasmCompilerTasksPackageVersion",
        "NetWasmHostToolsPackageVersion",
    ),
    Path("src/NetWasm.Sdk/Sdk/NetWasm.Sdk.Tfm.props"): (
        "NetWasmRefPackageVersion",
        "NetWasmRuntimePackageVersion",
        "NetWasmRuntimePackPackageVersion",
        "NetWasmToolchainPackageVersion",
        "NetWasmHostingPackageVersion",
        "NetWasmHostingBuildPackageVersion",
        "NetWasmHostToolsPackageVersion",
    ),
    Path("src/NetWasm.Sdk/Sdk/Sdk.props"): ("NetWasmSdkPackageVersion",),
}
JSON_FIELDS = {
    Path("global.json"): ("NetWasm.Sdk",),
    Path("src/NetWasm.Templates/content/NetWasm.App/base/global.json"): ("NetWasm.Sdk",),
    Path("src/NetWasm.Templates/content/NetWasm.Library/global.json"): ("NetWasm.Sdk",),
    Path("src/NetWasm.Toolchain/toolchain-manifest.json"): ("packageVersion",),
}


def run_git(source_root: Path, *arguments: str) -> bytes:
    return subprocess.check_output(
        ["git", "-C", str(source_root), *arguments], stderr=subprocess.STDOUT
    )


def tracked_files(source_root: Path) -> list[Path]:
    output = run_git(source_root, "ls-files", "-z")
    return [source_root / Path(value.decode("utf-8")) for value in output.split(b"\0") if value]


def replace_xml_field(
    contents: bytes, field: str, source: bytes, target: bytes
) -> tuple[bytes, int]:
    pattern = re.compile(
        rb"(<" + re.escape(field.encode("ascii")) + rb"(?:\s[^>]*)?>\s*)"
        + re.escape(source)
        + rb"(\s*</" + re.escape(field.encode("ascii")) + rb">)"
    )
    return pattern.subn(rb"\g<1>" + target + rb"\g<2>", contents)


def replace_json_field(
    contents: bytes, field: str, source: bytes, target: bytes
) -> tuple[bytes, int]:
    pattern = re.compile(
        rb'(\"' + re.escape(field.encode("ascii")) + rb'\"\s*:\s*\")'
        + re.escape(source)
        + rb'(\")'
    )
    return pattern.subn(rb"\g<1>" + target + rb"\g<2>", contents)


def project_release_fields(
    source_root: Path,
    source_version: str,
    target_version: str,
    files: list[Path],
) -> list[dict[str, object]]:
    if source_version == target_version:
        return []
    source_bytes = source_version.encode("utf-8")
    target_bytes = target_version.encode("utf-8")
    changed_files: list[dict[str, object]] = []
    version_file = source_root / VERSION_FILE
    version_file.write_text(target_version + "\n", encoding="utf-8")
    changed_files.append({"path": VERSION_FILE.as_posix(), "replacements": 1})

    tracked = {path.relative_to(source_root): path for path in files}
    for relative, fields in XML_FIELDS.items():
        path = tracked.get(relative)
        if path is None:
            continue
        contents = path.read_bytes()
        total = 0
        for field in fields:
            contents, count = replace_xml_field(
                contents, field, source_bytes, target_bytes
            )
            if count == 0:
                raise ValueError(
                    f"Release field {field} in {relative} does not match {source_version}."
                )
            total += count
        path.write_bytes(contents)
        changed_files.append({"path": relative.as_posix(), "replacements": total})

    for relative, fields in JSON_FIELDS.items():
        path = tracked.get(relative)
        if path is None:
            continue
        contents = path.read_bytes()
        total = 0
        for field in fields:
            contents, count = replace_json_field(
                contents, field, source_bytes, target_bytes
            )
            if count != 1:
                raise ValueError(
                    f"Release field {field} in {relative} does not match {source_version}."
                )
            total += count
        path.write_bytes(contents)
        changed_files.append({"path": relative.as_posix(), "replacements": total})

    for relative, path in sorted(tracked.items()):
        if (
            relative.suffix != ".csproj"
            or not relative.parts
            or relative.parts[0] not in {"src", "tools"}
        ):
            continue
        contents, count = replace_xml_field(
            path.read_bytes(), "Version", source_bytes, target_bytes
        )
        if count:
            path.write_bytes(contents)
            changed_files.append({
                "path": relative.as_posix(), "replacements": count,
            })
    return changed_files


def project_version(source_root: Path, target_version: str, receipt_path: Path) -> dict[str, object]:
    if not VERSION_PATTERN.fullmatch(target_version):
        raise ValueError(f"Invalid release version: {target_version}")

    source_root = source_root.resolve()
    if run_git(source_root, "status", "--porcelain=v1").strip():
        raise ValueError("Release-version projection requires a clean Git worktree.")

    version_file = source_root / VERSION_FILE
    source_version = version_file.read_text(encoding="utf-8").strip()
    if not VERSION_PATTERN.fullmatch(source_version):
        raise ValueError(f"Invalid source release version in {VERSION_FILE}: {source_version}")

    changed_files = project_release_fields(
        source_root, source_version, target_version, tracked_files(source_root)
    )

    receipt = {
        "schemaVersion": 1,
        "repositoryCommit": run_git(source_root, "rev-parse", "HEAD").decode("ascii").strip(),
        "sourceVersion": source_version,
        "targetVersion": target_version,
        "changedFiles": changed_files,
        "replacementCount": sum(int(item["replacements"]) for item in changed_files),
    }
    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    arguments = parser.parse_args()

    receipt = project_version(arguments.source_root, arguments.version, arguments.receipt)
    print(
        f"Projected {receipt['sourceVersion']} to {receipt['targetVersion']} "
        f"with {receipt['replacementCount']} replacements in "
        f"{len(receipt['changedFiles'])} tracked files."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

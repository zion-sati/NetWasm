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


def run_git(source_root: Path, *arguments: str) -> bytes:
    return subprocess.check_output(
        ["git", "-C", str(source_root), *arguments], stderr=subprocess.STDOUT
    )


def tracked_files(source_root: Path) -> list[Path]:
    output = run_git(source_root, "ls-files", "-z")
    return [source_root / Path(value.decode("utf-8")) for value in output.split(b"\0") if value]


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

    source_bytes = source_version.encode("utf-8")
    target_bytes = target_version.encode("utf-8")
    changed_files: list[dict[str, object]] = []
    remaining_files: list[str] = []
    files = tracked_files(source_root)

    for path in files:
        if path.is_symlink():
            continue
        contents = path.read_bytes()
        if b"\0" in contents:
            continue

        replacement_count = contents.count(source_bytes)
        if replacement_count and source_version != target_version:
            path.write_bytes(contents.replace(source_bytes, target_bytes))
            changed_files.append(
                {
                    "path": path.relative_to(source_root).as_posix(),
                    "replacements": replacement_count,
                }
            )

    for path in files:
        if path.is_symlink():
            continue
        contents = path.read_bytes()
        if (
            source_version != target_version
            and b"\0" not in contents
            and source_bytes in contents
        ):
            remaining_files.append(path.relative_to(source_root).as_posix())

    if remaining_files:
        raise ValueError(
            "Source release version remains after projection: " + ", ".join(remaining_files)
        )
    if source_version != target_version and not changed_files:
        raise ValueError(f"Source release version {source_version} was not found in tracked text files.")

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

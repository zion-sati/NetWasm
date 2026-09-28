#!/usr/bin/env python3
"""Fail-closed CI impact classification for the public NetWasm repository."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ElementTree
from urllib.parse import unquote


MARKDOWN_LINK = re.compile(r"(?<!!)\[[^]]+\]\(([^)]+)\)")
REGULAR_FILE_MODES = {"100644", "100755"}
CI_INFRASTRUCTURE_FILES = frozenset({
    ".github/workflows/ci.yml",
    ".github/workflows/coordinated-release.yml",
    ".github/workflows/prepare-release.yml",
    ".github/workflows/release.yml",
    "eng/ci-qualification.py",
    "eng/classify-ci-impact.py",
    "eng/compiler-test-durations.json",
    "eng/create-compiler-test-shard-filter.py",
    "eng/merge-compiler-test-timings.py",
    "eng/tests/test_ci_qualification.py",
    "eng/tests/test_classify_ci_impact.py",
    "eng/tests/test_create_compiler_test_shard_filter.py",
    "eng/tests/test_merge_compiler_test_timings.py",
    "eng/tests/test_verify_ci_results.py",
    "eng/tests/test_publish_release_packages.py",
    "eng/tests/test_release_coordinator.py",
    "eng/tests/test_release_orchestrator.py",
    "eng/tests/test_release_preparation.py",
    "eng/tests/test_release_receiver.py",
    "eng/tests/test_release_train.py",
    "eng/tests/test_resolve_release.py",
    "eng/tests/test_verify_release_ci.py",
    "eng/tests/test_verify_release_packages.py",
    "eng/publish-release-packages.py",
    "eng/release-coordinator.py",
    "eng/release-orchestrator.py",
    "eng/release-preparation.py",
    "eng/release-receiver.py",
    "eng/release-train.py",
    "eng/resolve-release.py",
    "eng/verify-release-ci.py",
    "eng/verify-release-packages.py",
    "eng/verify-ci-results.py",
})
CI_INFRASTRUCTURE_ADDITIONS = frozenset({
    ".github/workflows/coordinated-release.yml",
    ".github/workflows/prepare-release.yml",
    "eng/release-coordinator.py",
    "eng/release-orchestrator.py",
    "eng/release-preparation.py",
    "eng/release-receiver.py",
    "eng/tests/test_release_coordinator.py",
    "eng/tests/test_release_orchestrator.py",
    "eng/tests/test_release_preparation.py",
    "eng/tests/test_release_receiver.py",
})


@dataclass(frozen=True)
class Change:
    path: str
    previous: bytes | None
    current: bytes | None
    previous_mode: str | None = "100644"
    current_mode: str | None = "100644"


def _normalized_package_description(document: bytes) -> bytes | None:
    try:
        root = ElementTree.fromstring(document)
    except ElementTree.ParseError:
        return None

    descriptions = root.findall(".//Description")
    if not descriptions:
        return None
    for description in descriptions:
        description.text = "NETWASM_PACKAGE_DESCRIPTION"
    return ElementTree.tostring(root, encoding="utf-8")


def _is_description_only(change: Change) -> bool:
    if not change.path.endswith(".csproj"):
        return False
    if change.previous is None or change.current is None:
        return False
    previous = _normalized_package_description(change.previous)
    current = _normalized_package_description(change.current)
    return previous is not None and previous == current


def classify(changes: list[Change]) -> str:
    if not changes:
        return "full"

    ci_infrastructure_changed = False
    for change in changes:
        for mode in (change.previous_mode, change.current_mode):
            if mode is not None and mode not in REGULAR_FILE_MODES:
                return "full"
        if change.path.endswith(".md"):
            continue
        if _is_description_only(change):
            continue
        if (
            change.path in CI_INFRASTRUCTURE_FILES
            and change.current is not None
            and (
                change.previous is not None
                or change.path in CI_INFRASTRUCTURE_ADDITIONS
            )
        ):
            ci_infrastructure_changed = True
            continue
        return "full"
    return "ci" if ci_infrastructure_changed else "docs"


def _git(root: Path, *arguments: str, check: bool = True) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["git", "-C", str(root), *arguments],
        capture_output=True,
        check=check,
    )


def _blob(root: Path, revision: str, path: str) -> bytes | None:
    result = _git(root, "show", f"{revision}:{path}", check=False)
    return result.stdout if result.returncode == 0 else None


def _mode(root: Path, revision: str, path: str) -> str | None:
    result = _git(root, "ls-tree", revision, "--", path, check=False)
    if result.returncode != 0 or not result.stdout:
        return None
    return result.stdout.split(None, 1)[0].decode("ascii")


def collect_changes(root: Path, base: str, head: str) -> list[Change]:
    paths = _git(
        root,
        "diff",
        "--name-only",
        "--no-renames",
        "-z",
        base,
        head,
    ).stdout.split(b"\0")
    return [
        Change(
            path=path.decode("utf-8"),
            previous=_blob(root, base, path.decode("utf-8")),
            current=_blob(root, head, path.decode("utf-8")),
            previous_mode=_mode(root, base, path.decode("utf-8")),
            current_mode=_mode(root, head, path.decode("utf-8")),
        )
        for path in paths
        if path
    ]


def verify_changed_markdown_links(root: Path, changes: list[Change]) -> None:
    for change in changes:
        if not change.path.endswith(".md") or change.current is None:
            continue
        document = root / change.path
        for raw_target in MARKDOWN_LINK.findall(change.current.decode("utf-8")):
            target = raw_target.split("#", 1)[0].strip()
            if not target or target.startswith(("http://", "https://", "mailto:")):
                continue
            target = unquote(target.split(" ", 1)[0].strip("<>"))
            if not (document.parent / target).resolve().exists():
                raise ValueError(f"broken local Markdown link in {change.path}: {raw_target}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--verify-docs", action="store_true")
    args = parser.parse_args()

    root = args.root.resolve()
    changes = collect_changes(root, args.base, args.head)
    scope = classify(changes)
    if args.verify_docs:
        if scope != "docs":
            raise SystemExit("documentation verification requires a docs-only change")
        verify_changed_markdown_links(root, changes)
    print(scope)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3

"""Materialize the audited, platform-neutral Hosting bundler closure."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
from typing import Any


_SUPPORT_PATH = Path(__file__).with_name("stage-jco-closure.py")
_SPEC = importlib.util.spec_from_file_location("netwasm_jco_stager", _SUPPORT_PATH)
if _SPEC is None or _SPEC.loader is None:
    raise RuntimeError("cannot load the shared secure package stager")
_SUPPORT = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_SUPPORT)

ClosureStagingError = _SUPPORT.ClosureStagingError
ROOT_PACKAGE = "node_modules/rolldown"
POLICY_KEYS = {
    "schemaVersion",
    "contract",
    "roots",
    "entryPoint",
    "requiredCommands",
    "acceptedInvocation",
    "lockSha256",
    "evidence",
    "selectedPackagePaths",
}
ACCEPTED_INVOCATION = [
    "bundle",
    "<absolute-entry>",
    "--output",
    "<absolute-output>",
    "--platform",
    "<browser|node>",
    "--minify",
    "<true|false>",
]


def _validate_policy(policy: Any) -> dict[str, dict[str, Any]]:
    if not isinstance(policy, dict) or set(policy) != POLICY_KEYS:
        raise ClosureStagingError("bundler closure policy shape is invalid")
    if policy["schemaVersion"] != "1" or policy["contract"] != "hosting-bundle":
        raise ClosureStagingError("bundler closure policy identity is unsupported")
    if policy["roots"] != [ROOT_PACKAGE]:
        raise ClosureStagingError("bundler closure policy root is invalid")
    if policy["entryPoint"] != f"{ROOT_PACKAGE}/dist/index.mjs":
        raise ClosureStagingError("bundler closure policy entry point is invalid")
    if policy["requiredCommands"] != ["bundle"]:
        raise ClosureStagingError("bundler closure policy command is invalid")
    if policy["acceptedInvocation"] != ACCEPTED_INVOCATION:
        raise ClosureStagingError("bundler closure invocation is invalid")
    if not isinstance(policy["evidence"], dict) or not policy["evidence"]:
        raise ClosureStagingError("bundler closure policy evidence is missing")
    entries = _SUPPORT._policy_entry_map(policy, "selectedPackagePaths")
    if ROOT_PACKAGE not in entries:
        raise ClosureStagingError("bundler closure omits the Rolldown root")
    if list(entries) != sorted(entries):
        raise ClosureStagingError("bundler closure package paths are not canonical")
    for path, entry in entries.items():
        for field in ("name", "version", "resolved", "integrity"):
            value = entry.get(field)
            if not isinstance(value, str) or not value.strip():
                raise ClosureStagingError(
                    f"bundler package '{path}' has no policy {field} pin"
                )
    return entries


def select_locked_closure(
    lock: dict[str, Any],
    policy: dict[str, Any],
) -> dict[str, dict[str, Any]]:
    if not isinstance(lock, dict) or lock.get("lockfileVersion") != 3:
        raise ClosureStagingError("package-lock.json identity is unsupported")
    records = lock.get("packages")
    if not isinstance(records, dict):
        raise ClosureStagingError("package-lock.json has no package records")
    selected_entries = _validate_policy(policy)
    selected = set(selected_entries)
    for path in sorted(selected):
        record = records.get(path)
        if not isinstance(record, dict):
            raise ClosureStagingError(f"locked bundler package '{path}' is missing")
        _SUPPORT._validate_locked_record(path, record, selected_entries[path])

    reached: set[str] = set()
    queue = [ROOT_PACKAGE]
    while queue:
        path = queue.pop()
        if path in reached:
            continue
        if path not in selected:
            raise ClosureStagingError(
                f"bundler closure reaches unselected package '{path}'"
            )
        reached.add(path)
        for dependency, _ in _SUPPORT._record_dependencies(records[path]):
            target = _SUPPORT._dependency_key(path, dependency, records)
            if target not in selected:
                raise ClosureStagingError(
                    f"bundler dependency '{path}' -> '{target}' is unselected"
                )
            queue.append(target)
    if reached != selected:
        unreachable = sorted(selected - reached)[0]
        raise ClosureStagingError(
            f"bundler package '{unreachable}' is unreachable from Rolldown"
        )
    return {path: records[path] for path in sorted(selected)}


def stage(
    lock_path: Path,
    policy_path: Path,
    generation_root: Path,
    cache: Path,
) -> int:
    generation_root = generation_root.absolute()
    cache = cache.absolute()
    if _SUPPORT._paths_overlap(generation_root, cache):
        raise ClosureStagingError(
            "the bundler tarball cache and generation root must not overlap"
        )
    try:
        lock_bytes = lock_path.read_bytes()
        policy_bytes = policy_path.read_bytes()
        lock = json.loads(lock_bytes.decode("utf-8"))
        policy = json.loads(policy_bytes.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ClosureStagingError("cannot read the locked bundler inputs") from exception
    entries = _validate_policy(policy)
    if hashlib.sha256(lock_bytes).hexdigest() != policy["lockSha256"]:
        raise ClosureStagingError("bundler package-lock SHA-256 pin does not match")
    selected = select_locked_closure(lock, policy)

    with _SUPPORT._generation_transaction(generation_root) as (payload, marker):
        expanded_bytes = 0
        for path, record in selected.items():
            tarball = _SUPPORT._cache_tarball(record, cache)
            expanded_bytes += _SUPPORT._extract_package(
                tarball,
                payload / path,
                record["version"],
                entries[path]["name"],
                record,
                _SUPPORT.MAX_CLOSURE_EXPANDED_BYTES - expanded_bytes,
            )
        entry_point = payload / policy["entryPoint"]
        if not entry_point.is_file():
            raise ClosureStagingError("canonical Rolldown entry point is missing")
        (payload / "package-lock.json").write_bytes(lock_bytes)
        (payload / "closure-policy.json").write_bytes(policy_bytes)
        integrity = _SUPPORT._write_integrity(
            payload,
            payload / "closure-integrity.json",
        )
        _SUPPORT._write_pack_items(
            payload,
            payload / "closure-pack-items.props",
            "tools/bundler",
            "NetWasmToolchainBundlerAssetRoot",
        )
        _SUPPORT._write_notices(
            payload,
            selected,
            payload / "notices.json",
        )
        _SUPPORT._write_exclusive_json(marker, {
            "schemaVersion": _SUPPORT.GENERATION_SCHEMA_VERSION,
            "state": "complete",
            "payload": _SUPPORT.GENERATION_PAYLOAD_NAME,
            "canonicalRoot": _SUPPORT.GENERATION_PAYLOAD_NAME,
            "packageCount": len(selected),
            "fileCount": len(integrity),
        })
    print(f"packages={len(selected)} files={len(integrity)}")
    print(f"generation={generation_root}")
    return len(selected)


def _arguments(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--generation-root", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    arguments = _arguments(argv or sys.argv[1:])
    try:
        stage(
            arguments.lock.absolute(),
            arguments.policy.absolute(),
            arguments.generation_root.absolute(),
            arguments.cache.absolute(),
        )
    except (ClosureStagingError, OSError, json.JSONDecodeError) as exception:
        print(f"NW-BUNDLER-STAGE-001: {exception}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3

"""Materialize the independently pinned Binaryen JavaScript/Wasm package."""

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

BinaryenStagingError = _SUPPORT.ClosureStagingError
POLICY_KEYS = {"schemaVersion", "contract", "lockSha256", "package"}
PACKAGE_KEYS = {"path", "name", "version", "resolved", "integrity"}
PACKAGE_PATH = "node_modules/binaryen"


def _read_inputs(lock_path: Path, policy_path: Path) -> tuple[dict[str, Any], dict[str, Any]]:
    try:
        lock_bytes = lock_path.read_bytes()
        policy_bytes = policy_path.read_bytes()
        lock = json.loads(lock_bytes.decode("utf-8"))
        policy = json.loads(policy_bytes.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise BinaryenStagingError("cannot read the locked Binaryen inputs") from exception
    if not isinstance(policy, dict) or set(policy) != POLICY_KEYS:
        raise BinaryenStagingError("Binaryen closure policy shape is invalid")
    if policy["schemaVersion"] != "1" or policy["contract"] != "binaryen-programs":
        raise BinaryenStagingError("Binaryen closure policy identity is unsupported")
    if hashlib.sha256(lock_bytes).hexdigest() != policy["lockSha256"]:
        raise BinaryenStagingError("Binaryen package-lock SHA-256 pin does not match")
    package = policy["package"]
    if not isinstance(package, dict) or set(package) != PACKAGE_KEYS:
        raise BinaryenStagingError("Binaryen package policy shape is invalid")
    if package["path"] != PACKAGE_PATH or package["name"] != "binaryen":
        raise BinaryenStagingError("Binaryen package policy identity is invalid")
    if not isinstance(lock, dict) or lock.get("lockfileVersion") != 3:
        raise BinaryenStagingError("Binaryen package lock identity is unsupported")
    records = lock.get("packages")
    if not isinstance(records, dict) or set(records) != {"", PACKAGE_PATH}:
        raise BinaryenStagingError("Binaryen package lock inventory is invalid")
    root = records[""]
    if not isinstance(root, dict) or root.get("devDependencies") != {
        "binaryen": package["version"]
    }:
        raise BinaryenStagingError("Binaryen package lock root is invalid")
    record = records[PACKAGE_PATH]
    if not isinstance(record, dict):
        raise BinaryenStagingError("Binaryen package lock record is invalid")
    for field in ("version", "resolved", "integrity"):
        if record.get(field) != package[field]:
            raise BinaryenStagingError(f"Binaryen locked {field} differs from policy")
    _SUPPORT._validate_locked_record(PACKAGE_PATH, record, package)
    if list(_SUPPORT._record_dependencies(record)):
        raise BinaryenStagingError("the pinned Binaryen package unexpectedly has dependencies")
    return record, policy


def stage(
    lock_path: Path,
    policy_path: Path,
    generation_root: Path,
    cache: Path,
) -> int:
    generation_root = generation_root.absolute()
    cache = cache.absolute()
    if _SUPPORT._paths_overlap(generation_root, cache):
        raise BinaryenStagingError(
            "the Binaryen tarball cache and generation root must not overlap"
        )
    record, policy = _read_inputs(lock_path, policy_path)
    package = policy["package"]
    archive = _SUPPORT._cache_tarball(record, cache)
    with _SUPPORT._generation_transaction(generation_root) as (payload, marker):
        extracted = payload / ".binaryen-package"
        _SUPPORT._extract_package(
            archive,
            extracted,
            package["version"],
            package["name"],
            record,
        )
        for path in extracted.iterdir():
            path.rename(payload / path.name)
        extracted.rmdir()
        files = list(_SUPPORT._files(payload))
        _SUPPORT._write_exclusive_json(marker, {
            "schemaVersion": _SUPPORT.GENERATION_SCHEMA_VERSION,
            "state": "complete",
            "payload": _SUPPORT.GENERATION_PAYLOAD_NAME,
            "canonicalRoot": _SUPPORT.GENERATION_PAYLOAD_NAME,
            "package": package["name"],
            "version": package["version"],
            "fileCount": len(files),
        })
    print(f"package={package['name']} version={package['version']} files={len(files)}")
    print(f"generation={generation_root}")
    return len(files)


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--policy", required=True, type=Path)
    parser.add_argument("--generation-root", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    arguments = _arguments()
    try:
        stage(
            arguments.lock.absolute(),
            arguments.policy.absolute(),
            arguments.generation_root.absolute(),
            arguments.cache.absolute(),
        )
    except (BinaryenStagingError, OSError, json.JSONDecodeError) as exception:
        print(f"NW-BINARYEN-STAGE-001: {exception}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

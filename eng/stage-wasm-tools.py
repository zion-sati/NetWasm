#!/usr/bin/env python3

"""Materialize the pinned, platform-neutral wasm-tools WASI distribution."""

from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tarfile
import urllib.request
from urllib.parse import urlparse
import uuid
from typing import Iterator


class WasmToolsStagingError(RuntimeError):
    """Raised when the pinned wasm-tools distribution cannot be staged."""


VERSION = "1.256.0"
ARCHIVE_DIRECTORY = f"wasm-tools-{VERSION}-wasm32-wasip1"
ARCHIVE_NAME = f"{ARCHIVE_DIRECTORY}.tar.gz"
ARCHIVE_URL = (
    "https://github.com/bytecodealliance/wasm-tools/releases/download/"
    f"v{VERSION}/{ARCHIVE_NAME}"
)
ARCHIVE_SHA256 = "e54ab005eac55da72c0876b399a855346cea9880e3f50ab1249ac816544a8aaf"
MAX_ARCHIVE_BYTES = 8_000_000
MAX_EXPANDED_BYTES = 20_000_000
EXPECTED_FILES = {
    "LICENSE-APACHE": "a60eea817514531668d7e00765731449fe14d059d3249e0bc93b36de45f759f2",
    "LICENSE-Apache-2.0_WITH_LLVM-exception": (
        "268872b9816f90fd8e85db5a28d33f8150ebb8dd016653fb39ef1f94f2686bc5"
    ),
    "LICENSE-MIT": "23f18e03dc49df91622fe2a76176497404e46ced8a715d9d2b67a7446571cca3",
    "README.md": "2034723ff7a0741cab9faa03edd52dfb4cd0c70bc5c5c4fbf643a47a30931070",
    "wasm-tools.wasm": "1141fd48fcf58c9704fb24ef1bd9b5d9aaf584b688eb8dce8e226c0488056beb",
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _paths_overlap(first: Path, second: Path) -> bool:
    first = first.resolve(strict=False)
    second = second.resolve(strict=False)
    return first == second or first in second.parents or second in first.parents


def _download_archive(cache: Path) -> Path:
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / f"sha256-{ARCHIVE_SHA256}.tar.gz"
    if archive.exists():
        if not archive.is_file() or archive.is_symlink() or _sha256(archive) != ARCHIVE_SHA256:
            raise WasmToolsStagingError("the cached wasm-tools archive is invalid")
        return archive

    temporary = cache / f".{archive.name}.{uuid.uuid4().hex}.tmp"
    try:
        request = urllib.request.Request(
            ARCHIVE_URL,
            headers={"User-Agent": "NetWasm-toolchain-stager/1"},
        )
        with urllib.request.urlopen(request) as response, temporary.open("xb") as output:
            if urlparse(response.geturl()).scheme != "https":
                raise WasmToolsStagingError("the wasm-tools download left HTTPS")
            digest = hashlib.sha256()
            byte_count = 0
            while chunk := response.read(1024 * 1024):
                byte_count += len(chunk)
                if byte_count > MAX_ARCHIVE_BYTES:
                    raise WasmToolsStagingError("the wasm-tools archive exceeds its size limit")
                digest.update(chunk)
                output.write(chunk)
        if digest.hexdigest() != ARCHIVE_SHA256:
            raise WasmToolsStagingError("the downloaded wasm-tools archive hash does not match")
        try:
            temporary.rename(archive)
        except FileExistsError:
            if _sha256(archive) != ARCHIVE_SHA256:
                raise WasmToolsStagingError("a concurrent wasm-tools cache entry is invalid")
    finally:
        temporary.unlink(missing_ok=True)
    return archive


def _read_distribution(archive: Path) -> dict[str, bytes]:
    expected_members = {
        ARCHIVE_DIRECTORY,
        *(f"{ARCHIVE_DIRECTORY}/{name}" for name in EXPECTED_FILES),
    }
    files: dict[str, bytes] = {}
    with tarfile.open(archive, "r:gz") as package:
        members = package.getmembers()
        names = [member.name for member in members]
        if len(names) != len(set(names)) or set(names) != expected_members:
            raise WasmToolsStagingError("the wasm-tools archive inventory is invalid")
        expanded_bytes = 0
        for member in members:
            if member.name == ARCHIVE_DIRECTORY:
                if not member.isdir():
                    raise WasmToolsStagingError("the wasm-tools archive root is invalid")
                continue
            if not member.isfile() or member.issym() or member.islnk():
                raise WasmToolsStagingError("the wasm-tools archive contains an invalid entry")
            expanded_bytes += member.size
            if expanded_bytes > MAX_EXPANDED_BYTES:
                raise WasmToolsStagingError("the wasm-tools distribution exceeds its size limit")
            relative = member.name.removeprefix(f"{ARCHIVE_DIRECTORY}/")
            stream = package.extractfile(member)
            if stream is None:
                raise WasmToolsStagingError("a wasm-tools archive file cannot be read")
            content = stream.read(member.size + 1)
            if len(content) != member.size:
                raise WasmToolsStagingError("a wasm-tools archive file has an invalid size")
            if hashlib.sha256(content).hexdigest() != EXPECTED_FILES[relative]:
                raise WasmToolsStagingError(
                    f"the pinned wasm-tools file '{relative}' has an invalid hash"
                )
            files[relative] = content
    return files


@contextmanager
def _generation_transaction(generation_root: Path) -> Iterator[Path]:
    generation_root.parent.mkdir(parents=True, exist_ok=True)
    if generation_root.exists() or generation_root.is_symlink():
        raise WasmToolsStagingError("the wasm-tools generation root already exists")
    temporary = generation_root.parent / f".{generation_root.name}.{uuid.uuid4().hex}.tmp"
    payload = temporary / "payload"
    payload.mkdir(parents=True)
    try:
        yield payload
        temporary.rename(generation_root)
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)


def stage(generation_root: Path, cache: Path) -> int:
    generation_root = generation_root.absolute()
    cache = cache.absolute()
    if _paths_overlap(generation_root, cache):
        raise WasmToolsStagingError(
            "the wasm-tools archive cache and generation root must not overlap"
        )
    archive = _download_archive(cache)
    files = _read_distribution(archive)
    with _generation_transaction(generation_root) as payload:
        for relative, content in sorted(files.items()):
            path = payload / relative
            path.write_bytes(content)
        marker = {
            "schemaVersion": 1,
            "state": "complete",
            "payload": "payload",
            "canonicalRoot": "payload",
            "version": VERSION,
            "archive": ARCHIVE_NAME,
            "archiveSha256": ARCHIVE_SHA256,
            "fileCount": len(files),
        }
        (payload.parent / "generation.json").write_text(
            json.dumps(marker, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    print(f"files={len(files)} version={VERSION}")
    print(f"generation={generation_root}")
    return len(files)


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--generation-root", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    arguments = _arguments()
    try:
        stage(arguments.generation_root, arguments.cache)
    except (OSError, tarfile.TarError, WasmToolsStagingError) as exception:
        print(f"NW-WASM-TOOLS-STAGE-001: {exception}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

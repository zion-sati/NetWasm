#!/usr/bin/env python3

"""Materialize the audited, platform-neutral jco transpile closure.

The input package-lock is the source of truth for package bytes.  The checked-in
closure policy is deliberately separate from npm's host-pruned installation:
it names the exact static/runtime-qualified package paths and records why any
lock edge outside the transpile contract is omitted.
"""

from __future__ import annotations

import argparse
import base64
from contextlib import contextmanager
import hashlib
import io
import json
import os
import shutil
import sys
import tarfile
import unicodedata
import urllib.request
from urllib.parse import urlparse
import uuid
from pathlib import Path, PurePosixPath
from typing import Any, Iterable
from xml.sax.saxutils import escape

if os.name == "nt":
    import msvcrt
else:
    import fcntl


class ClosureStagingError(RuntimeError):
    """Raised when the lock or audited closure cannot be materialized."""


ROOT_PACKAGES = (
    "node_modules/@bytecodealliance/jco",
    "node_modules/@bytecodealliance/preview2-shim",
)
POLICY_SCHEMA_VERSION = "1"
POLICY_CONTRACT = "jco-transpile-only"
GENERATION_SCHEMA_VERSION = 1
GENERATION_PAYLOAD_NAME = "payload"
GENERATION_MARKER_NAME = "generation.json"
MAX_TARBALL_BYTES = 64_000_000
MAX_ARCHIVE_MEMBERS = 20_000
MAX_PACKAGE_EXPANDED_BYTES = 256_000_000
MAX_CLOSURE_EXPANDED_BYTES = 1_000_000_000
ACCEPTED_INVOCATION = [
    "transpile",
    "<component>",
    "--out-dir",
    "<output>",
    "--name",
    "program-component",
    "--instantiation",
    "async",
    "--strict",
    "--bindgen-enable-wasm-exnref",
    "--no-wasi-shim",
    "--quiet",
]


def _normalise_package_path(path: str) -> str:
    if not isinstance(path, str) or not path:
        raise ClosureStagingError("closure policy contains an empty package path")
    if "\\" in path:
        raise ClosureStagingError(f"closure policy path uses '\\': {path!r}")
    parts = path.split("/")
    if any(part in ("", ".", "..") for part in parts) or parts[0] != "node_modules":
        raise ClosureStagingError(f"unsafe closure policy package path {path!r}")
    for part in parts:
        _validate_portable_segment(part, path)
    return "/".join(parts)


def _portable_alias(path: str) -> str:
    return unicodedata.normalize("NFKC", path).casefold()


def _validate_portable_segment(segment: str, owner: str) -> None:
    if any(ord(character) < 32 or ord(character) == 127 for character in segment):
        raise ClosureStagingError(f"path contains a control character: {owner!r}")
    if any(character in '<>:"|?*' for character in segment) \
        or segment.endswith((".", " ")):
        raise ClosureStagingError(f"path contains a Windows-incompatible segment: {owner!r}")
    if ";" in segment or any(token in segment for token in ("$(", "@(", "%(")):
        raise ClosureStagingError(f"path contains an MSBuild-incompatible segment: {owner!r}")
    stem = segment.split(".", 1)[0].casefold()
    if stem in {"con", "prn", "aux", "nul"} or (
        len(stem) == 4 and stem[:3] in {"com", "lpt"} and stem[3].isdigit()
    ):
        raise ClosureStagingError(f"path contains a reserved Windows name: {owner!r}")


def _parse_sri(integrity: Any) -> bytes:
    if not isinstance(integrity, str) or not integrity or integrity != integrity.strip():
        raise ClosureStagingError(f"invalid package integrity '{integrity}'")
    tokens = integrity.split()
    if len(tokens) != 1 or not tokens[0].startswith("sha512-"):
        raise ClosureStagingError(f"invalid package integrity '{integrity}'")
    encoded = tokens[0][len("sha512-"):]
    if len(encoded) != 88 or not encoded.endswith("=="):
        raise ClosureStagingError(f"invalid package integrity '{integrity}'")
    try:
        decoded = base64.b64decode(encoded, validate=True)
    except (ValueError, base64.binascii.Error) as exception:
        raise ClosureStagingError(f"invalid package integrity '{integrity}'") from exception
    if len(decoded) != hashlib.sha512().digest_size \
        or base64.b64encode(decoded).decode("ascii") != encoded:
        raise ClosureStagingError(f"invalid package integrity '{integrity}'")
    return decoded


def _dependency_key(parent_key: str, dependency: str, records: dict[str, Any]) -> str:
    if not isinstance(dependency, str) or not dependency:
        raise ClosureStagingError(f"invalid dependency of '{parent_key}'")
    candidate = (
        f"{parent_key}/node_modules/{dependency}"
        if parent_key
        else f"node_modules/{dependency}"
    )
    if candidate in records:
        return candidate

    cursor = parent_key
    while "/node_modules/" in cursor:
        cursor = cursor.rsplit("/node_modules/", 1)[0]
        candidate = f"{cursor}/node_modules/{dependency}"
        if candidate in records:
            return candidate

    candidate = f"node_modules/{dependency}"
    if candidate in records:
        return candidate

    raise ClosureStagingError(
        f"locked dependency '{dependency}' of '{parent_key}' is missing"
    )


def _policy_entry_map(policy: dict[str, Any], field: str) -> dict[str, dict[str, Any]]:
    values = policy.get(field)
    if not isinstance(values, list):
        raise ClosureStagingError(f"closure policy has no {field}")

    entries: dict[str, dict[str, Any]] = {}
    for value in values:
        if isinstance(value, str):
            path = value
            entry = {"path": value}
        elif isinstance(value, dict):
            path = value.get("path")
            entry = value
        else:
            raise ClosureStagingError(f"closure policy {field} contains an invalid entry")
        path = _normalise_package_path(path)
        if path in entries:
            raise ClosureStagingError(f"closure policy repeats package path '{path}'")
        entries[path] = entry
    return entries


def _validate_policy(policy: Any) -> tuple[set[str], dict[str, dict[str, Any]]]:
    if not isinstance(policy, dict):
        raise ClosureStagingError("closure policy is not an object")
    if policy.get("schemaVersion") != POLICY_SCHEMA_VERSION:
        raise ClosureStagingError("closure policy schema is unsupported")
    if policy.get("contract") != POLICY_CONTRACT:
        raise ClosureStagingError("closure policy is not the transpile-only contract")
    if policy.get("roots") != list(ROOT_PACKAGES):
        raise ClosureStagingError("closure policy roots do not match the canonical roots")
    if policy.get("entryPoint") != f"{ROOT_PACKAGES[0]}/dist/jco.js":
        raise ClosureStagingError("closure policy entry point is not dist/jco.js")
    commands = policy.get("requiredCommands")
    if not isinstance(commands, list) or commands != ["transpile"]:
        raise ClosureStagingError("closure policy must qualify only the transpile command")
    if policy.get("acceptedInvocation") != ACCEPTED_INVOCATION:
        raise ClosureStagingError("closure policy accepted invocation is not canonical")

    selected_entries = _policy_entry_map(policy, "selectedPackagePaths")
    if not selected_entries:
        raise ClosureStagingError("closure policy has no selectedPackagePaths")
    compiler_closure = set(_policy_entry_map(policy, "compilerClosure"))
    runtime_shim_closure = set(_policy_entry_map(policy, "runtimeShimClosure"))
    if compiler_closure & runtime_shim_closure:
        overlap = sorted(compiler_closure & runtime_shim_closure)[0]
        raise ClosureStagingError(
            f"closure policy assigns package '{overlap}' to both compiler and runtime closures"
        )
    if compiler_closure | runtime_shim_closure != set(selected_entries):
        raise ClosureStagingError(
            "closure policy compiler/runtime closure union does not match selectedPackagePaths"
        )
    if ROOT_PACKAGES[0] not in compiler_closure:
        raise ClosureStagingError("closure policy compilerClosure omits the jco root")
    if ROOT_PACKAGES[1] not in runtime_shim_closure:
        raise ClosureStagingError("closure policy runtimeShimClosure omits the Preview 2 shim root")
    excluded = _policy_entry_map(policy, "excludedPackagePaths")
    selected = set(selected_entries)
    if selected & excluded.keys():
        overlap = sorted(selected & excluded.keys())[0]
        raise ClosureStagingError(f"closure policy both selects and excludes '{overlap}'")
    for path, entry in selected_entries.items():
        for field in ("name", "version", "resolved", "integrity"):
            value = entry.get(field)
            if not isinstance(value, str) or not value.strip():
                raise ClosureStagingError(
                    f"selected package '{path}' has no policy {field} pin"
                )
    for path, entry in excluded.items():
        reason = entry.get("reason")
        evidence = entry.get("evidence")
        if not isinstance(reason, str) or not reason.strip():
            raise ClosureStagingError(f"excluded package '{path}' has no reason")
        if not isinstance(evidence, str) or not evidence.strip():
            raise ClosureStagingError(f"excluded package '{path}' has no evidence reference")
    return selected, excluded


def _read_policy(policy_path: Path) -> tuple[set[str], dict[str, dict[str, Any]]]:
    try:
        policy = json.loads(policy_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ClosureStagingError(f"cannot read closure policy '{policy_path}'") from exception
    return _validate_policy(policy)


def _record_dependencies(record: dict[str, Any]) -> Iterable[tuple[str, str]]:
    dependencies = record.get("dependencies", {})
    optional_dependencies = record.get("optionalDependencies", {})
    peer_dependencies = record.get("peerDependencies", {})
    peer_meta = record.get("peerDependenciesMeta", {})
    if not isinstance(peer_meta, dict):
        raise ClosureStagingError("lock record has invalid peer dependency metadata")
    for field, values in (
        ("dependency", dependencies),
        ("optional", optional_dependencies),
        ("peer", peer_dependencies),
    ):
        if not isinstance(values, dict):
            raise ClosureStagingError(f"lock record has invalid {field} dependencies")
        if any(
            not isinstance(dependency, str)
            or not dependency.strip()
            or not isinstance(spec, str)
            or not spec.strip()
            for dependency, spec in values.items()
        ):
            raise ClosureStagingError(f"lock record has invalid {field} dependency values")
        for dependency in sorted(values):
            kind = field
            if field == "peer":
                metadata = peer_meta.get(dependency)
                if metadata is not None and not isinstance(metadata, dict):
                    raise ClosureStagingError(
                        f"lock record has invalid metadata for peer '{dependency}'"
                    )
                if isinstance(metadata, dict) and metadata.get("optional") is True:
                    kind = "optional-peer"
                if isinstance(metadata, dict) and "optional" in metadata \
                    and not isinstance(metadata["optional"], bool):
                    raise ClosureStagingError(
                        f"lock record has invalid optional flag for peer '{dependency}'"
                    )
            yield dependency, kind


def select_locked_closure(
    lock: dict[str, Any],
    policy: dict[str, Any],
) -> dict[str, dict[str, Any]]:
    if not isinstance(lock, dict):
        raise ClosureStagingError("package-lock.json is not an object")
    if lock.get("lockfileVersion") != 3:
        raise ClosureStagingError("package-lock.json lockfileVersion is unsupported")
    records = lock.get("packages")
    if not isinstance(records, dict):
        raise ClosureStagingError("package-lock.json has no package records")

    root_record = records.get("")
    if not isinstance(root_record, dict):
        raise ClosureStagingError("package-lock.json has no root package record")
    if root_record.get("name") != lock.get("name"):
        raise ClosureStagingError("package-lock.json root package identity differs")
    root_dependencies = root_record.get("devDependencies")
    if not isinstance(root_dependencies, dict) or any(
        not isinstance(name, str)
        or not name.strip()
        or not isinstance(specification, str)
        or not specification.strip()
        for name, specification in root_dependencies.items()
    ):
        raise ClosureStagingError("package-lock.json has invalid root devDependencies")

    selected_entries = _policy_entry_map(policy, "selectedPackagePaths")
    excluded = _policy_entry_map(policy, "excludedPackagePaths")
    _validate_policy(policy)
    selected = set(selected_entries)
    for root in ROOT_PACKAGES:
        if root not in selected:
            raise ClosureStagingError(f"closure policy omits required root '{root}'")

    lock_paths = {key for key in records if key}
    declared_paths = selected | set(excluded)
    if lock_paths != declared_paths:
        missing = sorted(lock_paths - declared_paths)
        extra = sorted(declared_paths - lock_paths)
        detail = missing[0] if missing else extra[0]
        raise ClosureStagingError(
            f"closure policy does not exactly cover locked package paths; first mismatch '{detail}'"
        )

    root_targets = {
        _dependency_key("", dependency, records)
        for dependency in root_dependencies
    }
    if not set(ROOT_PACKAGES).issubset(root_targets):
        raise ClosureStagingError(
            "package-lock.json root devDependencies omit a canonical closure root"
        )
    if not root_targets.issubset(declared_paths):
        raise ClosureStagingError(
            "package-lock.json root devDependencies escape the audited policy"
        )

    aliases: dict[str, str] = {}
    for key in sorted(lock_paths):
        normalized_key = _portable_alias(key)
        previous = aliases.setdefault(normalized_key, key)
        if previous != key:
            raise ClosureStagingError(
                f"package-lock.json contains case/Unicode-alias package paths '{previous}' and '{key}'"
            )
        record = records.get(key)
        if not isinstance(record, dict):
            raise ClosureStagingError(f"locked package '{key}' is missing")
        _validate_locked_record(key, record, selected_entries.get(key))
        for dependency, _ in _record_dependencies(record):
            _dependency_key(key, dependency, records)

    # Walk only selected records. A dependency may be omitted from the
    # transpile-only contract only when the policy names that exact lock edge
    # as excluded. This makes dynamic componentize/bundle families explicit
    # instead of accidentally inheriting npm's platform-pruned graph.
    reached: set[str] = set()
    queue = list(ROOT_PACKAGES)
    while queue:
        key = queue.pop()
        if key in reached:
            continue
        if key not in selected:
            raise ClosureStagingError(f"selected closure reaches unselected package '{key}'")
        reached.add(key)
        record = records[key]
        for dependency, kind in _record_dependencies(record):
            target = _dependency_key(key, dependency, records)
            if target in selected:
                queue.append(target)
                continue
            if kind == "peer":
                raise ClosureStagingError(
                    f"required peer edge '{key}' -> '{target}' cannot be excluded"
                )
            exclusion = excluded.get(target)
            if exclusion is None:
                raise ClosureStagingError(
                    f"{kind} edge '{key}' -> '{target}' is absent from selected closure "
                    "and has no explicit exclusion"
                )

    if reached != selected:
        unreachable = sorted(selected - reached)[0]
        raise ClosureStagingError(f"selected closure package '{unreachable}' is unreachable")

    return {key: records[key] for key in sorted(selected)}


def _validate_locked_record(
    key: str,
    record: dict[str, Any],
    policy_entry: dict[str, Any] | None,
) -> None:
    _normalise_package_path(key)
    version = record.get("version")
    resolved = record.get("resolved")
    integrity = record.get("integrity")
    if not isinstance(version, str) or not version.strip():
        raise ClosureStagingError(f"locked package '{key}' has no version")
    if not isinstance(resolved, str) or not resolved.startswith("https://"):
        raise ClosureStagingError(f"locked package '{key}' has no HTTPS tarball URL")
    _parse_sri(integrity)
    if policy_entry is not None:
        for field, actual in (
            ("version", version),
            ("resolved", resolved),
            ("integrity", integrity),
        ):
            if policy_entry.get(field) != actual:
                raise ClosureStagingError(
                    f"closure policy {field} pin for '{key}' differs from package-lock.json"
                )
        if not isinstance(policy_entry.get("name"), str) or not policy_entry["name"].strip():
            raise ClosureStagingError(f"closure policy has no package name for '{key}'")


def _integrity_digest(integrity: str, payload: bytes) -> None:
    expected = _parse_sri(integrity)
    if hashlib.sha512(payload).digest() != expected:
        raise ClosureStagingError("package tarball digest does not match package-lock.json")


def _read_bounded(stream: Any, limit: int, description: str) -> bytes:
    payload = stream.read(limit + 1)
    if len(payload) > limit:
        raise ClosureStagingError(f"{description} exceeds the {limit}-byte limit")
    return payload


def _cache_tarball(record: dict[str, Any], cache: Path) -> bytes:
    integrity = record["integrity"]
    digest = _parse_sri(integrity)
    cache_path = cache / f"sha512-{digest.hex()}.tgz"
    if cache.exists() and (cache.is_symlink() or not cache.is_dir()):
        raise ClosureStagingError(f"jco tarball cache is not a regular directory: {cache}")
    cache.mkdir(parents=True, exist_ok=True)
    # The reusable audit cache predates the hex cache name and stores the
    # canonical digest using a deliberately URL/filesystem-safe alphabet.  Do
    # not derive a path from the raw SRI token: even a seemingly harmless
    # sanitiser can make distinct tokens collide, and an invalid token must
    # never influence a filesystem path.
    safe_integrity_path = cache / (
        "sha512-"
        + base64.urlsafe_b64encode(digest).decode("ascii")
        .replace("-", "_")
        .replace("=", "_")
        + ".tgz"
    )
    for candidate in (cache_path, safe_integrity_path):
        if candidate.is_symlink():
            raise ClosureStagingError(f"jco tarball cache contains a symbolic link: {candidate}")
        if candidate.exists() and not candidate.is_file():
            raise ClosureStagingError(
                f"jco tarball cache entry is not a regular file: {candidate}"
            )
    existing_path = cache_path if cache_path.is_file() else safe_integrity_path
    if existing_path.is_file():
        if existing_path.stat().st_size > MAX_TARBALL_BYTES:
            raise ClosureStagingError(
                f"cached package tarball exceeds the {MAX_TARBALL_BYTES}-byte limit"
            )
        with existing_path.open("rb") as stream:
            payload = _read_bounded(
                stream, MAX_TARBALL_BYTES, "cached package tarball"
            )
        _integrity_digest(integrity, payload)
        return payload

    url = record["resolved"]
    if not isinstance(url, str) or not url.startswith("https://"):
        raise ClosureStagingError(f"locked package URL is not HTTPS: {url!r}")
    try:
        with urllib.request.urlopen(url, timeout=60) as response:
            final_url = response.geturl()
            if not isinstance(final_url, str) or urlparse(final_url).scheme.lower() != "https":
                raise ClosureStagingError(
                    f"locked package URL redirected away from HTTPS: {final_url!r}"
                )
            payload = _read_bounded(
                response, MAX_TARBALL_BYTES, "downloaded package tarball"
            )
    except OSError as exception:
        raise ClosureStagingError(f"failed to download locked package '{url}'") from exception
    _integrity_digest(integrity, payload)
    temporary = cache / f".{cache_path.name}.{os.getpid()}.{uuid.uuid4().hex}.tmp"
    try:
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
        flags |= getattr(os, "O_NOFOLLOW", 0)
        try:
            descriptor = os.open(temporary, flags, 0o644)
        except OSError as exception:
            raise ClosureStagingError(
                f"cannot create an exclusive jco tarball cache entry: {temporary}"
            ) from exception
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
        try:
            # A hard link is the no-overwrite publication primitive for a
            # digest-addressed cache entry.  os.replace() would let a losing
            # concurrent downloader overwrite a validated winner.
            os.link(temporary, cache_path, follow_symlinks=False)
        except FileExistsError:
            if cache_path.is_symlink() or not cache_path.is_file():
                raise ClosureStagingError(
                    f"jco tarball cache entry was replaced by a non-file: {cache_path}"
                )
            winner = cache_path.read_bytes()
            _integrity_digest(integrity, winner)
    finally:
        temporary.unlink(missing_ok=True)
    return payload


def _safe_member_path(member_name: str) -> PurePosixPath | None:
    if not isinstance(member_name, str) or not member_name:
        raise ClosureStagingError("package tarball contains an empty member name")
    if "\\" in member_name:
        raise ClosureStagingError(f"package tarball member uses '\\': '{member_name}'")
    normalized = member_name.rstrip("/")
    if "//" in normalized:
        raise ClosureStagingError(f"unsafe package tarball member '{member_name}'")
    parts = normalized.split("/")
    if not parts or any(part in ("", ".", "..") for part in parts):
        raise ClosureStagingError(f"unsafe package tarball member '{member_name}'")
    for part in parts:
        _validate_portable_segment(part, member_name)
    if parts == ["package"]:
        return None
    if parts[0] != "package" or len(parts) == 1:
        raise ClosureStagingError(f"unexpected package tarball member '{member_name}'")
    return PurePosixPath(*parts[1:])


def _extract_package(
    payload: bytes,
    target: Path,
    version: str,
    expected_name: str | None = None,
    expected_record: dict[str, Any] | None = None,
    remaining_closure_bytes: int | None = None,
) -> int:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() or target.is_symlink():
        raise ClosureStagingError(
            f"locked package target already exists and has another owner: {target}"
        )
    target.mkdir(exist_ok=False)
    try:
        try:
            archive = tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz")
            seen: set[PurePosixPath] = set()
            aliases: dict[str, PurePosixPath] = {}
            file_count = 0
            expanded_bytes = 0
            with archive:
                member_count = 0
                for member in archive:
                    member_count += 1
                    if member_count > MAX_ARCHIVE_MEMBERS:
                        raise ClosureStagingError(
                            f"package tarball exceeds the {MAX_ARCHIVE_MEMBERS}-member limit"
                        )
                    relative = _safe_member_path(member.name)
                    if relative is None:
                        if not member.isdir():
                            raise ClosureStagingError(
                                "package tarball root entry must be a directory"
                            )
                        continue
                    if relative in seen:
                        raise ClosureStagingError(
                            f"package tarball contains duplicate member '{member.name}'"
                        )
                    seen.add(relative)
                    alias = _portable_alias(relative.as_posix())
                    previous = aliases.setdefault(alias, relative)
                    if previous != relative:
                        raise ClosureStagingError(
                            f"package tarball contains case/Unicode-alias members "
                            f"'{previous}' and '{relative}'"
                        )
                    destination = target.joinpath(*relative.parts)
                    if member.issym() or member.islnk():
                        raise ClosureStagingError(
                            f"package tarball contains an unsupported link '{member.name}'"
                        )
                    if member.isdir():
                        if destination.exists() and not destination.is_dir():
                            raise ClosureStagingError(
                                f"package tarball collides with a file at '{member.name}'"
                            )
                        destination.mkdir(parents=True, exist_ok=True)
                        continue
                    if not member.isfile():
                        raise ClosureStagingError(
                            f"package tarball contains unsupported member '{member.name}'"
                        )
                    if member.size < 0:
                        raise ClosureStagingError(
                            f"package tarball member has an invalid size: '{member.name}'"
                        )
                    expanded_bytes += member.size
                    if expanded_bytes > MAX_PACKAGE_EXPANDED_BYTES:
                        raise ClosureStagingError(
                            "package tarball exceeds the "
                            f"{MAX_PACKAGE_EXPANDED_BYTES}-byte expanded limit"
                        )
                    if (
                        remaining_closure_bytes is not None
                        and expanded_bytes > remaining_closure_bytes
                    ):
                        raise ClosureStagingError(
                            "jco closure exceeds the "
                            f"{MAX_CLOSURE_EXPANDED_BYTES}-byte expanded limit"
                        )
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    source = archive.extractfile(member)
                    if source is None:
                        raise ClosureStagingError(f"package tarball member '{member.name}' is unreadable")
                    if destination.exists():
                        raise ClosureStagingError(
                            f"package tarball collides with an existing member '{member.name}'"
                        )
                    data = _read_bounded(source, member.size, "package tarball member")
                    if len(data) != member.size:
                        raise ClosureStagingError(
                            f"package tarball member is truncated: '{member.name}'"
                        )
                    destination.write_bytes(data)
                    destination.chmod(member.mode & 0o777)
                    file_count += 1

            metadata_path = target / "package.json"
            if file_count == 0 or not metadata_path.is_file():
                raise ClosureStagingError(f"locked package {target} has no package.json")
            try:
                metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            except (OSError, UnicodeError, json.JSONDecodeError) as exception:
                raise ClosureStagingError(f"locked package {target} has invalid package.json") from exception
            if not isinstance(metadata, dict):
                raise ClosureStagingError(f"locked package {target} package.json is not an object")
            if metadata.get("version") != version:
                raise ClosureStagingError(
                    f"package {target} version {metadata.get('version')!r} does not match lock {version!r}"
                )
            if expected_name is not None and metadata.get("name") != expected_name:
                raise ClosureStagingError(
                    f"package {target} name {metadata.get('name')!r} does not match policy "
                    f"name {expected_name!r}"
                )
            if expected_record is not None:
                for field in (
                    "dependencies",
                    "optionalDependencies",
                    "peerDependencies",
                    "peerDependenciesMeta",
                ):
                    lock_value = expected_record.get(field, {})
                    package_value = metadata.get(field, {})
                    if not isinstance(lock_value, dict) or not isinstance(package_value, dict):
                        raise ClosureStagingError(
                            f"package {target} has invalid {field} declarations"
                        )
                    if package_value != lock_value:
                        raise ClosureStagingError(
                            f"package {target} {field} declarations differ from package-lock.json"
                        )
            return expanded_bytes
        except ClosureStagingError:
            raise
        except (OSError, UnicodeError, tarfile.TarError, EOFError, ValueError) as exception:
            raise ClosureStagingError(
                "locked package tarball is not a readable gzip archive"
            ) from exception
    except Exception:
        # The target is private staging state. Never leave a partially
        # extracted package available for a later package to merge into.
        shutil.rmtree(target, ignore_errors=True)
        raise


@contextmanager
def _generation_transaction(generation_root: Path):
    """Build privately and publish one immutable generation atomically.

    The payload is never visible under the requested root while it is being
    generated. The complete staging directory, including its completion marker,
    is published with one same-filesystem rename. A crash therefore leaves
    either no requested root or one complete generation; no recovery path
    deletes or repairs a requested output.
    """
    generation_root = Path(generation_root)
    parent = generation_root.parent
    parent.mkdir(parents=True, exist_ok=True)
    lock_path = parent / f".{generation_root.name}.publication.lock"
    if lock_path.is_symlink():
        raise ClosureStagingError(f"generation lock is a symbolic link: {lock_path}")
    flags = os.O_RDWR | os.O_CREAT
    flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(lock_path, flags, 0o600)
        handle = os.fdopen(descriptor, "a+b")
    except OSError as exception:
        raise ClosureStagingError(
            f"cannot reserve generation root near '{parent}'"
        ) from exception

    staging_root = parent / (
        f".{generation_root.name}.staging-{os.getpid()}-{uuid.uuid4().hex}"
    )
    try:
        if os.name == "nt":
            if handle.tell() == 0:
                handle.write(b"0")
                handle.flush()
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_LOCK, 1)
        else:
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX)

        if generation_root.exists() or generation_root.is_symlink():
            raise ClosureStagingError(
                f"staging generation root already exists: {generation_root}"
            )
        staging_root.mkdir(mode=0o700)
        payload_root = staging_root / GENERATION_PAYLOAD_NAME
        payload_root.mkdir()
        marker = staging_root / GENERATION_MARKER_NAME
        yield payload_root, marker
        if marker.is_symlink() or not marker.is_file():
            raise ClosureStagingError("the staged generation has no completion marker")

        try:
            os.rename(staging_root, generation_root)
        except FileExistsError as exception:
            raise ClosureStagingError(
                f"staging generation root was claimed concurrently: {generation_root}"
            ) from exception
    finally:
        shutil.rmtree(staging_root, ignore_errors=True)
        try:
            if os.name == "nt":
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
        finally:
            handle.close()


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _files(root: Path) -> Iterable[Path]:
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ClosureStagingError(f"closure contains a symbolic link: {path}")
        if path.is_file():
            yield path


def _paths_overlap(first: Path, second: Path) -> bool:
    physical_first = Path(_portable_alias(str(first.resolve(strict=False))))
    physical_second = Path(_portable_alias(str(second.resolve(strict=False))))
    return (
        physical_first == physical_second
        or physical_first in physical_second.parents
        or physical_second in physical_first.parents
    )


def _write_integrity(
    root: Path,
    manifest_path: Path,
    files_root: Path | None = None,
) -> list[dict[str, str]]:
    files_root = files_root or root / "node_modules"
    files = [
        {
            "path": path.relative_to(root).as_posix(),
            "sha256": _sha256(path),
        }
        for path in _files(files_root)
    ]
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps({"schemaVersion": "1", "files": files}, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    return files


def _write_pack_items(
    source_root: Path,
    output: Path,
    package_prefix: str,
    asset_root_property: str = "NetWasmToolchainJcoAssetRoot",
) -> None:
    items = []
    for path in _files(source_root / "node_modules"):
        relative = path.relative_to(source_root).as_posix()
        # Keep the generated import deterministic across external staging
        # roots.  The project supplies this property when it imports the
        # props; embedding the private temporary absolute path would make
        # otherwise identical package bytes differ between machines.
        include = escape(
            f"$({asset_root_property})/{relative}",
            {'"': "&quot;"},
        )
        # NuGet treats an extensionless PackagePath as a directory and appends
        # the source filename (LICENSE -> LICENSE/LICENSE). Use the parent
        # directory for such files so the extracted package remains byte- and
        # path-identical to the audited node_modules closure.
        if path.suffix == "" and not path.name.startswith("."):
            parent = relative.rsplit("/", 1)[0] if "/" in relative else ""
            package_path = f"{package_prefix}/{parent}/"
            target_path = escape(path.name, {'"': "&quot;"})
            target_metadata = f' TargetPath="{target_path}"'
        else:
            package_path = f"{package_prefix}/{relative}"
            target_metadata = ""
        package_path = escape(package_path, {'"': "&quot;"})
        items.append(
            f'    <None Include="{include}" Pack="true" PackagePath="{package_path}"'
            f'{target_metadata} />'
        )
    output.write_text(
        "<Project>\n  <PropertyGroup>\n"
        "    <NoDefaultExcludes>true</NoDefaultExcludes>\n"
        "  </PropertyGroup>\n  <ItemGroup>\n"
        + "\n".join(items)
        + "\n  </ItemGroup>\n</Project>\n",
        encoding="utf-8",
        newline="\n",
    )


def _write_notices(root: Path, selected: dict[str, dict[str, Any]], output: Path) -> None:
    packages = []
    for key, record in selected.items():
        package_root = root / key
        metadata = json.loads((package_root / "package.json").read_text(encoding="utf-8"))
        license_files = [
            path.relative_to(package_root).as_posix()
            for path in _files(package_root)
            if ("license" in path.name.lower() or "notice" in path.name.lower())
            and "node_modules" not in path.relative_to(package_root).parts
        ]
        packages.append(
            {
                "lockPath": key,
                "name": metadata.get("name"),
                "version": record["version"],
                "license": metadata.get("license", record.get("license")),
                "resolved": record["resolved"],
                "integrity": record["integrity"],
                "licenseFiles": license_files,
            }
        )
    output.write_text(
        json.dumps(
            {"schemaVersion": "1", "packages": packages}, indent=2, sort_keys=True
        ) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def stage(
    lock_path: Path,
    policy_path: Path,
    generation_root: Path,
    cache: Path,
) -> int:
    generation_root = Path(generation_root).absolute()
    cache = Path(cache).absolute()
    if _paths_overlap(generation_root, cache):
        raise ClosureStagingError(
            "the jco tarball cache and generation root must not overlap"
        )

    try:
        lock_bytes = lock_path.read_bytes()
        policy_bytes = policy_path.read_bytes()
        lock = json.loads(lock_bytes.decode("utf-8"))
        policy = json.loads(policy_bytes.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ClosureStagingError("cannot read the locked jco closure inputs") from exception
    _validate_policy(policy)
    lock_sha256 = policy.get("lockSha256")
    if not isinstance(lock_sha256, str) or len(lock_sha256) != 64:
        raise ClosureStagingError("closure policy has no valid package-lock SHA-256 pin")
    if hashlib.sha256(lock_bytes).hexdigest() != lock_sha256:
        raise ClosureStagingError("closure policy package-lock SHA-256 pin does not match")
    selected = select_locked_closure(lock, policy)
    selected_entries = _policy_entry_map(policy, "selectedPackagePaths")

    with _generation_transaction(generation_root) as (payload_root, marker):
        canonical_stage = payload_root
        canonical_lock = canonical_stage / "package-lock.json"
        canonical_integrity = canonical_stage / "closure-integrity.json"
        canonical_pack_items = canonical_stage / "closure-pack-items.props"
        canonical_notices = canonical_stage / "notices.json"
        canonical_policy = canonical_stage / "closure-policy.json"

        closure_expanded_bytes = 0
        for key, record in selected.items():
            payload = _cache_tarball(record, cache)
            closure_expanded_bytes += _extract_package(
                payload,
                canonical_stage / key,
                record["version"],
                selected_entries[key]["name"],
                record,
                MAX_CLOSURE_EXPANDED_BYTES - closure_expanded_bytes,
            )
            if closure_expanded_bytes > MAX_CLOSURE_EXPANDED_BYTES:
                raise ClosureStagingError(
                    "jco closure exceeds the "
                    f"{MAX_CLOSURE_EXPANDED_BYTES}-byte expanded limit"
                )

        entry_point = canonical_stage / "node_modules/@bytecodealliance/jco/dist/jco.js"
        if not entry_point.is_file():
            raise ClosureStagingError("canonical jco entrypoint is missing")
        canonical_lock.write_bytes(lock_bytes)
        canonical_policy.write_bytes(policy_bytes)
        integrity = _write_integrity(canonical_stage, canonical_integrity)
        _write_pack_items(canonical_stage, canonical_pack_items, "tools/jco")
        _write_notices(canonical_stage, selected, canonical_notices)

        marker_payload = {
            "schemaVersion": GENERATION_SCHEMA_VERSION,
            "state": "complete",
            "payload": GENERATION_PAYLOAD_NAME,
            "canonicalRoot": GENERATION_PAYLOAD_NAME,
            "packageCount": len(selected),
            "fileCount": len(integrity),
        }
        _write_exclusive_json(marker, marker_payload)

    print(f"packages={len(selected)} files={len(integrity)}")
    print(f"generation={generation_root}")
    return len(selected)
def _write_exclusive_json(path: Path, document: dict[str, Any]) -> None:
    payload = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode("utf-8")
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags, 0o644)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
    except OSError as exception:
        raise ClosureStagingError(f"cannot publish generation marker '{path}'") from exception


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
        print(f"NW-JCO-STAGE-001: {exception}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

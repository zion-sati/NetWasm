#!/usr/bin/env python3

"""Publish a verified NetWasm release in dependency-safe feed stages."""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
import stat
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath
from typing import Callable
from xml.etree import ElementTree


SOURCE_INDEX = "https://api.nuget.org/v3/index.json"
PUSH_SOURCE = SOURCE_INDEX
PUSH_TIMEOUT_SECONDS = 2 * 60
MAX_PARALLEL_PUSHES = 4
FINAL_IDS = ("NetWasm.Sdk", "NetWasm.Templates")
STAGE_NAMES = ("prerequisites", "final")


def package_dependencies(path: Path) -> set[str]:
    with zipfile.ZipFile(path) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError(f"Package must contain exactly one nuspec: {path.name}.")
        root = ElementTree.fromstring(archive.read(nuspecs[0]))
    metadata = root.find("{*}metadata")
    if metadata is None:
        raise ValueError(f"Package metadata is missing: {path.name}.")
    dependencies = metadata.find("{*}dependencies")
    if dependencies is None:
        return set()
    return {
        value
        for dependency in dependencies.findall(".//{*}dependency")
        if (value := dependency.get("id"))
    }


def publication_stages(
    manifest: dict[str, object], packages_root: Path
) -> tuple[tuple[tuple[str, Path], ...], ...]:
    version = manifest.get("releaseVersion")
    package_ids = manifest.get("packages")
    if not isinstance(version, str) or not version or not isinstance(package_ids, list):
        raise ValueError("Release manifest is missing its version or package IDs.")
    if not package_ids or any(not isinstance(item, str) or not item for item in package_ids):
        raise ValueError("Release manifest has invalid package IDs.")
    if len(package_ids) != len(set(package_ids)):
        raise ValueError("Release manifest has duplicate package IDs.")
    if any(package_id not in package_ids for package_id in FINAL_IDS):
        raise ValueError("Release manifest must contain the SDK and templates.")

    expected = {
        package_id: packages_root / f"{package_id}.{version}.nupkg"
        for package_id in package_ids
    }
    actual = set(packages_root.glob("*.nupkg"))
    if set(expected.values()) != actual or any(
        not path.is_file() for path in expected.values()
    ):
        raise ValueError("Release package files do not match the manifest exactly.")
    template_dependencies = package_dependencies(expected["NetWasm.Templates"])
    if "NetWasm.Sdk" in template_dependencies:
        raise ValueError(
            "NetWasm.Templates depends on NetWasm.Sdk and cannot share its publication wave."
        )

    prerequisites = tuple(
        (package_id, expected[package_id])
        for package_id in sorted(package_ids)
        if package_id not in FINAL_IDS
    )
    return (
        prerequisites,
        tuple(
            (package_id, expected[package_id])
            for package_id in FINAL_IDS
        ),
    )


def package_base_address(
    fetch: Callable[..., object] = urllib.request.urlopen,
) -> str:
    with fetch(SOURCE_INDEX, timeout=15) as response:
        index = json.load(response)
    for resource in index.get("resources", []):
        kinds = resource.get("@type", [])
        if isinstance(kinds, str):
            kinds = [kinds]
        if "PackageBaseAddress/3.0.0" in kinds:
            address = resource.get("@id")
            if isinstance(address, str) and address.startswith("https://"):
                return address.rstrip("/") + "/"
    raise ValueError("NuGet.org did not advertise a package-content endpoint.")


def available_on_feed(
    package_id: str,
    version: str,
    base_address: str,
    fetch: Callable[..., object] = urllib.request.urlopen,
) -> bool:
    lower_id = package_id.lower()
    lower_version = version.lower()
    content_url = package_content_url(package_id, version, base_address)
    try:
        with fetch(f"{base_address}{lower_id}/index.json", timeout=15) as response:
            if lower_version not in json.load(response).get("versions", []):
                return False
        with fetch(urllib.request.Request(content_url, method="HEAD"), timeout=15) as response:
            return response.status == 200
    except urllib.error.HTTPError as error:
        if error.code == 404 or error.code == 429 or error.code >= 500:
            return False
        raise
    except (TimeoutError, urllib.error.URLError):
        return False


def package_content_url(package_id: str, version: str, base_address: str) -> str:
    lower_id = package_id.lower()
    lower_version = version.lower()
    return (
        f"{base_address}{lower_id}/{lower_version}/"
        f"{lower_id}.{lower_version}.nupkg"
    )


def payload_digests(archive: zipfile.ZipFile) -> dict[str, tuple[int, str, int, int]]:
    entries = archive.infolist()
    names = [entry.filename for entry in entries]
    if len(names) != len(set(names)):
        raise ValueError("Package contains duplicate archive paths.")
    digests = {}
    for entry in entries:
        if entry.filename == ".signature.p7s":
            continue
        name = entry.filename
        parts = name.rstrip("/").split("/")
        if (
            not name or name.startswith("/") or "\\" in name or "\x00" in name
            or ".." in parts or "." in parts or "" in parts
            or ":" in parts[0] or PurePosixPath(name).is_absolute()
        ):
            raise ValueError(f"Package contains an unsafe archive path: {name}.")
        digest = hashlib.sha256()
        size = 0
        with archive.open(entry) as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
                size += len(block)
        if size != entry.file_size:
            raise ValueError(f"Package archive size changed while reading {name}.")
        mode = entry.external_attr >> 16
        digests[name] = (
            size,
            digest.hexdigest(),
            stat.S_IFMT(mode),
            mode & 0o111,
        )
    return digests


def verify_feed_payload(
    package_id: str,
    version: str,
    local_path: Path,
    base_address: str,
    fetch: Callable[..., object] = urllib.request.urlopen,
) -> None:
    url = package_content_url(package_id, version, base_address)
    max_bytes = local_path.stat().st_size + 1024 * 1024
    with tempfile.TemporaryFile() as downloaded:
        with fetch(url, timeout=60) as response:
            size = 0
            for block in iter(lambda: response.read(1024 * 1024), b""):
                size += len(block)
                if size > max_bytes:
                    raise ValueError(f"NuGet.org package is unexpectedly large: {package_id}.")
                downloaded.write(block)
        downloaded.seek(0)
        with zipfile.ZipFile(local_path) as local, zipfile.ZipFile(downloaded) as remote:
            if payload_digests(local) != payload_digests(remote):
                raise ValueError(
                    f"NuGet.org package content differs from the release candidate: "
                    f"{package_id} {version}."
                )


def push_or_reconcile(
    package_id: str,
    version: str,
    path: Path,
    base_address: str,
) -> bool:
    if available_on_feed(package_id, version, base_address):
        verify_feed_payload(package_id, version, path, base_address)
        print(f"Reusing identical {package_id} {version} on NuGet.org.", flush=True)
        return True
    print(f"Publishing {package_id} {version}.", flush=True)
    try:
        result = subprocess.run(
            [
                "dotnet", "nuget", "push", str(path),
                "--source", PUSH_SOURCE,
                "--skip-duplicate",
            ],
            timeout=PUSH_TIMEOUT_SECONDS,
            check=False,
        )
    except subprocess.TimeoutExpired as error:
        raise TimeoutError(
            f"Publishing timed out for {package_id} {version}. Retry the release; "
            "its preflight will reconcile any package accepted by NuGet.org."
        ) from error
    if result.returncode != 0:
        raise RuntimeError(f"Publishing failed for {package_id} {version}.")
    print(f"Submitted {package_id} {version} to NuGet.org.", flush=True)
    return False


def preflight_release(
    manifest: dict[str, object], packages_root: Path
) -> dict[str, object]:
    started = time.monotonic()
    stages = publication_stages(manifest, packages_root)
    version = manifest["releaseVersion"]
    assert isinstance(version, str)
    base_address = package_base_address()
    existing: list[str] = []
    absent: list[str] = []
    for package_id, path in (item for stage in stages for item in stage):
        if available_on_feed(package_id, version, base_address):
            verify_feed_payload(package_id, version, path, base_address)
            existing.append(package_id)
        else:
            absent.append(package_id)
    return {
        "schemaVersion": 1,
        "operation": "preflight",
        "version": version,
        "existing": sorted(existing),
        "absent": sorted(absent),
        "durationSeconds": round(time.monotonic() - started, 3),
    }


def publish_stage(
    manifest: dict[str, object], packages_root: Path, stage_name: str
) -> dict[str, object]:
    stages = publication_stages(manifest, packages_root)
    if stage_name not in STAGE_NAMES:
        raise ValueError(f"Unknown release publication stage: {stage_name}.")
    stage = stages[STAGE_NAMES.index(stage_name)]
    version = manifest["releaseVersion"]
    assert isinstance(version, str)
    base_address = package_base_address()
    started = time.monotonic()
    count = len(stage)
    print(f"Publishing {stage_name} ({count} package{'s' if count != 1 else ''}).", flush=True)
    verified: set[str] = set()
    workers = min(MAX_PARALLEL_PUSHES, count)
    with ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {
            executor.submit(
                push_or_reconcile,
                package_id,
                version,
                path,
                base_address,
            ): package_id
            for package_id, path in stage
        }
        for future in as_completed(futures):
            if future.result():
                verified.add(futures[future])
    finished = time.monotonic()
    print(f"Submitted the {stage_name} packages to NuGet.org.", flush=True)
    return {
        "schemaVersion": 1,
        "operation": "publish",
        "stage": stage_name,
        "version": version,
        "packageCount": count,
        "reusedCount": len(verified),
        "totalSeconds": round(finished - started, 3),
    }


def write_timing(path: Path | None, timing: dict[str, object]) -> None:
    if path is not None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(timing, indent=2) + "\n", encoding="utf-8")
    print("NETWASM_RELEASE_TIMING " + json.dumps(timing, sort_keys=True), flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--packages", required=True, type=Path)
    operation = parser.add_mutually_exclusive_group(required=True)
    operation.add_argument("--stage", choices=STAGE_NAMES)
    operation.add_argument("--preflight", action="store_true")
    parser.add_argument("--timing-output", type=Path)
    arguments = parser.parse_args()
    if arguments.stage is not None and not os.environ.get("NUGET_API_KEY"):
        parser.error("NUGET_API_KEY is required.")
    manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
    timing = (
        preflight_release(manifest, arguments.packages)
        if arguments.preflight
        else publish_stage(manifest, arguments.packages, arguments.stage)
    )
    write_timing(arguments.timing_output, timing)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

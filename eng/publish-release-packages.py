#!/usr/bin/env python3

"""Publish a verified NetWasm release in dependency-safe feed stages."""

from __future__ import annotations

import argparse
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


SOURCE_INDEX = "https://api.nuget.org/v3/index.json"
PUSH_SOURCE = SOURCE_INDEX
FEED_WAIT_SECONDS = 30 * 60
POLL_SECONDS = 15
STAGE_DEADLINE_SECONDS = 45 * 60
PUSH_TIMEOUT_SECONDS = 10 * 60
PUSH_ATTEMPTS = 3
DEPENDENT_IDS = ("NetWasm.Sdk", "NetWasm.Templates")
STAGE_NAMES = ("prerequisites", "SDK", "templates")


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
    if any(package_id not in package_ids for package_id in DEPENDENT_IDS):
        raise ValueError("Release manifest must contain the SDK and templates.")

    expected = {
        packages_root / f"{package_id}.{version}.nupkg": package_id
        for package_id in package_ids
    }
    actual = set(packages_root.glob("*.nupkg"))
    if set(expected) != actual or any(not path.is_file() for path in expected):
        raise ValueError("Release package files do not match the manifest exactly.")

    prerequisites = tuple(
        (package_id, packages_root / f"{package_id}.{version}.nupkg")
        for package_id in sorted(package_ids)
        if package_id not in DEPENDENT_IDS
    )
    return (
        prerequisites,
        (("NetWasm.Sdk", packages_root / f"NetWasm.Sdk.{version}.nupkg"),),
        (("NetWasm.Templates", packages_root / f"NetWasm.Templates.{version}.nupkg"),),
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


def wait_for_feed(
    package_ids: tuple[str, ...],
    version: str,
    base_address: str,
    *,
    timeout_seconds: int = FEED_WAIT_SECONDS,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
    probe: Callable[[str, str, str], bool] = available_on_feed,
) -> None:
    deadline = clock() + timeout_seconds
    pending = set(package_ids)
    while pending:
        for package_id in sorted(pending):
            if probe(package_id, version, base_address):
                pending.remove(package_id)
        if not pending:
            return
        if clock() >= deadline:
            raise TimeoutError(
                "NuGet.org did not make these packages available before the "
                f"deadline: {', '.join(sorted(pending))}"
            )
        print(f"Waiting for NuGet.org: {', '.join(sorted(pending))}", flush=True)
        sleep(min(POLL_SECONDS, max(0, deadline - clock())))


def push_or_reconcile(
    package_id: str,
    version: str,
    path: Path,
    base_address: str,
    deadline: float,
    *,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> bool:
    if available_on_feed(package_id, version, base_address):
        verify_feed_payload(package_id, version, path, base_address)
        print(f"Reusing identical {package_id} {version} on NuGet.org.", flush=True)
        return True
    for attempt in range(1, PUSH_ATTEMPTS + 1):
        remaining = deadline - clock()
        if remaining <= 0:
            break
        print(f"Publishing {package_id} {version} (attempt {attempt}).", flush=True)
        try:
            result = subprocess.run(
                [
                    "dotnet", "nuget", "push", str(path),
                    "--source", PUSH_SOURCE,
                    "--skip-duplicate",
                ],
                timeout=min(PUSH_TIMEOUT_SECONDS, remaining),
                check=False,
            )
            if result.returncode == 0:
                return False
        except subprocess.TimeoutExpired:
            print(f"Push timed out for {package_id}; reconciling feed state.", flush=True)
        if available_on_feed(package_id, version, base_address):
            verify_feed_payload(package_id, version, path, base_address)
            return True
        if attempt < PUSH_ATTEMPTS:
            sleep(min(POLL_SECONDS, max(0, deadline - clock())))
    remaining = deadline - clock()
    if remaining > 0:
        wait_for_feed((package_id,), version, base_address, timeout_seconds=remaining)
        verify_feed_payload(package_id, version, path, base_address)
        return True
    raise TimeoutError(f"Publishing deadline expired for {package_id} {version}.")


def publish_stage(
    manifest: dict[str, object], packages_root: Path, stage_name: str
) -> None:
    stages = publication_stages(manifest, packages_root)
    if stage_name not in STAGE_NAMES:
        raise ValueError(f"Unknown release publication stage: {stage_name}.")
    stage = stages[STAGE_NAMES.index(stage_name)]
    version = manifest["releaseVersion"]
    assert isinstance(version, str)
    base_address = package_base_address()
    deadline = time.monotonic() + STAGE_DEADLINE_SECONDS
    count = len(stage)
    print(f"Publishing {stage_name} ({count} package{'s' if count != 1 else ''}).", flush=True)
    verified = set()
    for package_id, path in stage:
        if push_or_reconcile(package_id, version, path, base_address, deadline):
            verified.add(package_id)
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError(f"Publishing deadline expired for the {stage_name} stage.")
    wait_for_feed(
        tuple(package_id for package_id, _ in stage), version, base_address,
        timeout_seconds=remaining,
    )
    for package_id, path in stage:
        if package_id not in verified:
            verify_feed_payload(package_id, version, path, base_address)
    print(f"NuGet.org serves the verified {stage_name} packages.", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--packages", required=True, type=Path)
    parser.add_argument("--stage", required=True, choices=STAGE_NAMES)
    arguments = parser.parse_args()
    if not os.environ.get("NUGET_API_KEY"):
        parser.error("NUGET_API_KEY is required.")
    manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
    publish_stage(manifest, arguments.packages, arguments.stage)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

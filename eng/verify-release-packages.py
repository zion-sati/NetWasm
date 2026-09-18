#!/usr/bin/env python3

"""Validate a release package set before a publishing credential is requested."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from xml.etree import ElementTree


def read_manifest(path: Path) -> dict[str, object]:
    manifest = json.loads(path.read_text(encoding="utf-8"))
    required = {
        "schemaVersion",
        "repository",
        "repositoryUrl",
        "releaseVersion",
        "releaseTag",
        "sourceCommit",
        "packages",
    }
    if set(manifest) != required:
        raise ValueError("Release manifest fields do not match schema version 1.")
    if manifest["schemaVersion"] != 1:
        raise ValueError("Unsupported release manifest schema.")
    packages = manifest["packages"]
    if not isinstance(packages, list) or not packages or any(
        not isinstance(package, str) or not package for package in packages
    ):
        raise ValueError("Release manifest packages must be a non-empty string list.")
    if len(packages) != len(set(packages)):
        raise ValueError("Release manifest contains duplicate package IDs.")
    return manifest


def git(source_root: Path, *arguments: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(source_root), *arguments],
        stderr=subprocess.STDOUT,
        text=True,
    ).strip()


def verify_signature(
    source_root: Path,
    allowed_signers: Path,
    verification_command: str,
    object_to_verify: str,
) -> None:
    subprocess.run(
        [
            "git",
            "-C",
            str(source_root),
            "-c",
            f"gpg.ssh.allowedSignersFile={allowed_signers.resolve()}",
            verification_command,
            object_to_verify,
        ],
        check=True,
    )


def verify_source(
    source_root: Path,
    manifest: dict[str, object],
    allowed_signers: Path | None = None,
) -> None:
    source_commit = str(manifest["sourceCommit"])
    release_tag = str(manifest["releaseTag"])
    if git(source_root, "rev-parse", "HEAD") != source_commit:
        raise ValueError("Checked-out source does not match the release manifest commit.")
    tag_ref = f"refs/tags/{release_tag}"
    tag_type = git(source_root, "cat-file", "-t", tag_ref)
    if tag_type not in {"commit", "tag"}:
        raise ValueError("Release tag does not identify a commit or annotated tag.")
    if git(source_root, "rev-parse", f"{tag_ref}^{{commit}}") != source_commit:
        raise ValueError("Release tag does not peel to the release manifest commit.")
    if allowed_signers is not None:
        object_to_verify = release_tag if tag_type == "tag" else source_commit
        verification_command = "verify-tag" if tag_type == "tag" else "verify-commit"
        verify_signature(
            source_root,
            allowed_signers,
            verification_command,
            object_to_verify,
        )


def element(parent: ElementTree.Element, name: str) -> ElementTree.Element:
    match = parent.find(f"{{*}}{name}")
    if match is None:
        raise ValueError(f"Package metadata is missing {name}.")
    return match


def text(parent: ElementTree.Element, name: str) -> str:
    value = element(parent, name).text
    if value is None or not value.strip():
        raise ValueError(f"Package metadata has an empty {name}.")
    return value.strip()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def inspect_package(
    path: Path, manifest: dict[str, object]
) -> dict[str, object]:
    with zipfile.ZipFile(path) as archive:
        invalid_entry = archive.testzip()
        if invalid_entry is not None:
            raise ValueError(f"Package archive has an invalid entry: {invalid_entry}")
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError("Package archive must contain exactly one nuspec.")
        root = ElementTree.fromstring(archive.read(nuspecs[0]))

    metadata = element(root, "metadata")
    package_id = text(metadata, "id")
    version = text(metadata, "version")
    expected_version = str(manifest["releaseVersion"])
    expected_file_name = f"{package_id}.{expected_version}.nupkg"
    if version != expected_version:
        raise ValueError(f"{package_id} has unexpected version {version}.")
    if path.name != expected_file_name:
        raise ValueError(f"Unexpected package filename: {path.name}")

    repository = element(metadata, "repository")
    if repository.get("type") != "git":
        raise ValueError(f"{package_id} repository type is not git.")
    if repository.get("url") != manifest["repositoryUrl"]:
        raise ValueError(f"{package_id} repository URL does not match the manifest.")
    if repository.get("commit") != manifest["sourceCommit"]:
        raise ValueError(f"{package_id} repository commit does not match the manifest.")

    dependencies: list[dict[str, str]] = []
    dependencies_element = metadata.find("{*}dependencies")
    if dependencies_element is not None:
        for dependency in dependencies_element.findall(".//{*}dependency"):
            dependency_id = dependency.get("id", "")
            dependency_version = dependency.get("version", "")
            dependencies.append({"id": dependency_id, "version": dependency_version})
            if dependency_id.startswith("NetWasm.") and dependency_version != f"[{expected_version}]":
                raise ValueError(
                    f"{package_id} dependency {dependency_id} is not pinned to "
                    f"[{expected_version}]."
                )

    return {
        "id": package_id,
        "version": version,
        "fileName": path.name,
        "size": path.stat().st_size,
        "sha256": sha256(path),
        "repositoryCommit": repository.get("commit"),
        "dependencies": dependencies,
    }


def validate_packages(
    package_root: Path, manifest: dict[str, object]
) -> list[dict[str, object]]:
    expected_ids = set(manifest["packages"])
    package_paths = sorted(package_root.glob("*.nupkg"))
    packages = [inspect_package(path, manifest) for path in package_paths]
    actual_ids = [str(package["id"]) for package in packages]
    if len(actual_ids) != len(set(actual_ids)):
        raise ValueError("Package directory contains duplicate package IDs.")
    actual_id_set = set(actual_ids)
    if actual_id_set != expected_ids:
        missing = sorted(expected_ids - actual_id_set)
        unexpected = sorted(actual_id_set - expected_ids)
        raise ValueError(
            f"Package allowlist mismatch; missing={missing}, unexpected={unexpected}."
        )
    return sorted(packages, key=lambda package: str(package["id"]))


def require_absent_from_nuget(packages: list[dict[str, object]]) -> None:
    for package in packages:
        package_id = str(package["id"])
        version = str(package["version"])
        url = (
            "https://api.nuget.org/v3-flatcontainer/"
            f"{package_id.lower()}/index.json"
        )
        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                versions = json.load(response).get("versions", [])
        except urllib.error.HTTPError as error:
            if error.code == 404:
                versions = []
            else:
                raise
        if version in versions:
            raise ValueError(f"{package_id} {version} already exists on NuGet.org.")


def write_receipt(
    path: Path, manifest: dict[str, object], packages: list[dict[str, object]]
) -> None:
    receipt = {
        "schemaVersion": 1,
        "status": "PASS",
        "repository": manifest["repository"],
        "releaseVersion": manifest["releaseVersion"],
        "releaseTag": manifest["releaseTag"],
        "sourceCommit": manifest["sourceCommit"],
        "packageCount": len(packages),
        "packages": packages,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--source-root", type=Path)
    parser.add_argument("--allowed-signers", type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--require-absent-on-nuget", action="store_true")
    arguments = parser.parse_args()

    manifest = read_manifest(arguments.manifest)
    if arguments.allowed_signers is not None and arguments.source_root is None:
        parser.error("--allowed-signers requires --source-root")
    if arguments.source_root is not None:
        verify_source(arguments.source_root, manifest, arguments.allowed_signers)
    packages = validate_packages(arguments.packages, manifest)
    if arguments.require_absent_on_nuget:
        require_absent_from_nuget(packages)
    if arguments.receipt is not None:
        write_receipt(arguments.receipt, manifest, packages)
    print(
        f"Validated {len(packages)} packages for "
        f"{manifest['repository']} {manifest['releaseVersion']}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

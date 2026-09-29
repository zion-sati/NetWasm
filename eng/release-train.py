#!/usr/bin/env python3
"""Create and verify immutable preview/stable release-train bundles."""

from __future__ import annotations

import argparse
import hashlib
from io import BufferedReader
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import zipfile


SCHEMA_VERSION = 1
MAX_ENTRY_BYTES = 512 * 1024 * 1024
MAX_BUNDLE_CONTENT_BYTES = 2 * 1024 * 1024 * 1024
COMMIT = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")


def sha256_stream(stream: BufferedReader) -> str:
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
    return digest.hexdigest()


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return sha256_stream(stream)


def read_json(path: Path) -> dict[str, object]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON document is not an object: {path}")
    return value


def package_summary(
    manifest: dict[str, object], receipt: dict[str, object]
) -> list[dict[str, object]]:
    for field in ("repository", "releaseVersion", "releaseTag", "sourceCommit"):
        if receipt.get(field) != manifest.get(field):
            raise ValueError(f"Release receipt {field} does not match its manifest.")
    if receipt.get("schemaVersion") != 1 or receipt.get("status") != "PASS":
        raise ValueError("Release receipt is not a passing schema-version-1 receipt.")
    packages = receipt.get("packages")
    if not isinstance(packages, list) or not packages:
        raise ValueError("Release receipt has no packages.")
    expected_ids = manifest.get("packages")
    if not isinstance(expected_ids, list):
        raise ValueError("Release manifest has no package allowlist.")
    result: list[dict[str, object]] = []
    for package in packages:
        if not isinstance(package, dict):
            raise ValueError("Release receipt contains an invalid package entry.")
        summary = {
            name: package.get(name)
            for name in ("id", "version", "fileName", "size", "sha256")
        }
        if (
            not isinstance(summary["id"], str)
            or not isinstance(summary["version"], str)
            or not isinstance(summary["fileName"], str)
            or not isinstance(summary["size"], int)
            or isinstance(summary["size"], bool)
            or summary["size"] < 0
            or not isinstance(summary["sha256"], str)
            or re.fullmatch(r"[0-9a-f]{64}", summary["sha256"]) is None
        ):
            raise ValueError("Release receipt contains invalid package identity data.")
        result.append(summary)
    if [item["id"] for item in result] != sorted(expected_ids):
        raise ValueError("Release receipt packages do not match the sorted manifest allowlist.")
    return result


def canonical_entry(name: str) -> zipfile.ZipInfo:
    entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
    entry.create_system = 3
    entry.external_attr = (stat.S_IFREG | 0o644) << 16
    entry.compress_type = zipfile.ZIP_STORED
    return entry


def create_bundle(
    manifest_path: Path,
    receipt_path: Path,
    packages_root: Path,
    output: Path,
) -> dict[str, object]:
    if output.exists():
        raise ValueError(f"Release-train bundle already exists: {output}")
    manifest = read_json(manifest_path)
    receipt = read_json(receipt_path)
    packages = package_summary(manifest, receipt)
    expected_files = {str(item["fileName"]) for item in packages}
    actual_files = {path.name for path in packages_root.glob("*.nupkg")}
    if actual_files != expected_files:
        raise ValueError(
            f"Package files do not match the receipt; expected={sorted(expected_files)}, "
            f"actual={sorted(actual_files)}"
        )
    for item in packages:
        path = packages_root / str(item["fileName"])
        if path.stat().st_size != item["size"] or sha256(path) != item["sha256"]:
            raise ValueError(f"Package does not match its receipt: {path.name}")

    checksums = "".join(
        f"{item['sha256']}  packages/{item['fileName']}\n" for item in packages
    ).encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "x", allowZip64=True) as bundle:
        for name, path in (
            ("release-manifest.json", manifest_path),
            ("release-package-receipt.json", receipt_path),
        ):
            bundle.writestr(canonical_entry(name), path.read_bytes())
        bundle.writestr(canonical_entry("SHA256SUMS"), checksums)
        for item in packages:
            path = packages_root / str(item["fileName"])
            entry = canonical_entry(f"packages/{path.name}")
            with path.open("rb") as source, bundle.open(
                entry, "w", force_zip64=True
            ) as destination:
                shutil.copyfileobj(source, destination, length=1024 * 1024)
    return inspect_bundle(output)


def safe_bundle_names(bundle: zipfile.ZipFile) -> list[str]:
    names = bundle.namelist()
    if len(names) != len(set(names)):
        raise ValueError("Release-train bundle contains duplicate entries.")
    total = 0
    for entry in bundle.infolist():
        path = PurePosixPath(entry.filename)
        if (
            path.is_absolute()
            or not path.parts
            or any(part in {"", ".", ".."} for part in path.parts)
            or entry.is_dir()
        ):
            raise ValueError(f"Release-train bundle contains an unsafe entry: {entry.filename}")
        if entry.file_size > MAX_ENTRY_BYTES:
            raise ValueError(f"Release-train bundle entry is too large: {entry.filename}")
        total += entry.file_size
    if total > MAX_BUNDLE_CONTENT_BYTES:
        raise ValueError("Release-train bundle content exceeds the size limit.")
    return names


def inspect_bundle(path: Path) -> dict[str, object]:
    with zipfile.ZipFile(path) as bundle:
        names = safe_bundle_names(bundle)
        required = {"release-manifest.json", "release-package-receipt.json", "SHA256SUMS"}
        if not required <= set(names):
            raise ValueError("Release-train bundle is missing identity documents.")
        manifest = json.loads(bundle.read("release-manifest.json"))
        receipt = json.loads(bundle.read("release-package-receipt.json"))
        if not isinstance(manifest, dict) or not isinstance(receipt, dict):
            raise ValueError("Release-train bundle identity documents are invalid.")
        packages = package_summary(manifest, receipt)
        expected = required | {f"packages/{item['fileName']}" for item in packages}
        if set(names) != expected:
            raise ValueError("Release-train bundle entries do not match its receipt.")
        expected_sums = "".join(
            f"{item['sha256']}  packages/{item['fileName']}\n" for item in packages
        ).encode("utf-8")
        if bundle.read("SHA256SUMS") != expected_sums:
            raise ValueError("Release-train bundle checksum index does not match its receipt.")
        for item in packages:
            entry = bundle.getinfo(f"packages/{item['fileName']}")
            with bundle.open(entry) as stream:
                digest = sha256_stream(stream)
            if entry.file_size != item["size"] or digest != item["sha256"]:
                raise ValueError(
                    f"Release-train bundle package does not match its receipt: {item['fileName']}"
                )
        return {
            "version": manifest["releaseVersion"],
            "sourceCommit": manifest["sourceCommit"],
            "producingReleaseTag": manifest["releaseTag"],
            "bundleFile": path.name,
            "bundleSize": path.stat().st_size,
            "bundleSha256": sha256(path),
            "manifestSha256": hashlib.sha256(
                bundle.read("release-manifest.json")
            ).hexdigest(),
            "receiptSha256": hashlib.sha256(
                bundle.read("release-package-receipt.json")
            ).hexdigest(),
            "packages": packages,
        }


def extract_bundle(path: Path, destination: Path) -> dict[str, object]:
    summary = inspect_bundle(path)
    if destination.exists() and any(destination.iterdir()):
        raise ValueError(f"Release-train extraction destination is not empty: {destination}")
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path) as bundle:
        for name in ("release-manifest.json", "release-package-receipt.json"):
            (destination / name).write_bytes(bundle.read(name))
        packages = destination / "packages"
        packages.mkdir()
        for item in summary["packages"]:
            file_name = str(item["fileName"])
            with bundle.open(f"packages/{file_name}") as source, (
                packages / file_name
            ).open("xb") as target:
                shutil.copyfileobj(source, target, length=1024 * 1024)
    return summary


def create_train_manifest(
    *,
    repository: str,
    source_commit: str,
    producing_tag: str,
    preview_tag: str,
    stable_tag: str,
    run_id: str,
    artifact_name: str,
    preview_bundle: Path,
    stable_bundle: Path,
    toolchain: Path,
) -> dict[str, object]:
    if COMMIT.fullmatch(source_commit) is None:
        raise ValueError("Release-train source commit is invalid.")
    preview = inspect_bundle(preview_bundle)
    stable = inspect_bundle(stable_bundle)
    for channel in (preview, stable):
        if channel["sourceCommit"] != source_commit:
            raise ValueError("Release-train bundle source commit does not match the train.")
        if channel["producingReleaseTag"] != producing_tag:
            raise ValueError("Release-train bundle was not qualified against the producing tag.")
    preview["releaseTag"] = preview_tag
    stable["releaseTag"] = stable_tag
    toolchain_value = read_json(toolchain)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "sourceCommit": source_commit,
        "producingReleaseTag": producing_tag,
        "workflowRunId": str(run_id),
        "artifactName": artifact_name,
        "toolchainSha256": sha256(toolchain),
        "nativeToolchain": toolchain_value,
        "preview": preview,
        "stable": stable,
    }


def validate_train_structure(train: dict[str, object]) -> None:
    required = {
        "schemaVersion", "repository", "sourceCommit", "producingReleaseTag",
        "workflowRunId", "artifactName", "toolchainSha256",
        "nativeToolchain", "preview", "stable",
    }
    schema = train.get("schemaVersion")
    if schema == 2:
        required.update({
            "preparationSha256", "infrastructureCommit", "workflowRunAttempt",
        })
    elif schema != SCHEMA_VERSION:
        raise ValueError("Release-train manifest schema version is unsupported.")
    if set(train) != required:
        raise ValueError("Release-train manifest fields do not match its schema version.")
    if (
        not isinstance(train.get("repository"), str)
        or not train["repository"]
        or not isinstance(train.get("sourceCommit"), str)
        or COMMIT.fullmatch(str(train["sourceCommit"])) is None
        or not isinstance(train.get("producingReleaseTag"), str)
        or not train["producingReleaseTag"]
        or not isinstance(train.get("workflowRunId"), str)
        or not str(train["workflowRunId"]).isdigit()
        or int(str(train["workflowRunId"])) < 1
        or not isinstance(train.get("artifactName"), str)
        or not train["artifactName"]
        or not isinstance(train.get("toolchainSha256"), str)
        or SHA256.fullmatch(str(train["toolchainSha256"])) is None
        or not isinstance(train.get("nativeToolchain"), dict)
        or not isinstance(train.get("preview"), dict)
        or not isinstance(train.get("stable"), dict)
    ):
        raise ValueError("Release-train root identity is invalid.")
    if schema == 2 and (
        not isinstance(train.get("preparationSha256"), str)
        or SHA256.fullmatch(str(train["preparationSha256"])) is None
        or not isinstance(train.get("infrastructureCommit"), str)
        or COMMIT.fullmatch(str(train["infrastructureCommit"])) is None
        or not isinstance(train.get("workflowRunAttempt"), str)
        or not str(train["workflowRunAttempt"]).isdigit()
        or int(str(train["workflowRunAttempt"])) < 1
    ):
        raise ValueError("Release-train producer identity is invalid.")


def verify_train(
    train: dict[str, object],
    bundle: Path,
    channel: str,
    repository: str,
    source_commit: str,
    release_tag: str,
) -> dict[str, object]:
    validate_train_structure(train)
    if train.get("repository") != repository or train.get("sourceCommit") != source_commit:
        raise ValueError("Release train does not match the repository source release.")
    if channel not in {"preview", "stable"}:
        raise ValueError(f"Unsupported release-train channel: {channel}")
    expected = train[channel]
    if not isinstance(expected, dict) or expected.get("releaseTag") != release_tag:
        raise ValueError("Release train does not target this GitHub Release tag.")
    actual = inspect_bundle(bundle)
    for name in (
        "version", "sourceCommit", "producingReleaseTag", "bundleFile", "bundleSize",
        "bundleSha256", "manifestSha256", "receiptSha256", "packages",
    ):
        if actual.get(name) != expected.get(name):
            raise ValueError(f"Release-train {channel} bundle mismatch: {name}")
    return actual


def verify_train_identity(
    train: dict[str, object],
    repository: str,
    source_commit: str,
    producing_tag: str,
    preview_tag: str,
    stable_tag: str,
    expected_artifact_name: str,
) -> tuple[str, str]:
    validate_train_structure(train)
    expected = {
        "repository": repository,
        "sourceCommit": source_commit,
        "producingReleaseTag": producing_tag,
    }
    for name, value in expected.items():
        if train.get(name) != value:
            raise ValueError(f"Release-train identity mismatch: {name}")
    for channel, tag in (("preview", preview_tag), ("stable", stable_tag)):
        value = train.get(channel)
        if not isinstance(value, dict) or value.get("releaseTag") != tag:
            raise ValueError(f"Release-train identity mismatch: {channel} tag")
    run_id = train.get("workflowRunId")
    artifact_name = train.get("artifactName")
    if (
        not isinstance(run_id, str)
        or not run_id.isdigit()
        or not isinstance(artifact_name, str)
        or artifact_name != expected_artifact_name
    ):
        raise ValueError("Release-train producer coordinates are invalid.")
    return run_id, artifact_name



def effective_mode(resolved_mode: str, retained_manifest_count: int) -> str:
    if resolved_mode not in {"build", "promote"}:
        raise ValueError(f"Unsupported resolved release mode: {resolved_mode}")
    if retained_manifest_count not in {0, 1}:
        raise ValueError("Preview release has an ambiguous retained train manifest.")
    if resolved_mode == "promote":
        if retained_manifest_count != 1:
            raise ValueError("Stable promotion requires the retained preview train manifest.")
        return "promote"
    return "resume" if retained_manifest_count == 1 else "build"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    bundle = subparsers.add_parser("bundle")
    bundle.add_argument("--manifest", type=Path, required=True)
    bundle.add_argument("--receipt", type=Path, required=True)
    bundle.add_argument("--packages", type=Path, required=True)
    bundle.add_argument("--output", type=Path, required=True)
    create = subparsers.add_parser("create")
    create.add_argument("--repository", required=True)
    create.add_argument("--source-commit", required=True)
    create.add_argument("--producing-tag", required=True)
    create.add_argument("--preview-tag", required=True)
    create.add_argument("--stable-tag", required=True)
    create.add_argument("--run-id", required=True)
    create.add_argument("--artifact-name", required=True)
    create.add_argument("--preview-bundle", type=Path, required=True)
    create.add_argument("--stable-bundle", type=Path, required=True)
    create.add_argument("--toolchain", type=Path, required=True)
    create.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("--train", type=Path, required=True)
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--channel", choices=("preview", "stable"), required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument("--source-commit", required=True)
    verify.add_argument("--release-tag", required=True)
    identity = subparsers.add_parser("identity")
    identity.add_argument("--train", type=Path, required=True)
    identity.add_argument("--repository", required=True)
    identity.add_argument("--source-commit", required=True)
    identity.add_argument("--producing-tag", required=True)
    identity.add_argument("--preview-tag", required=True)
    identity.add_argument("--stable-tag", required=True)
    identity.add_argument("--artifact-name", required=True)
    identity.add_argument("--github-output", type=Path)
    state = subparsers.add_parser("state")
    state.add_argument("--resolved-mode", required=True)
    state.add_argument("--retained-manifest-count", required=True, type=int)
    state.add_argument("--github-output", required=True, type=Path)
    extract = subparsers.add_parser("extract")
    extract.add_argument("--bundle", type=Path, required=True)
    extract.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    if arguments.command == "bundle":
        summary = create_bundle(
            arguments.manifest, arguments.receipt, arguments.packages, arguments.output
        )
        print(f"Created {summary['bundleFile']} ({summary['bundleSize']:,} bytes).")
        return 0
    if arguments.command == "create":
        train = create_train_manifest(
            repository=arguments.repository,
            source_commit=arguments.source_commit,
            producing_tag=arguments.producing_tag,
            preview_tag=arguments.preview_tag,
            stable_tag=arguments.stable_tag,
            run_id=arguments.run_id,
            artifact_name=arguments.artifact_name,
            preview_bundle=arguments.preview_bundle,
            stable_bundle=arguments.stable_bundle,
            toolchain=arguments.toolchain,
        )
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(json.dumps(train, indent=2) + "\n", encoding="utf-8")
        return 0
    if arguments.command == "extract":
        summary = extract_bundle(arguments.bundle, arguments.output)
        print(f"Extracted release train {summary['version']}.")
        return 0
    if arguments.command == "state":
        mode = effective_mode(
            arguments.resolved_mode, arguments.retained_manifest_count
        )
        with arguments.github_output.open("a", encoding="utf-8") as output:
            output.write(f"mode={mode}\n")
        return 0
    train = read_json(arguments.train)
    if arguments.command == "identity":
        run_id, artifact_name = verify_train_identity(
            train,
            arguments.repository,
            arguments.source_commit,
            arguments.producing_tag,
            arguments.preview_tag,
            arguments.stable_tag,
            arguments.artifact_name,
        )
        if arguments.github_output is not None:
            with arguments.github_output.open("a", encoding="utf-8") as stream:
                stream.write(f"producing_run_id={run_id}\n")
                stream.write(f"artifact_name={artifact_name}\n")
        print(f"Verified release train from workflow run {run_id}.")
        return 0
    summary = verify_train(
        train, arguments.bundle, arguments.channel, arguments.repository,
        arguments.source_commit, arguments.release_tag,
    )
    print(f"Verified {arguments.channel} release train {summary['version']}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

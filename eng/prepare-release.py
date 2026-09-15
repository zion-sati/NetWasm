#!/usr/bin/env python3
"""Prepare signed release source and metadata locally; never push or publish."""
from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import tempfile
from pathlib import Path


def load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


projection = load("release_projection", "project-release-version.py")
verification = load("release_verification", "verify-release-packages.py")


def git(root: Path, *arguments: str) -> str:
    try:
        return subprocess.check_output(["git", "-C", str(root), *arguments],
                                       stderr=subprocess.PIPE, text=True).strip()
    except subprocess.CalledProcessError as error:
        raise ValueError(f"Git {arguments[0]} failed; check repository access and signing configuration.") from error


def prepare(root: Path, version: str) -> dict[str, str]:
    if not projection.VERSION_PATTERN.fullmatch(version):
        raise ValueError("Use a valid semantic release version.")
    root = root.resolve()
    if git(root, "branch", "--show-current") != "main" or git(root, "status", "--porcelain=v1"):
        raise ValueError("Release preparation requires a clean main checkout.")
    manifest_path = root / "eng/release-manifest.json"
    manifest = verification.read_manifest(manifest_path)
    previous = manifest["releaseVersion"]
    if (root / "eng/NetWasm.ReleaseVersion.txt").read_text().strip() != previous:
        raise ValueError("The source release version and manifest must agree before preparation.")
    if version == previous:
        raise ValueError("This release version is already prepared; use a new version.")
    if not manifest["releaseTag"].endswith(previous):
        raise ValueError("The existing manifest tag must end with its release version.")
    tag = manifest["releaseTag"][:-len(previous)] + version
    origin = git(root, "remote", "get-url", "origin")
    expected = str(manifest["repository"])
    origin_repository = origin.removesuffix(".git")
    if origin_repository not in (f"https://github.com/{expected}", f"git@github.com:{expected}"):
        raise ValueError("Origin does not match the release manifest repository.")
    principals = {principal for line in (root / "eng/release-signers").read_text().splitlines()
                  if line.strip() and not line.lstrip().startswith("#")
                  for principal in line.split()[0].split(",")}
    if git(root, "config", "user.name") != "Zion Sati" or git(root, "config", "user.email") not in principals:
        raise ValueError("Configure the public Git author as Zion Sati with an approved signer email.")
    if git(root, "tag", "--list", tag) or git(root, "ls-remote", "--tags", "origin", f"refs/tags/{tag}"):
        raise ValueError("The release tag already exists; immutable releases cannot be overwritten.")
    with tempfile.TemporaryDirectory(prefix="release-preparation-") as temporary:
        projection.project_version(root, version, Path(temporary) / "projection.json")
    git(root, "add", "--update")
    git(root, "commit", "-S", "-m", f"Prepare {expected.split('/')[-1]} {version}")
    source = git(root, "rev-parse", "HEAD")
    git(root, "tag", "-s", tag, "-m", f"{expected.split('/')[-1]} {version}", source)
    manifest.update(releaseVersion=version, releaseTag=tag, sourceCommit=source)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    verification.verify_source(root, manifest, root / "eng/release-signers")
    git(root, "add", "eng/release-manifest.json")
    git(root, "commit", "-S", "-m", f"Bind {version} release manifest to signed source")
    return {"releaseVersion": version, "releaseTag": tag, "sourceCommit": source,
            "manifestCommit": git(root, "rev-parse", "HEAD")}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try:
        receipt = prepare(Path(__file__).resolve().parent.parent, args.version)
    except (ValueError, OSError) as error:
        parser.exit(1, f"Release preparation stopped: {error}\nLocal changes are preserved; nothing was pushed or published.\n")
    print(json.dumps(receipt, indent=2))
    print("Review the commits, then push main and the signed tag before publishing the GitHub Release.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

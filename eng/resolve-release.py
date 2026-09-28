#!/usr/bin/env python3

"""Resolve an effective package manifest from a GitHub Release tag."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


SEMVER = re.compile(
    r"(?:0|[1-9][0-9]*)\."
    r"(?:0|[1-9][0-9]*)\."
    r"(?:0|[1-9][0-9]*)"
    r"(?:-(?:0|[1-9][0-9]*|[0-9]*[a-z-][0-9a-z-]*)"
    r"(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-z-][0-9a-z-]*))*)?"
)
TRAIN_PREVIEW_SUFFIX = "-preview.1"


def resolve_train_identity(version: str, tag_prefix: str) -> dict[str, str]:
    if version.endswith(TRAIN_PREVIEW_SUFFIX):
        stable_version = version.removesuffix(TRAIN_PREVIEW_SUFFIX)
        mode = "build"
    elif "-" not in version:
        stable_version = version
        mode = "promote"
    else:
        raise ValueError(
            "Release trains must start at preview.1 and finish at the stable version."
        )
    if SEMVER.fullmatch(stable_version) is None or "-" in stable_version:
        raise ValueError("Release train base version must be stable semantic version.")
    preview_version = stable_version + TRAIN_PREVIEW_SUFFIX
    return {
        "mode": mode,
        "previewVersion": preview_version,
        "previewTag": tag_prefix + preview_version,
        "stableVersion": stable_version,
        "stableTag": tag_prefix + stable_version,
        "artifactName": f"netwasm-release-train-{stable_version}",
    }


def write_train_manifests(
    directory: Path,
    resolved: dict[str, object],
    identity: dict[str, str],
) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    if identity["mode"] != "build":
        return
    preview = dict(resolved)
    stable = dict(resolved)
    stable["releaseVersion"] = identity["stableVersion"]
    # The future stable tag does not exist yet. Both candidates are qualified
    # against the signed preview tag that produced the immutable train.
    stable["releaseTag"] = identity["previewTag"]
    (directory / "preview-manifest.json").write_text(
        json.dumps(preview, indent=2) + "\n", encoding="utf-8"
    )
    (directory / "stable-manifest.json").write_text(
        json.dumps(stable, indent=2) + "\n", encoding="utf-8"
    )


def resolve_manifest(
    manifest: dict[str, object], tag: str, source_commit: str, tag_prefix: str
) -> dict[str, object]:
    required = {
        "schemaVersion",
        "repository",
        "repositoryUrl",
        "releaseVersion",
        "releaseTag",
        "sourceCommit",
        "packages",
    }
    if set(manifest) != required or manifest["schemaVersion"] != 1:
        raise ValueError("Release manifest does not match schema version 1.")
    if not tag_prefix or not tag.startswith(tag_prefix):
        raise ValueError(f"Release tag must start with {tag_prefix!r}.")
    version = tag[len(tag_prefix) :]
    if SEMVER.fullmatch(version) is None:
        raise ValueError("Release tag does not contain a supported semantic version.")
    if re.fullmatch(r"[0-9a-f]{40}", source_commit) is None:
        raise ValueError("Release source commit must be a full lowercase Git object ID.")
    packages = manifest["packages"]
    if not isinstance(packages, list) or not packages or any(
        not isinstance(package, str) or not package for package in packages
    ):
        raise ValueError("Release manifest packages must be a non-empty string list.")
    if len(packages) != len(set(packages)):
        raise ValueError("Release manifest contains duplicate package IDs.")

    resolved = dict(manifest)
    resolved.update(
        releaseVersion=version,
        releaseTag=tag,
        sourceCommit=source_commit,
    )
    return resolved


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--tag-prefix", default="v")
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--train-directory", type=Path)
    arguments = parser.parse_args()

    manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
    resolved = resolve_manifest(
        manifest, arguments.tag, arguments.source_commit, arguments.tag_prefix
    )
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(resolved, indent=2) + "\n", encoding="utf-8")
    identity = resolve_train_identity(str(resolved["releaseVersion"]), arguments.tag_prefix)
    if arguments.train_directory is not None:
        write_train_manifests(arguments.train_directory, resolved, identity)
    if arguments.github_output is not None:
        with arguments.github_output.open("a", encoding="utf-8") as stream:
            stream.write(f"tag={resolved['releaseTag']}\n")
            stream.write(f"version={resolved['releaseVersion']}\n")
            stream.write(f"source_commit={resolved['sourceCommit']}\n")
            for name, value in identity.items():
                output_name = re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()
                stream.write(f"{output_name}={value}\n")
    print(f"Resolved {resolved['releaseTag']} from GitHub Release metadata.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

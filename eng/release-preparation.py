#!/usr/bin/env python3
"""Plan coordinated release drafts and finalize their immutable preparation."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
from typing import Protocol
import urllib.parse
import urllib.error
import urllib.request


SCRIPT_ROOT = Path(__file__).resolve().parent
TRAIN_SCRIPT = SCRIPT_ROOT / "release-train.py"
TRAIN_SPEC = importlib.util.spec_from_file_location("release_train", TRAIN_SCRIPT)
assert TRAIN_SPEC is not None and TRAIN_SPEC.loader is not None
TRAIN = importlib.util.module_from_spec(TRAIN_SPEC)
TRAIN_SPEC.loader.exec_module(TRAIN)

COORDINATE_SCHEMA_VERSION = 1
PACKAGE_STAGE_NAMES = tuple(item[0] for item in TRAIN.PREPARATION_STAGES[:-1])
REPOSITORIES = tuple(dict.fromkeys(item[1] for item in TRAIN.PREPARATION_STAGES))
PREPARATION_ASSET = "release-preparation.json"


def origin(url: str) -> tuple[str, str, int | None]:
    parsed = urllib.parse.urlsplit(url)
    return parsed.scheme.lower(), (parsed.hostname or "").lower(), parsed.port


class CredentialSafeRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, new_url):
        old_origin = origin(request.full_url)
        new_origin = origin(new_url)
        if old_origin[0] == "https" and new_origin[0] != "https":
            raise ValueError("Refusing HTTPS redirect downgrade.")
        redirected = super().redirect_request(
            request, fp, code, msg, headers, new_url
        )
        if redirected is not None and old_origin != new_origin:
            redirected.headers.pop("Authorization", None)
            redirected.unredirected_hdrs.pop("Authorization", None)
        return redirected


class ReleaseStore(Protocol):
    def tag_commit(self, repository: str, tag: str) -> str | None: ...
    def verify_workflow_tree(self, repository: str, source_commit: str) -> None: ...
    def verify_workflow_ref(
        self, repository: str, workflow_ref: str, workflow_commit: str
    ) -> None: ...
    def releases(self, repository: str) -> list[dict[str, object]]: ...
    def release(self, repository: str, release_id: int) -> dict[str, object]: ...
    def create_draft(self, spec: dict[str, object]) -> dict[str, object]: ...
    def asset_bytes(
        self, repository: str, release_id: int, asset_name: str
    ) -> bytes | None: ...
    def upload_asset(
        self, repository: str, release_id: int, asset_name: str, contents: bytes
    ) -> None: ...


class GitHubReleaseStore:
    def __init__(self, token: str) -> None:
        if not token:
            raise ValueError("GitHub App installation token is required.")
        self.token = token
        self.open = urllib.request.build_opener(
            CredentialSafeRedirectHandler()
        ).open

    def request(
        self,
        method: str,
        url: str,
        *,
        payload: dict[str, object] | None = None,
        accept: str = "application/vnd.github+json",
        allow_not_found: bool = False,
    ) -> tuple[object, dict[str, str]]:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            url,
            data=data,
            method=method,
            headers={
                "Accept": accept,
                "Authorization": f"Bearer {self.token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "Content-Type": "application/json",
                "User-Agent": "NetWasm-release-preparation",
            },
        )
        try:
            with self.open(request, timeout=30) as response:
                contents = response.read()
                headers = {
                    name.lower(): value for name, value in response.headers.items()
                }
        except urllib.error.HTTPError as error:
            if allow_not_found and error.code == 404:
                error.close()
                return None, {}
            raise
        if accept == "application/octet-stream":
            return contents, headers
        return json.loads(contents), headers

    def tag_commit(self, repository: str, tag: str) -> str | None:
        encoded = urllib.parse.quote(f"tags/{tag}", safe="/")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/git/ref/{encoded}",
            allow_not_found=True,
        )
        if value is None:
            return None
        seen = set()
        for _ in range(8):
            if not isinstance(value, dict):
                raise ValueError("GitHub tag reference is invalid.")
            target = value.get("object")
            if not isinstance(target, dict):
                raise ValueError("GitHub tag target is invalid.")
            kind = target.get("type")
            digest = target.get("sha")
            if not isinstance(digest, str) or TRAIN.COMMIT.fullmatch(digest) is None:
                raise ValueError("GitHub tag target commit is invalid.")
            if kind == "commit":
                return digest
            if kind != "tag" or digest in seen:
                raise ValueError("GitHub annotated tag chain is invalid.")
            seen.add(digest)
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/git/tags/{digest}",
            )
        raise ValueError("GitHub annotated tag chain is too deep.")

    def workflow_rows(self, repository: str, commit: str) -> list[tuple[object, ...]]:
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/git/trees/{commit}?recursive=1",
        )
        if not isinstance(value, dict) or value.get("truncated") is not False:
            raise ValueError("GitHub workflow tree is unavailable or truncated.")
        tree = value.get("tree")
        if not isinstance(tree, list):
            raise ValueError("GitHub workflow tree is invalid.")
        return sorted(
            (item.get("path"), item.get("mode"), item.get("type"), item.get("sha"))
            for item in tree
            if isinstance(item, dict)
            and str(item.get("path", "")).startswith(".github/workflows/")
        )

    def verify_workflow_tree(self, repository: str, source_commit: str) -> None:
        metadata, _ = self.request(
            "GET", f"https://api.github.com/repos/{repository}"
        )
        if not isinstance(metadata, dict) or not isinstance(
            metadata.get("default_branch"), str
        ):
            raise ValueError("GitHub repository default branch is invalid.")
        branch = urllib.parse.quote(str(metadata["default_branch"]), safe="")
        head, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/commits/{branch}",
        )
        if not isinstance(head, dict) or not isinstance(head.get("sha"), str):
            raise ValueError("GitHub default branch commit is invalid.")
        if self.workflow_rows(repository, source_commit) != self.workflow_rows(
            repository, str(head["sha"])
        ):
            raise ValueError(
                "Prepared source changes GitHub workflows relative to the default branch."
            )

    def verify_workflow_ref(
        self, repository: str, workflow_ref: str, workflow_commit: str
    ) -> None:
        encoded = urllib.parse.quote(workflow_ref, safe="")
        value, _ = self.request(
            "GET", f"https://api.github.com/repos/{repository}/commits/{encoded}"
        )
        if not isinstance(value, dict) or value.get("sha") != workflow_commit:
            raise ValueError(
                f"Prepared workflow ref does not resolve to its approved commit: "
                f"{repository} {workflow_ref}."
            )

    def releases(self, repository: str) -> list[dict[str, object]]:
        result = []
        page = 1
        while True:
            url = (
                f"https://api.github.com/repos/{repository}/releases"
                f"?per_page=100&page={page}"
            )
            value, _ = self.request("GET", url)
            if not isinstance(value, list) or any(
                not isinstance(item, dict) for item in value
            ):
                raise ValueError("GitHub release listing is invalid.")
            result.extend(value)
            if len(value) < 100:
                return result
            page += 1

    def create_draft(self, spec: dict[str, object]) -> dict[str, object]:
        repository = str(spec["repository"])
        value, _ = self.request(
            "POST",
            f"https://api.github.com/repos/{repository}/releases",
            payload={
                "tag_name": spec["tag"],
                "target_commitish": spec["targetCommit"],
                "name": spec["title"],
                "body": (
                    "Prepared NetWasm release train stage. Publishing the Core "
                    "preview starts the approved coordinated train."
                ),
                "draft": True,
                "prerelease": spec["prerelease"],
            },
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub draft release response is invalid.")
        return value

    def release(self, repository: str, release_id: int) -> dict[str, object]:
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/releases/{release_id}",
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub release response is invalid.")
        return value

    def assets(self, repository: str, release_id: int) -> list[dict[str, object]]:
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/releases/"
                f"{release_id}/assets?per_page=100&page={page}",
            )
            if not isinstance(value, list) or any(
                not isinstance(item, dict) for item in value
            ):
                raise ValueError("GitHub release asset listing is invalid.")
            result.extend(value)
            if len(value) < 100:
                return result
            page += 1

    def asset_bytes(
        self, repository: str, release_id: int, asset_name: str
    ) -> bytes | None:
        matches = [
            asset for asset in self.assets(repository, release_id)
            if asset.get("name") == asset_name
        ]
        if not matches:
            return None
        if len(matches) != 1 or not isinstance(matches[0].get("id"), int):
            raise ValueError("GitHub release preparation asset is ambiguous.")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/releases/assets/"
            f"{matches[0]['id']}",
            accept="application/octet-stream",
        )
        if not isinstance(value, bytes):
            raise ValueError("GitHub release preparation asset is invalid.")
        return value

    def upload_asset(
        self, repository: str, release_id: int, asset_name: str, contents: bytes
    ) -> None:
        url = (
            f"https://uploads.github.com/repos/{repository}/releases/{release_id}/assets?"
            + urllib.parse.urlencode({"name": asset_name})
        )
        request = urllib.request.Request(
            url,
            data=contents,
            method="POST",
            headers={
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self.token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "Content-Type": "application/json",
                "User-Agent": "NetWasm-release-preparation",
            },
        )
        with self.open(request, timeout=60) as response:
            if response.status != 201:
                raise ValueError("GitHub did not accept the preparation asset.")


def validate_coordinates(value: dict[str, object]) -> dict[str, object]:
    if set(value) != {"schemaVersion", "version", "repositories"}:
        raise ValueError("Release coordinates fields are invalid.")
    if value.get("schemaVersion") != COORDINATE_SCHEMA_VERSION:
        raise ValueError("Release coordinates schema version is unsupported.")
    version = value.get("version")
    if not isinstance(version, str) or TRAIN.VERSION.fullmatch(version) is None:
        raise ValueError("Release coordinates version must be a stable semantic version.")
    repositories = value.get("repositories")
    if not isinstance(repositories, dict) or set(repositories) != set(REPOSITORIES):
        raise ValueError("Release coordinates repository set is incomplete or unexpected.")
    fields = {
        "sourceCommit", "infrastructureCommit", "workflowCommit", "workflowRef",
    }
    for repository in REPOSITORIES:
        coordinate = repositories.get(repository)
        if not isinstance(coordinate, dict) or set(coordinate) != fields:
            raise ValueError(f"Release coordinate fields are invalid: {repository}.")
        for field in ("sourceCommit", "infrastructureCommit", "workflowCommit"):
            commit = coordinate.get(field)
            if not isinstance(commit, str) or TRAIN.COMMIT.fullmatch(commit) is None:
                raise ValueError(
                    f"Release coordinate {field} is invalid: {repository}."
                )
        workflow_ref = coordinate.get("workflowRef")
        if not isinstance(workflow_ref, str) or not workflow_ref:
            raise ValueError(f"Release coordinate workflowRef is invalid: {repository}.")
        if (
            TRAIN.WORKFLOW_REF.fullmatch(workflow_ref) is None
            or ".." in workflow_ref
            or "//" in workflow_ref
            or "@{" in workflow_ref
            or workflow_ref.endswith((".", "/", ".lock"))
        ):
            raise ValueError(f"Release coordinate workflowRef is invalid: {repository}.")
    return value


def draft_plan(coordinates: dict[str, object]) -> dict[str, object]:
    validate_coordinates(coordinates)
    version = str(coordinates["version"])
    repositories = coordinates["repositories"]
    assert isinstance(repositories, dict)
    drafts = []
    for name, repository, _, _, _ in TRAIN.PREPARATION_STAGES[:-1]:
        coordinate = repositories[repository]
        assert isinstance(coordinate, dict)
        tag = TRAIN.expected_stage_ref(name, version)
        assert tag is not None
        drafts.append({
            "stage": name,
            "repository": repository,
            "tag": tag,
            "targetCommit": coordinate["sourceCommit"],
            "prerelease": name.endswith("-preview"),
            "title": f"{repository.rsplit('/', 1)[1]} {tag}",
        })
    return {"schemaVersion": 1, "version": version, "drafts": drafts}


def create_preparation(
    coordinates: dict[str, object], release_ids: dict[str, int]
) -> dict[str, object]:
    validate_coordinates(coordinates)
    if set(release_ids) != set(PACKAGE_STAGE_NAMES):
        raise ValueError("Release ID set is incomplete or unexpected.")
    if any(
        not isinstance(value, int) or isinstance(value, bool) or value < 1
        for value in release_ids.values()
    ):
        raise ValueError("Release IDs must be positive integers.")
    if len(set(release_ids.values())) != len(release_ids):
        raise ValueError("Release IDs must be unique.")
    version = str(coordinates["version"])
    repositories = coordinates["repositories"]
    assert isinstance(repositories, dict)
    stages = []
    for name, repository, workflow, upstream, _ in TRAIN.PREPARATION_STAGES:
        coordinate = repositories[repository]
        assert isinstance(coordinate, dict)
        website = name == "website"
        stages.append({
            "name": name,
            "repository": repository,
            "workflow": workflow,
            "sourceCommit": coordinate["sourceCommit"],
            "infrastructureCommit": coordinate["infrastructureCommit"],
            "workflowCommit": coordinate["workflowCommit"],
            "workflowRef": coordinate["workflowRef"],
            "ref": coordinate["sourceCommit"] if website else (
                TRAIN.expected_stage_ref(name, version)
            ),
            "releaseId": None if website else release_ids[name],
            "prerelease": name.endswith("-preview"),
            "upstreamStages": list(upstream),
        })
    result = {
        "schemaVersion": TRAIN.PREPARATION_SCHEMA_VERSION,
        "version": version,
        "stages": stages,
        "policy": {
            "publicationReceiptSchemaVersion": TRAIN.PUBLICATION_RECEIPT_SCHEMA_VERSION,
            "completionStage": "website",
        },
    }
    return TRAIN.validate_preparation(result)


def parse_release_ids(values: list[str]) -> dict[str, int]:
    result = {}
    for value in values:
        match = re.fullmatch(r"([a-z-]+)=([1-9][0-9]*)", value)
        if match is None or match.group(1) in result:
            raise ValueError(f"Release ID assignment is invalid: {value}.")
        result[match.group(1)] = int(match.group(2))
    return result


def verify_draft(
    release: dict[str, object], spec: dict[str, object]
) -> int:
    release_id = release.get("id")
    expected = {
        "tag_name": spec["tag"],
        "target_commitish": spec["targetCommit"],
        "name": spec["title"],
        "draft": True,
        "prerelease": spec["prerelease"],
    }
    if (
        not isinstance(release_id, int)
        or isinstance(release_id, bool)
        or release_id < 1
        or any(release.get(field) != value for field, value in expected.items())
    ):
        raise ValueError(
            f"Existing release conflicts with prepared stage {spec['stage']}."
        )
    return release_id


def apply_preparation(
    coordinates: dict[str, object], store: ReleaseStore
) -> tuple[dict[str, object], bytes]:
    plan = draft_plan(coordinates)
    drafts = plan["drafts"]
    assert isinstance(drafts, list)
    releases_by_repository = {
        repository: store.releases(repository) for repository in REPOSITORIES[:-1]
    }
    verified_sources = set()
    repositories = coordinates["repositories"]
    assert isinstance(repositories, dict)
    for repository in REPOSITORIES:
        coordinate = repositories[repository]
        assert isinstance(coordinate, dict)
        store.verify_workflow_ref(
            repository,
            str(coordinate["workflowRef"]),
            str(coordinate["workflowCommit"]),
        )
    release_ids = {}
    for spec in drafts:
        assert isinstance(spec, dict)
        repository = str(spec["repository"])
        source_commit = str(spec["targetCommit"])
        source_identity = (repository, source_commit)
        if source_identity not in verified_sources:
            store.verify_workflow_tree(repository, source_commit)
            verified_sources.add(source_identity)
        tag_commit = store.tag_commit(repository, str(spec["tag"]))
        if tag_commit is not None and tag_commit != source_commit:
            raise ValueError(
                f"Existing tag points to an unapproved commit: "
                f"{repository} {spec['tag']}."
            )
        matches = [
            release for release in releases_by_repository[repository]
            if release.get("tag_name") == spec["tag"]
        ]
        if len(matches) > 1:
            raise ValueError(f"Release tag is ambiguous: {repository} {spec['tag']}.")
        release = matches[0] if matches else store.create_draft(spec)
        release_id = verify_draft(release, spec)
        release_ids[str(spec["stage"])] = release_id
        if not matches:
            releases_by_repository[repository].append(release)

    preparation = create_preparation(coordinates, release_ids)
    contents = (json.dumps(preparation, indent=2) + "\n").encode("utf-8")
    for spec in drafts:
        assert isinstance(spec, dict)
        repository = str(spec["repository"])
        release_id = release_ids[str(spec["stage"])]
        verify_draft(store.release(repository, release_id), spec)
        current_tag = store.tag_commit(repository, str(spec["tag"]))
        if current_tag is not None and current_tag != spec["targetCommit"]:
            raise ValueError(
                f"Existing tag changed to an unapproved commit: "
                f"{repository} {spec['tag']}."
            )
        existing = store.asset_bytes(repository, release_id, PREPARATION_ASSET)
        if existing is None:
            store.upload_asset(repository, release_id, PREPARATION_ASSET, contents)
        elif existing != contents:
            raise ValueError(
                f"Prepared release has a conflicting {PREPARATION_ASSET}: "
                f"{repository} {spec['tag']}."
            )
    return preparation, contents


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    plan = subparsers.add_parser("draft-plan")
    plan.add_argument("--coordinates", type=Path, required=True)
    plan.add_argument("--output", type=Path, required=True)
    finalize = subparsers.add_parser("finalize")
    finalize.add_argument("--coordinates", type=Path, required=True)
    finalize.add_argument("--release-id", action="append", default=[])
    finalize.add_argument("--output", type=Path, required=True)
    apply = subparsers.add_parser("apply")
    apply.add_argument("--coordinates", type=Path, required=True)
    apply.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    apply.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    coordinates = TRAIN.read_json(arguments.coordinates)
    if arguments.command == "draft-plan":
        result = draft_plan(coordinates)
    elif arguments.command == "finalize":
        result = create_preparation(
            coordinates, parse_release_ids(arguments.release_id)
        )
    else:
        token = os.environ.get(arguments.token_environment, "").strip()
        result, contents = apply_preparation(
            coordinates, GitHubReleaseStore(token)
        )
        digest = hashlib.sha256(contents).hexdigest()
        print(f"Prepared coordinated release {result['version']} ({digest}).")
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

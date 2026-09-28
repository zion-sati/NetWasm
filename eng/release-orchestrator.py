#!/usr/bin/env python3
"""Run one resumable stage of the coordinated NetWasm release train."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import time
from typing import Callable
import urllib.parse
import zipfile


SCRIPT_ROOT = Path(__file__).resolve().parent


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TRAIN = load("release_train", SCRIPT_ROOT / "release-train.py")
COORDINATOR = load("release_coordinator", SCRIPT_ROOT / "release-coordinator.py")
PREPARATION = load("release_preparation", SCRIPT_ROOT / "release-preparation.py")

CORE_REPOSITORY = "zion-sati/NetWasm"
CORE_PREVIEW_STAGE = "core-preview"
RECEIPT_FILE = "publication-receipt.json"
CANDIDATE_RECEIPT_FILE = "delivery-candidate-receipt.json"
COMPLETION_RECEIPT_FILE = "delivery-completion-receipt.json"
POLL_SECONDS = 15
STAGE_TIMEOUT_SECONDS = 50 * 60


def receipt_asset_name(stage_name: str) -> str:
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES}:
        raise ValueError("Release receipt stage is invalid.")
    prefix = (
        "publication-receipt"
        if stage_name in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}
        else "delivery-completion"
    )
    return f"{prefix}-{stage_name}.json"


def completion_receipt_artifact_name(
    stage_name: str, run_id: int, run_attempt: int
) -> str:
    if stage_name not in {"playground", "website"}:
        raise ValueError("Delivery completion stage is invalid.")
    TRAIN.require_positive_integer(run_id, "Delivery completion run ID")
    TRAIN.require_positive_integer(run_attempt, "Delivery completion run attempt")
    return f"delivery-completion-{stage_name}-{run_id}-{run_attempt}"


def candidate_receipt_artifact_name(
    kind: str, run_id: int, run_attempt: int
) -> str:
    if kind not in {
        "playground-toolchain-candidate",
        "playground-site-candidate",
        "website-site-candidate",
    }:
        raise ValueError("Delivery candidate kind is invalid.")
    TRAIN.require_positive_integer(run_id, "Delivery candidate run ID")
    TRAIN.require_positive_integer(
        run_attempt, "Delivery candidate run attempt"
    )
    return f"delivery-candidate-receipt-{kind}-{run_id}-{run_attempt}"


def candidate_receipt_asset_name(kind: str) -> str:
    if kind not in {
        "playground-toolchain-candidate",
        "playground-site-candidate",
        "website-site-candidate",
    }:
        raise ValueError("Delivery candidate kind is invalid.")
    return f"delivery-candidate-{kind}.json"


class CandidateArtifactCollector:
    """Collect one original candidate receipt and its immutable payload."""

    def __init__(self, store: GitHubStore) -> None:
        self.store = store

    def _artifact(
        self,
        repository: str,
        run_id: int,
        *,
        name: str,
        artifact_id: int | None = None,
    ) -> dict[str, object]:
        matches = [
            item
            for item in self.store.run_artifacts(repository, str(run_id))
            if item.get("name") == name
            and (artifact_id is None or item.get("id") == artifact_id)
        ]
        if (
            len(matches) != 1
            or matches[0].get("expired") is not False
            or not isinstance(matches[0].get("id"), int)
            or isinstance(matches[0].get("id"), bool)
            or int(matches[0]["id"]) < 1
        ):
            raise ValueError(f"Delivery candidate artifact is missing: {name}.")
        return matches[0]

    def _member(
        self,
        repository: str,
        artifact: dict[str, object],
        expected_name: str,
        maximum_bytes: int,
    ) -> bytes:
        TRAIN.require_safe_leaf(expected_name, "Delivery candidate member name")
        archive = self.store.artifact_bytes_bounded(
            repository,
            int(artifact["id"]),
            min(TRAIN.MAX_ENTRY_BYTES, maximum_bytes + 16 * 1024 * 1024),
        )
        try:
            bundle = zipfile.ZipFile(io.BytesIO(archive))
        except zipfile.BadZipFile as error:
            raise ValueError("Delivery candidate artifact is not a ZIP archive.") from error
        with bundle:
            files = [item for item in bundle.infolist() if not item.is_dir()]
            if len(files) != 1:
                raise ValueError("Delivery candidate artifact inventory is invalid.")
            item = files[0]
            path = PurePosixPath(item.filename)
            if (
                path.is_absolute()
                or any(part in {"", ".", ".."} for part in path.parts)
                or len(path.parts) != 1
                or path.name != expected_name
                or item.flag_bits & 0x1
                or item.file_size < 1
                or item.file_size > maximum_bytes
            ):
                raise ValueError("Delivery candidate artifact member is invalid.")
            contents = bundle.read(item)
            if len(contents) != item.file_size:
                raise ValueError("Delivery candidate artifact member is truncated.")
            return contents

    def collect(
        self,
        preparation: dict[str, object],
        preparation_digest: str,
        kind: str,
        run_id: int,
        run_attempt: int,
        approved_actor_id: str,
        dispatch_attempt_identity: str,
    ) -> dict[str, object]:
        stage_name = TRAIN.DELIVERY_KINDS.get(kind)
        if stage_name is None or not kind.endswith("-candidate"):
            raise ValueError("Delivery candidate kind is invalid.")
        stage = TRAIN.preparation_stage(preparation, stage_name)
        repository = str(stage["repository"])
        if (
            not approved_actor_id.isdigit()
            or int(approved_actor_id) < 1
            or TRAIN.SHA256.fullmatch(dispatch_attempt_identity) is None
        ):
            raise ValueError("Delivery candidate authentication inputs are invalid.")
        run = self.store.workflow_run_attempt(repository, run_id, run_attempt)
        validate_run(
            run,
            stage,
            str(run_id),
            str(run_attempt),
            approved_actor_id,
            require_complete=False,
        )
        if (
            run.get("status") != "completed"
            or not isinstance(run.get("conclusion"), str)
            or not run["conclusion"]
        ):
            raise ValueError("Delivery candidate workflow run is not terminal.")
        receipt_artifact = self._artifact(
            repository,
            run_id,
            name=candidate_receipt_artifact_name(kind, run_id, run_attempt),
        )
        receipt_bytes = self._member(
            repository,
            receipt_artifact,
            CANDIDATE_RECEIPT_FILE,
            4 * 1024 * 1024,
        )
        try:
            receipt = json.loads(receipt_bytes)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError("Delivery candidate receipt is invalid JSON.") from error
        if not isinstance(receipt, dict):
            raise ValueError("Delivery candidate receipt is not an object.")
        TRAIN.validate_candidate_receipt(
            receipt, preparation, preparation_digest, kind
        )
        producer = receipt["producer"]
        assert isinstance(producer, dict)
        if (
            producer.get("runId") != run_id
            or producer.get("runAttempt") != run_attempt
            or producer.get("actorId") != int(approved_actor_id)
            or producer.get("dispatchAttemptIdentity")
            != dispatch_attempt_identity
        ):
            raise ValueError("Delivery candidate producer does not match its receipt artifact.")
        jobs = self.store.workflow_jobs(repository, run_id, run_attempt)
        matching_jobs = [item for item in jobs if item.get("id") == producer.get("jobId")]
        if len(matching_jobs) != 1:
            raise ValueError("Delivery candidate producer job is missing.")
        job = matching_jobs[0]
        if (
            job.get("run_id") != run_id
            or job.get("run_attempt") != run_attempt
            or job.get("status") != "completed"
            or job.get("conclusion") != "success"
            or job.get("head_sha") != stage.get("workflowCommit")
        ):
            raise ValueError("Delivery candidate producer job did not succeed.")
        payload = receipt["artifact"]
        archive = receipt["archive"]
        assert isinstance(payload, dict) and isinstance(archive, dict)
        payload_artifact = self._artifact(
            repository,
            run_id,
            name=str(payload["artifactName"]),
            artifact_id=int(payload["artifactId"]),
        )
        archive_bytes = self._member(
            repository,
            payload_artifact,
            str(archive["fileName"]),
            TRAIN.MAX_ENTRY_BYTES,
        )
        if (
            len(archive_bytes) != archive.get("bytes")
            or hashlib.sha256(archive_bytes).hexdigest() != archive.get("sha256")
        ):
            raise ValueError("Delivery candidate payload does not match its receipt.")
        return {
            "receipt": receipt,
            "receiptBytes": receipt_bytes,
            "receiptArtifactId": receipt_artifact["id"],
            "archiveBytes": archive_bytes,
            "payloadArtifactId": payload_artifact["id"],
        }


class GitHubStore(PREPARATION.GitHubReleaseStore):
    def publish_release(
        self, repository: str, release_id: int
    ) -> dict[str, object]:
        value, _ = self.request(
            "PATCH",
            f"https://api.github.com/repos/{repository}/releases/{release_id}",
            payload={"draft": False},
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub release publication response is invalid.")
        return value

    def workflow_run(
        self, repository: str, run_id: str
    ) -> dict[str, object]:
        if not run_id.isdigit() or int(run_id) < 1:
            raise ValueError("Workflow run ID is invalid.")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/actions/runs/{run_id}",
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub workflow run response is invalid.")
        return value

    def workflow_runs(
        self, repository: str, workflow: str, ref: str
    ) -> list[dict[str, object]]:
        workflow_name = Path(workflow).name
        if workflow != f".github/workflows/{workflow_name}":
            raise ValueError("Workflow path is invalid.")
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/actions/workflows/"
                f"{urllib.parse.quote(workflow_name, safe='')}/runs?"
                f"event=workflow_dispatch&branch={urllib.parse.quote(ref, safe='')}"
                f"&per_page=100&page={page}",
            )
            if not isinstance(value, dict) or not isinstance(
                value.get("workflow_runs"), list
            ):
                raise ValueError("GitHub workflow run listing is invalid.")
            runs = value["workflow_runs"]
            if any(not isinstance(item, dict) for item in runs):
                raise ValueError("GitHub workflow run listing is invalid.")
            result.extend(runs)
            if len(runs) < 100:
                return result
            page += 1

    def workflow_run_attempt(
        self, repository: str, run_id: int, run_attempt: int
    ) -> dict[str, object]:
        TRAIN.require_positive_integer(run_id, "Workflow run ID")
        TRAIN.require_positive_integer(run_attempt, "Workflow run attempt")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/actions/runs/"
            f"{run_id}/attempts/{run_attempt}",
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub workflow run-attempt response is invalid.")
        return value

    def workflow_jobs(
        self, repository: str, run_id: int, run_attempt: int
    ) -> list[dict[str, object]]:
        TRAIN.require_positive_integer(run_id, "Workflow run ID")
        TRAIN.require_positive_integer(run_attempt, "Workflow run attempt")
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/actions/runs/"
                f"{run_id}/attempts/{run_attempt}/jobs?filter=all&per_page=100&page={page}",
            )
            if not isinstance(value, dict) or not isinstance(value.get("jobs"), list):
                raise ValueError("GitHub workflow job listing is invalid.")
            jobs = value["jobs"]
            if any(not isinstance(item, dict) for item in jobs):
                raise ValueError("GitHub workflow job listing is invalid.")
            result.extend(jobs)
            if len(jobs) < 100:
                return result
            page += 1

    def run_artifacts(
        self, repository: str, run_id: str
    ) -> list[dict[str, object]]:
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/actions/runs/"
                f"{run_id}/artifacts?per_page=100&page={page}",
            )
            if not isinstance(value, dict) or not isinstance(
                value.get("artifacts"), list
            ):
                raise ValueError("GitHub workflow artifact listing is invalid.")
            artifacts = value["artifacts"]
            if any(not isinstance(item, dict) for item in artifacts):
                raise ValueError("GitHub workflow artifact listing is invalid.")
            result.extend(artifacts)
            if len(artifacts) < 100:
                return result
            page += 1

    def deployment(
        self, repository: str, deployment_id: int
    ) -> dict[str, object]:
        TRAIN.require_positive_integer(deployment_id, "GitHub deployment ID")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/deployments/{deployment_id}",
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub deployment response is invalid.")
        return value

    def deployment_statuses(
        self, repository: str, deployment_id: int
    ) -> list[dict[str, object]]:
        TRAIN.require_positive_integer(deployment_id, "GitHub deployment ID")
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/deployments/"
                f"{deployment_id}/statuses?per_page=100&page={page}",
            )
            if not isinstance(value, list) or any(
                not isinstance(item, dict) for item in value
            ):
                raise ValueError("GitHub deployment status response is invalid.")
            result.extend(value)
            if len(value) < 100:
                return result
            page += 1

    def artifact_bytes(self, repository: str, artifact_id: int) -> bytes:
        if artifact_id < 1:
            raise ValueError("Workflow artifact ID is invalid.")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/actions/artifacts/"
            f"{artifact_id}/zip",
            accept="application/octet-stream",
        )
        if not isinstance(value, bytes):
            raise ValueError("GitHub workflow artifact response is invalid.")
        return value

    def artifact_bytes_bounded(
        self, repository: str, artifact_id: int, maximum_bytes: int
    ) -> bytes:
        TRAIN.require_positive_integer(artifact_id, "Workflow artifact ID")
        return self._download_bounded(
            f"https://api.github.com/repos/{repository}/actions/artifacts/"
            f"{artifact_id}/zip",
            maximum_bytes,
        )

    def release_asset_bytes_bounded(
        self,
        repository: str,
        release_id: int,
        asset_name: str,
        maximum_bytes: int,
    ) -> bytes | None:
        TRAIN.require_positive_integer(release_id, "GitHub release ID")
        TRAIN.require_safe_leaf(asset_name, "GitHub release asset name")
        matches = [
            item for item in self.assets(repository, release_id)
            if item.get("name") == asset_name
        ]
        if not matches:
            return None
        if (
            len(matches) != 1
            or not isinstance(matches[0].get("id"), int)
            or isinstance(matches[0].get("id"), bool)
            or int(matches[0]["id"]) < 1
        ):
            raise ValueError("GitHub release asset is ambiguous.")
        return self._download_bounded(
            f"https://api.github.com/repos/{repository}/releases/assets/"
            f"{matches[0]['id']}",
            maximum_bytes,
        )

    def _download_bounded(self, url: str, maximum_bytes: int) -> bytes:
        if maximum_bytes < 1 or maximum_bytes > TRAIN.MAX_BUNDLE_CONTENT_BYTES:
            raise ValueError("Download byte limit is invalid.")
        request = urllib.request.Request(
            url,
            method="GET",
            headers={
                "Accept": "application/octet-stream",
                "Authorization": f"Bearer {self.token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "NetWasm-release-orchestrator",
            },
        )
        with self.open(request, timeout=60) as response:
            length = response.headers.get("Content-Length")
            if length is not None:
                try:
                    declared = int(length)
                except ValueError as error:
                    raise ValueError("Download Content-Length is invalid.") from error
                if declared < 0 or declared > maximum_bytes:
                    raise ValueError("Download exceeds its byte limit.")
            chunks = []
            remaining = maximum_bytes + 1
            while remaining > 0:
                block = response.read(min(1024 * 1024, remaining))
                if not block:
                    break
                chunks.append(block)
                remaining -= len(block)
        contents = b"".join(chunks)
        if len(contents) > maximum_bytes:
            raise ValueError("Download exceeds its byte limit.")
        return contents


def release_matches_stage(
    release: dict[str, object], stage: dict[str, object]
) -> bool:
    return (
        release.get("id") == stage.get("releaseId")
        and release.get("tag_name") == stage.get("ref")
        and release.get("prerelease") is stage.get("prerelease")
    )


def validate_published_release(
    store: GitHubStore,
    release: dict[str, object],
    stage: dict[str, object],
) -> None:
    if not release_matches_stage(release, stage) or release.get("draft") is not False:
        raise ValueError(f"Prepared release is not published: {stage['name']}.")
    if store.tag_commit(str(stage["repository"]), str(stage["ref"])) != stage.get(
        "sourceCommit"
    ):
        raise ValueError(f"Published release tag changed: {stage['name']}.")


def ensure_asset(
    store: GitHubStore,
    repository: str,
    release_id: int,
    name: str,
    contents: bytes,
) -> None:
    existing = store.asset_bytes(repository, release_id, name)
    if existing is None:
        store.upload_asset(repository, release_id, name, contents)
    elif existing != contents:
        raise ValueError(f"Release asset conflicts with approved bytes: {name}.")


def validate_candidate_dependencies(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_digest: str,
    *,
    upstream_receipts: dict[str, bytes],
    toolchain_receipt_bytes: bytes | None = None,
) -> None:
    coordinates = receipt.get("upstreamReceipts")
    if not isinstance(coordinates, list):
        raise ValueError("Delivery candidate upstream receipts are invalid.")
    expected = {
        str(item["stage"]): str(item["sha256"])
        for item in coordinates
        if isinstance(item, dict)
    }
    actual = {
        name: hashlib.sha256(contents).hexdigest()
        for name, contents in upstream_receipts.items()
    }
    if expected != actual:
        raise ValueError("Delivery candidate upstream receipt bytes do not match.")
    if receipt.get("kind") == "playground-site-candidate":
        if toolchain_receipt_bytes is None:
            raise ValueError("Playground site does not match its toolchain candidate.")
        try:
            toolchain_receipt = json.loads(toolchain_receipt_bytes)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError("Playground toolchain candidate is invalid JSON.") from error
        if not isinstance(toolchain_receipt, dict):
            raise ValueError("Playground toolchain candidate is not an object.")
        TRAIN.validate_candidate_receipt(
            toolchain_receipt,
            preparation,
            preparation_digest,
            "playground-toolchain-candidate",
        )
        if (
            hashlib.sha256(toolchain_receipt_bytes).hexdigest()
            != receipt.get("toolchainCandidateSha256")
            or toolchain_receipt.get("toolchain") != receipt.get("toolchain")
            or toolchain_receipt.get("upstreamReceipts")
            != receipt.get("upstreamReceipts")
        ):
            raise ValueError("Playground site does not match its toolchain candidate.")
    elif toolchain_receipt_bytes is not None:
        raise ValueError("Unexpected toolchain candidate dependency.")


def ensure_delivery_asset(
    store: GitHubStore,
    repository: str,
    release_id: int,
    name: str,
    contents: bytes,
) -> None:
    if not contents:
        raise ValueError(f"Delivery asset is empty: {name}.")
    existing = store.release_asset_bytes_bounded(
        repository, release_id, name, len(contents)
    )
    if existing is None:
        store.upload_asset(repository, release_id, name, contents)
        existing = store.release_asset_bytes_bounded(
            repository, release_id, name, len(contents)
        )
    if existing != contents:
        raise ValueError(f"Delivery asset conflicts with approved bytes: {name}.")


def attach_candidate(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    kind: str,
    collected: dict[str, object],
    *,
    upstream_receipts: dict[str, bytes],
    toolchain_receipt_bytes: bytes | None = None,
) -> dict[str, object]:
    receipt = collected.get("receipt")
    receipt_bytes = collected.get("receiptBytes")
    archive_bytes = collected.get("archiveBytes")
    if (
        not isinstance(receipt, dict)
        or not isinstance(receipt_bytes, bytes)
        or not isinstance(archive_bytes, bytes)
    ):
        raise ValueError("Collected delivery candidate is incomplete.")
    try:
        parsed_receipt = json.loads(receipt_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("Collected delivery candidate receipt is invalid JSON.") from error
    if not isinstance(parsed_receipt, dict) or parsed_receipt != receipt:
        raise ValueError("Collected delivery candidate receipt bytes do not match.")
    TRAIN.validate_candidate_receipt(
        parsed_receipt, preparation, preparation_digest, kind
    )
    validate_candidate_dependencies(
        receipt,
        preparation,
        preparation_digest,
        upstream_receipts=upstream_receipts,
        toolchain_receipt_bytes=toolchain_receipt_bytes,
    )
    archive = receipt["archive"]
    assert isinstance(archive, dict)
    if (
        len(archive_bytes) != archive.get("bytes")
        or hashlib.sha256(archive_bytes).hexdigest() != archive.get("sha256")
    ):
        raise ValueError("Collected delivery archive does not match its receipt.")
    stage_name = TRAIN.DELIVERY_KINDS[kind]
    repository, release_id = TRAIN.stage_state_anchor(preparation, stage_name)
    ensure_delivery_asset(
        store, repository, release_id, str(archive["fileName"]), archive_bytes
    )
    ensure_delivery_asset(
        store,
        repository,
        release_id,
        candidate_receipt_asset_name(kind),
        receipt_bytes,
    )
    return receipt


def load_attached_candidate(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    kind: str,
    *,
    upstream_receipts: dict[str, bytes],
    toolchain_receipt_bytes: bytes | None = None,
) -> dict[str, object] | None:
    stage_name = TRAIN.DELIVERY_KINDS.get(kind)
    if stage_name is None or not kind.endswith("-candidate"):
        raise ValueError("Delivery candidate kind is invalid.")
    repository, release_id = TRAIN.stage_state_anchor(preparation, stage_name)
    receipt_name = candidate_receipt_asset_name(kind)
    receipt_bytes = store.release_asset_bytes_bounded(
        repository, release_id, receipt_name, 4 * 1024 * 1024
    )
    if receipt_bytes is None:
        return None
    try:
        receipt = json.loads(receipt_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("Attached delivery candidate receipt is invalid JSON.") from error
    if not isinstance(receipt, dict):
        raise ValueError("Attached delivery candidate receipt is not an object.")
    TRAIN.validate_candidate_receipt(
        receipt, preparation, preparation_digest, kind
    )
    validate_candidate_dependencies(
        receipt,
        preparation,
        preparation_digest,
        upstream_receipts=upstream_receipts,
        toolchain_receipt_bytes=toolchain_receipt_bytes,
    )
    archive = receipt["archive"]
    assert isinstance(archive, dict)
    archive_bytes = store.release_asset_bytes_bounded(
        repository,
        release_id,
        str(archive["fileName"]),
        int(archive["bytes"]),
    )
    if (
        archive_bytes is None
        or len(archive_bytes) != archive["bytes"]
        or hashlib.sha256(archive_bytes).hexdigest() != archive["sha256"]
    ):
        raise ValueError("Attached delivery archive does not match its receipt.")
    return {
        "receipt": receipt,
        "receiptBytes": receipt_bytes,
        "archiveBytes": archive_bytes,
    }


def validate_run(
    run: dict[str, object],
    stage: dict[str, object],
    run_id: str,
    run_attempt: str,
    approved_actor_id: str,
    *,
    require_complete: bool,
) -> None:
    repository = run.get("repository")
    actor = run.get("actor")
    if (
        run.get("id") != int(run_id)
        or run.get("run_attempt") != int(run_attempt)
        or run.get("event") != "workflow_dispatch"
        or run.get("path") != stage.get("workflow")
        or run.get("head_sha") != stage.get("workflowCommit")
        or run.get("head_branch") != stage.get("workflowRef")
        or not isinstance(repository, dict)
        or repository.get("full_name") != stage.get("repository")
        or not isinstance(actor, dict)
        or str(actor.get("id")) != approved_actor_id
    ):
        raise ValueError(f"Workflow run identity is invalid: {stage['name']}.")
    if require_complete and (
        run.get("status") != "completed" or run.get("conclusion") != "success"
    ):
        raise ValueError(f"Workflow stage did not complete successfully: {stage['name']}.")


def validate_receipt_bytes(
    contents: bytes,
    preparation: dict[str, object],
    preparation_digest: str,
    stage_name: str,
) -> dict[str, object]:
    try:
        value = json.loads(contents)
    except json.JSONDecodeError as error:
        raise ValueError(f"Publication receipt is invalid JSON: {stage_name}.") from error
    if not isinstance(value, dict):
        raise ValueError(f"Publication receipt is invalid: {stage_name}.")
    TRAIN.validate_stage_receipt(
        value, preparation, preparation_digest, stage_name
    )
    return value


def validate_receipt_run(
    store: GitHubStore,
    receipt: dict[str, object],
    stage: dict[str, object],
    approved_actor_id: str,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    package_stage = stage.get("name") in {
        item[0] for item in TRAIN.PREPARATION_STAGES[:6]
    }
    coordinates = receipt.get("publication" if package_stage else "producer")
    if not isinstance(coordinates, dict):
        raise ValueError("Release receipt workflow identity is invalid.")
    if not package_stage and coordinates.get("actorId") != int(approved_actor_id):
        raise ValueError("Delivery completion producer actor is invalid.")
    run_id = str(coordinates.get("runId", ""))
    run_attempt = str(coordinates.get("runAttempt", ""))
    if not run_id.isdigit() or not run_attempt.isdigit():
        raise ValueError("Release receipt workflow identity is invalid.")
    if (
        expected_run_id is not None
        and (run_id != expected_run_id or run_attempt != expected_run_attempt)
    ):
        raise ValueError("Release receipt does not match the completed workflow run.")
    run = (
        store.workflow_run(str(stage["repository"]), run_id)
        if package_stage
        else store.workflow_run_attempt(
            str(stage["repository"]), int(run_id), int(run_attempt)
        )
    )
    validate_run(
        run,
        stage,
        run_id,
        run_attempt,
        approved_actor_id,
        require_complete=True,
    )
    if not package_stage:
        job_id = coordinates.get("jobId")
        jobs = store.workflow_jobs(
            str(stage["repository"]), int(run_id), int(run_attempt)
        )
        matches = [item for item in jobs if item.get("id") == job_id]
        if len(matches) != 1 or any(
            matches[0].get(field) != expected
            for field, expected in {
                "run_id": int(run_id),
                "run_attempt": int(run_attempt),
                "status": "completed",
                "conclusion": "success",
                "head_sha": stage.get("workflowCommit"),
            }.items()
        ):
            raise ValueError("Delivery completion producer job did not succeed.")


def evidence_from_artifact(
    store: GitHubStore,
    stage: dict[str, object],
    run_id: int,
    evidence: dict[str, object],
) -> dict[str, object]:
    repository = str(stage["repository"])
    artifact_id = evidence.get("artifactId")
    artifact_name = evidence.get("artifactName")
    file_name = evidence.get("fileName")
    TRAIN.require_positive_integer(artifact_id, "Delivery evidence artifact ID")
    TRAIN.require_safe_leaf(artifact_name, "Delivery evidence artifact name")
    TRAIN.require_safe_leaf(file_name, "Delivery evidence file name")
    matches = [
        item for item in store.run_artifacts(repository, str(run_id))
        if item.get("id") == artifact_id and item.get("name") == artifact_name
    ]
    workflow_run = matches[0].get("workflow_run") if len(matches) == 1 else None
    if (
        len(matches) != 1
        or matches[0].get("expired") is not False
        or not isinstance(workflow_run, dict)
        or workflow_run.get("id") != run_id
        or workflow_run.get("head_sha") != stage.get("workflowCommit")
        or workflow_run.get("head_branch") != stage.get("workflowRef")
    ):
        raise ValueError("Delivery evidence artifact is missing.")
    archive = store.artifact_bytes_bounded(
        repository, int(artifact_id), 128 * 1024 * 1024
    )
    try:
        bundle = zipfile.ZipFile(io.BytesIO(archive))
    except zipfile.BadZipFile as error:
        raise ValueError("Delivery evidence artifact is not a ZIP archive.") from error
    with bundle:
        files = [item for item in bundle.infolist() if not item.is_dir()]
        if not files or sum(item.file_size for item in files) > 256 * 1024 * 1024:
            raise ValueError("Delivery evidence artifact inventory is invalid.")
        for item in files:
            path = PurePosixPath(item.filename)
            if (
                path.is_absolute()
                or any(part in {"", ".", ".."} for part in path.parts)
                or item.flag_bits & 0x1
                or item.file_size > 128 * 1024 * 1024
            ):
                raise ValueError("Delivery evidence artifact member is invalid.")
        matches = [
            item for item in files
            if len(PurePosixPath(item.filename).parts) == 1
            and PurePosixPath(item.filename).name == file_name
        ]
        if len(matches) != 1 or matches[0].file_size > 4 * 1024 * 1024:
            raise ValueError("Delivery evidence file is missing.")
        contents = bundle.read(matches[0])
    if hashlib.sha256(contents).hexdigest() != evidence.get("sha256"):
        raise ValueError("Delivery evidence digest does not match.")
    try:
        value = json.loads(contents)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("Delivery evidence is invalid JSON.") from error
    if not isinstance(value, dict):
        raise ValueError("Delivery evidence is not an object.")
    return value


def strictly_equal_json(actual: object, expected: object) -> bool:
    if type(actual) is not type(expected):
        return False
    if isinstance(expected, dict):
        return set(actual) == set(expected) and all(
            strictly_equal_json(actual[key], value)
            for key, value in expected.items()
        )
    if isinstance(expected, list):
        return len(actual) == len(expected) and all(
            strictly_equal_json(actual_item, expected_item)
            for actual_item, expected_item in zip(actual, expected)
        )
    return actual == expected


def validate_delivery_deployment(
    store: GitHubStore,
    receipt: dict[str, object],
    stage: dict[str, object],
    approved_actor_id: str,
) -> None:
    deployment_receipt = receipt["deployment"]
    assert isinstance(deployment_receipt, dict)
    repository = str(stage["repository"])
    deployment_id = int(deployment_receipt["id"])
    deployment = store.deployment(repository, deployment_id)
    creator = deployment.get("creator")
    if (
        deployment.get("id") != deployment_id
        or deployment.get("sha") != stage.get("workflowCommit")
        or deployment.get("ref") != stage.get("workflowRef")
        or deployment.get("task") != "deploy"
        or deployment.get("environment") != deployment_receipt["environment"]
        or not isinstance(creator, dict)
        or str(creator.get("id")) != approved_actor_id
    ):
        raise ValueError("Delivery deployment identity is invalid.")
    producer = receipt["producer"]
    assert isinstance(producer, dict)
    expected_log_url = (
        f"https://github.com/{repository}/actions/runs/{producer['runId']}"
        f"/job/{deployment_receipt['jobId']}"
    )
    matches = []
    for status in store.deployment_statuses(repository, deployment_id):
        status_creator = status.get("creator")
        if (
            status.get("state") == "success"
            and status.get("environment") == deployment_receipt["environment"]
            and status.get("environment_url") == deployment_receipt["url"]
            and status.get("log_url") == expected_log_url
            and isinstance(status_creator, dict)
            and str(status_creator.get("id")) == approved_actor_id
        ):
            matches.append(status)
    if len(matches) != 1:
        raise ValueError("Delivery deployment success evidence is invalid.")


def validated_candidate_from_bytes(
    candidate: dict[str, object],
    kind: str,
    preparation: dict[str, object],
    preparation_digest: str,
    *,
    upstream_receipts: dict[str, bytes],
    toolchain_receipt_bytes: bytes | None = None,
) -> tuple[dict[str, object], bytes]:
    receipt = candidate.get("receipt")
    receipt_bytes = candidate.get("receiptBytes")
    if not isinstance(receipt, dict) or not isinstance(receipt_bytes, bytes):
        raise ValueError("Delivery completion candidate is incomplete.")
    try:
        parsed = json.loads(receipt_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("Delivery completion candidate is invalid JSON.") from error
    if not isinstance(parsed, dict) or not strictly_equal_json(parsed, receipt):
        raise ValueError("Delivery completion candidate bytes do not match.")
    TRAIN.validate_candidate_receipt(
        parsed, preparation, preparation_digest, kind
    )
    validate_candidate_dependencies(
        parsed,
        preparation,
        preparation_digest,
        upstream_receipts=upstream_receipts,
        toolchain_receipt_bytes=toolchain_receipt_bytes,
    )
    return parsed, receipt_bytes


def validate_delivery_completion(
    store: GitHubStore,
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_digest: str,
    candidates: dict[str, dict[str, object]],
    *,
    upstream_receipts: dict[str, bytes],
    approved_actor_id: str,
    expected_dispatch_attempt_identity: str,
) -> None:
    kind = receipt.get("kind")
    stage_name = TRAIN.DELIVERY_KINDS.get(kind)
    if stage_name is None or not str(kind).endswith("-completion"):
        raise ValueError("Delivery completion kind is invalid.")
    TRAIN.validate_stage_receipt(
        receipt, preparation, preparation_digest, stage_name
    )
    if (
        not approved_actor_id.isdigit()
        or int(approved_actor_id) < 1
        or TRAIN.SHA256.fullmatch(expected_dispatch_attempt_identity) is None
    ):
        raise ValueError("Delivery completion authentication inputs are invalid.")
    stage = TRAIN.preparation_stage(preparation, stage_name)
    producer = receipt.get("producer")
    deployment = receipt.get("deployment")
    if not isinstance(producer, dict) or not isinstance(deployment, dict):
        raise ValueError("Delivery completion coordinates are invalid.")
    if producer.get("dispatchAttemptIdentity") != expected_dispatch_attempt_identity:
        raise ValueError("Delivery completion dispatch identity does not match.")
    validate_receipt_run(store, receipt, stage, approved_actor_id)
    validate_candidate_dependencies(
        receipt,
        preparation,
        preparation_digest,
        upstream_receipts=upstream_receipts,
    )
    repository = str(stage["repository"])
    run_id = int(producer["runId"])
    run_attempt = int(producer["runAttempt"])
    checks = (
        receipt.get("liveChecks")
        if receipt.get("kind") == "playground-completion"
        else [receipt.get("liveCheck")]
    )
    if not isinstance(checks, list) or any(not isinstance(item, dict) for item in checks):
        raise ValueError("Delivery live-check coordinates are invalid.")
    jobs = store.workflow_jobs(repository, run_id, run_attempt)
    jobs_by_id = {
        item.get("id"): item for item in jobs
        if isinstance(item, dict) and isinstance(item.get("id"), int)
    }
    required_job_ids = {
        int(producer["jobId"]), int(deployment["jobId"]),
        *(int(check["jobId"]) for check in checks),
    }
    if len(jobs_by_id) != len([item for item in jobs if isinstance(item, dict)]):
        raise ValueError("Delivery workflow job IDs are duplicated or invalid.")
    for job_id in required_job_ids:
        job = jobs_by_id.get(job_id)
        if job is None or any(
            job.get(field) != expected
            for field, expected in {
                "run_id": run_id,
                "run_attempt": run_attempt,
                "status": "completed",
                "conclusion": "success",
                "head_sha": stage.get("workflowCommit"),
            }.items()
        ):
            raise ValueError("Delivery workflow job did not succeed.")
    validate_delivery_deployment(store, receipt, stage, approved_actor_id)
    evidence_common = {
        "schemaVersion": 1,
        "status": "PASS",
        "stage": stage_name,
        "repository": repository,
        "runId": run_id,
        "runAttempt": run_attempt,
        "dispatchAttemptIdentity": expected_dispatch_attempt_identity,
        "deployment": deployment,
    }
    if kind == "playground-completion":
        expected_kinds = {
            "playground-toolchain-candidate", "playground-site-candidate",
        }
        if set(candidates) != expected_kinds:
            raise ValueError("Playground completion candidate set is invalid.")
        toolchain, toolchain_bytes = validated_candidate_from_bytes(
            candidates["playground-toolchain-candidate"],
            "playground-toolchain-candidate",
            preparation,
            preparation_digest,
            upstream_receipts=upstream_receipts,
        )
        site, site_bytes = validated_candidate_from_bytes(
            candidates["playground-site-candidate"],
            "playground-site-candidate",
            preparation,
            preparation_digest,
            upstream_receipts=upstream_receipts,
            toolchain_receipt_bytes=toolchain_bytes,
        )
        if (
            hashlib.sha256(toolchain_bytes).hexdigest()
            != receipt.get("toolchainCandidateSha256")
            or hashlib.sha256(site_bytes).hexdigest()
            != receipt.get("siteCandidateSha256")
        ):
            raise ValueError("Playground completion candidate digests do not match.")
        site_identity = site.get("site")
        toolchain_identity = site.get("toolchain")
        if not isinstance(site_identity, dict) or not isinstance(toolchain_identity, dict):
            raise ValueError("Playground site candidate identities are invalid.")
        for check in checks:
            expected_evidence = {
                **evidence_common,
                "jobId": check["jobId"],
                "schemaVersion": 1,
                "status": "PASS",
                "browser": check["browser"],
                "siteIdentitySha256": check["siteIdentitySha256"],
                "toolchainId": check["toolchainId"],
                "toolchainManifestSha256": check["toolchainManifestSha256"],
            }
            if (
                check["siteIdentitySha256"] != site_identity.get("identitySha256")
                or check["toolchainId"] != toolchain_identity.get("id")
                or check["toolchainManifestSha256"]
                != toolchain_identity.get("manifestSha256")
                or not strictly_equal_json(
                    evidence_from_artifact(store, stage, run_id, check["evidence"]),
                    expected_evidence,
                )
            ):
                raise ValueError("Playground live evidence does not match its candidate.")
        return
    if set(candidates) != {"website-site-candidate"}:
        raise ValueError("Website completion candidate set is invalid.")
    site, site_bytes = validated_candidate_from_bytes(
        candidates["website-site-candidate"],
        "website-site-candidate",
        preparation,
        preparation_digest,
        upstream_receipts=upstream_receipts,
    )
    if (
        hashlib.sha256(site_bytes).hexdigest()
        != receipt.get("siteCandidateSha256")
    ):
        raise ValueError("Website completion candidate digest does not match.")
    check = checks[0]
    site_identity = site.get("site")
    expected_evidence = {
        **evidence_common,
        "jobId": check["jobId"],
        "schemaVersion": 1,
        "status": "PASS",
        "siteIdentitySha256": check["siteIdentitySha256"],
        "playgroundCompletionSha256": check["playgroundCompletionSha256"],
    }
    if (
        not isinstance(site_identity, dict)
        or check["siteIdentitySha256"] != site_identity.get("identitySha256")
        or not strictly_equal_json(
            evidence_from_artifact(store, stage, run_id, check["evidence"]),
            expected_evidence,
        )
    ):
        raise ValueError("Website live evidence does not match its candidate.")


def write_exact(path: Path, contents: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() != contents:
        raise ValueError(f"Output already contains different bytes: {path.name}.")
    path.write_bytes(contents)


def prepare_start(
    store: GitHubStore,
    release_id: int,
    output: Path,
) -> dict[str, object]:
    release = store.release(CORE_REPOSITORY, release_id)
    contents = store.asset_bytes(
        CORE_REPOSITORY, release_id, PREPARATION.PREPARATION_ASSET
    )
    if contents is None:
        raise ValueError("Core preview release has no immutable preparation asset.")
    write_exact(output, contents)
    preparation, digest = TRAIN.read_preparation(output)
    stage = TRAIN.preparation_stage(preparation, CORE_PREVIEW_STAGE)
    if stage.get("releaseId") != release_id:
        raise ValueError("Core preview release ID does not match preparation.")
    validate_published_release(store, release, stage)
    return {
        "status": "PASS",
        "version": preparation["version"],
        "preparationSha256": digest,
        "releaseId": release_id,
    }


def upstream_receipts(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    stage: dict[str, object],
    approved_actor_id: str,
    directory: Path,
) -> list[Path]:
    expected = stage.get("upstreamStages")
    if not isinstance(expected, list):
        raise ValueError("Prepared upstream stage list is invalid.")
    result = []
    for upstream_name in expected:
        upstream_stage = TRAIN.preparation_stage(preparation, str(upstream_name))
        release_id = upstream_stage.get("releaseId")
        assert isinstance(release_id, int)
        name = receipt_asset_name(str(upstream_name))
        contents = store.asset_bytes(
            str(upstream_stage["repository"]), release_id, name
        )
        if contents is None:
            raise ValueError(f"Upstream publication receipt is missing: {upstream_name}.")
        receipt = validate_receipt_bytes(
            contents, preparation, preparation_digest, str(upstream_name)
        )
        validate_receipt_run(
            store, receipt, upstream_stage, approved_actor_id
        )
        path = directory / name
        write_exact(path, contents)
        result.append(path)
    return result


def publish_prepared_release(
    store: GitHubStore,
    stage: dict[str, object],
    upstream_paths: list[Path],
    preparation_bytes: bytes,
) -> dict[str, object]:
    repository = str(stage["repository"])
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    release = store.release(repository, release_id)
    if not release_matches_stage(release, stage):
        raise ValueError(f"Prepared release identity changed: {stage['name']}.")
    if release.get("target_commitish") != stage.get("sourceCommit"):
        raise ValueError(f"Prepared release target changed: {stage['name']}.")
    tag_commit = store.tag_commit(repository, str(stage["ref"]))
    if tag_commit is not None and tag_commit != stage.get("sourceCommit"):
        raise ValueError(f"Prepared release tag changed: {stage['name']}.")
    preparation_asset = store.asset_bytes(
        repository, release_id, PREPARATION.PREPARATION_ASSET
    )
    if preparation_asset != preparation_bytes:
        raise ValueError(f"Prepared release approval changed: {stage['name']}.")
    for path in upstream_paths:
        ensure_asset(store, repository, release_id, path.name, path.read_bytes())
    if release.get("draft") is True:
        if stage.get("name") == CORE_PREVIEW_STAGE:
            raise ValueError("Core preview must be published by the owner to start the train.")
        release = store.publish_release(repository, release_id)
    validate_published_release(store, release, stage)
    return release


def expected_run_title(preparation_digest: str, stage_name: str) -> str:
    identity = COORDINATOR.stage_identity(preparation_digest, stage_name)
    return f"Release {stage_name} {identity}"


def recoverable_run(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
) -> dict[str, object] | None:
    matches = []
    title = expected_run_title(preparation_digest, str(stage["name"]))
    runs = store.workflow_runs(
        str(stage["repository"]), str(stage["workflow"]), str(stage["workflowRef"])
    )
    for run in runs:
        if run.get("display_title") != title:
            continue
        run_id = str(run.get("id", ""))
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed" and run.get("conclusion") != "success":
            continue
        matches.append(run)
    if len(matches) > 1:
        raise ValueError(f"Multiple recoverable workflow runs exist: {stage['name']}.")
    return matches[0] if matches else None


def recoverable_delivery_run(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
    *,
    excluded_run_ids: set[int] | None = None,
) -> dict[str, object] | None:
    matches = []
    excluded = excluded_run_ids or set()
    title = expected_run_title(preparation_digest, str(stage["name"]))
    for run in store.workflow_runs(
        str(stage["repository"]), str(stage["workflow"]), str(stage["workflowRef"])
    ):
        if run.get("display_title") != title:
            continue
        if run.get("id") in excluded:
            continue
        run_id = str(run.get("id", ""))
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        matches.append(run)
    if len(matches) > 1:
        raise ValueError(
            f"Multiple visible delivery workflow runs exist: {stage['name']}."
        )
    return matches[0] if matches else None


def dispatch_coordinates(
    stage: dict[str, object], preparation_digest: str, dispatch: dict[str, object]
) -> bytes:
    value = {
        "schemaVersion": 1,
        "stage": stage["name"],
        "stageIdentity": COORDINATOR.stage_identity(
            preparation_digest, str(stage["name"])
        ),
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "workflowRunId": str(dispatch["workflowRunId"]),
        "dispatchAttemptIdentity": str(dispatch["dispatchAttemptIdentity"]),
    }
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def dispatch_intent(
    stage: dict[str, object], preparation_digest: str, attempt_identity: str
) -> bytes:
    value = {
        "schemaVersion": 1,
        "stage": stage["name"],
        "stageIdentity": COORDINATOR.stage_identity(
            preparation_digest, str(stage["name"])
        ),
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "workflowCommit": stage["workflowCommit"],
        "workflowRef": stage["workflowRef"],
        "dispatchAttemptIdentity": attempt_identity,
    }
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def persisted_recoverable_run(
    store: GitHubStore,
    preparation: dict[str, object],
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
) -> tuple[tuple[dict[str, object], str] | None, set[str]]:
    anchor_repository, release_id = TRAIN.stage_state_anchor(
        preparation, str(stage["name"])
    )
    workflow_repository = str(stage["repository"])
    prefix = f"dispatch-coordinate-{stage['name']}-"
    matches = [
        asset for asset in store.assets(anchor_repository, release_id)
        if isinstance(asset.get("name"), str)
        and str(asset["name"]).startswith(prefix)
    ]
    candidates: list[tuple[dict[str, object], str]] = []
    coordinate_attempts: set[str] = set()
    for asset in matches:
        name = str(asset["name"])
        suffix = name[len(prefix):]
        if re.fullmatch(r"[1-9][0-9]*\.json", suffix) is None:
            raise ValueError(f"Persisted dispatch coordinate name is invalid: {name}.")
        contents = store.asset_bytes(anchor_repository, release_id, name)
        if contents is None:
            raise ValueError(f"Persisted dispatch coordinate is missing: {name}.")
        try:
            value = json.loads(contents)
        except json.JSONDecodeError as error:
            raise ValueError(f"Persisted dispatch coordinate is invalid: {name}.") from error
        expected = {
            "schemaVersion": 1,
            "stage": stage["name"],
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, str(stage["name"])
            ),
            "repository": stage["repository"],
            "workflow": stage["workflow"],
            "workflowRunId": suffix.removesuffix(".json"),
            "dispatchAttemptIdentity": value.get("dispatchAttemptIdentity"),
        }
        attempt_identity = value.get("dispatchAttemptIdentity")
        if (
            not isinstance(attempt_identity, str)
            or COORDINATOR.SHA256.fullmatch(attempt_identity) is None
            or value != expected
            or attempt_identity in coordinate_attempts
        ):
            raise ValueError(f"Persisted dispatch coordinate changed: {name}.")
        coordinate_attempts.add(attempt_identity)
        run_id = str(value["workflowRunId"])
        run = store.workflow_run(workflow_repository, run_id)
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed" and run.get("conclusion") != "success":
            continue
        candidates.append((run, attempt_identity))
    if len(candidates) > 1:
        raise ValueError(f"Multiple persisted workflow runs are viable: {stage['name']}.")
    return (candidates[0] if candidates else None), coordinate_attempts


def persisted_intents(
    store: GitHubStore,
    preparation: dict[str, object],
    stage: dict[str, object],
    preparation_digest: str,
) -> set[str]:
    repository, release_id = TRAIN.stage_state_anchor(
        preparation, str(stage["name"])
    )
    prefix = f"dispatch-intent-{stage['name']}-"
    identities: set[str] = set()
    for asset in store.assets(repository, release_id):
        name = asset.get("name")
        if not isinstance(name, str) or not name.startswith(prefix):
            continue
        suffix = name[len(prefix):]
        if re.fullmatch(r"[0-9a-f]{64}\.json", suffix) is None:
            raise ValueError(f"Persisted dispatch intent name is invalid: {name}.")
        identity = suffix.removesuffix(".json")
        contents = store.asset_bytes(repository, release_id, name)
        if contents != dispatch_intent(stage, preparation_digest, identity):
            raise ValueError(f"Persisted dispatch intent changed: {name}.")
        if identity in identities:
            raise ValueError(f"Persisted dispatch intent is duplicated: {name}.")
        identities.add(identity)
    return identities


def receipt_from_artifact(
    store: GitHubStore,
    stage: dict[str, object],
    run_id: str,
    run_attempt: str,
) -> bytes:
    expected_name = (
        f"publication-receipt-{stage['name']}-{run_id}-{run_attempt}"
    )
    matches = [
        item for item in store.run_artifacts(str(stage["repository"]), run_id)
        if item.get("name") == expected_name
    ]
    if (
        len(matches) != 1
        or matches[0].get("expired") is not False
        or not isinstance(matches[0].get("id"), int)
    ):
        raise ValueError(f"Publication receipt artifact is missing: {stage['name']}.")
    archive = store.artifact_bytes(
        str(stage["repository"]), int(matches[0]["id"])
    )
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        files = [item for item in bundle.infolist() if not item.is_dir()]
        if len(files) != 1:
            raise ValueError("Publication receipt artifact inventory is invalid.")
        path = PurePosixPath(files[0].filename)
        if (
            path.is_absolute()
            or any(part in {"", ".", ".."} for part in path.parts)
            or path.name != RECEIPT_FILE
            or files[0].file_size > 4 * 1024 * 1024
        ):
            raise ValueError("Publication receipt artifact path is invalid.")
        return bundle.read(files[0])


def wait_for_run(
    store: GitHubStore,
    stage: dict[str, object],
    request: dict[str, object],
    dispatch: dict[str, object],
    approved_actor_id: str,
    *,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> tuple[dict[str, object], str]:
    run_id = str(dispatch["workflowRunId"])
    deadline = clock() + STAGE_TIMEOUT_SECONDS
    while True:
        run = store.workflow_run(str(stage["repository"]), run_id)
        COORDINATOR.validate_workflow_run(
            run, request, dispatch, require_complete=False
        )
        run_attempt = str(run.get("run_attempt", ""))
        if not run_attempt.isdigit():
            raise ValueError("Dispatched workflow run attempt is invalid.")
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed":
            validate_run(
                run,
                stage,
                run_id,
                run_attempt,
                approved_actor_id,
                require_complete=True,
            )
            return run, run_attempt
        remaining = deadline - clock()
        if remaining <= 0:
            raise TimeoutError(f"Timed out waiting for release stage: {stage['name']}.")
        sleep(min(POLL_SECONDS, remaining))


def prepare_delivery_anchor(
    store: GitHubStore,
    preparation: dict[str, object],
    stage: dict[str, object],
    preparation_bytes: bytes,
    upstream_paths: list[Path],
) -> tuple[str, int]:
    stage_name = str(stage["name"])
    repository, release_id = TRAIN.stage_state_anchor(preparation, stage_name)
    release = store.release(repository, release_id)
    if stage_name == "playground":
        draft = release.get("draft")
        tag_commit = store.tag_commit(repository, str(stage["ref"]))
        if (
            not release_matches_stage(release, stage)
            or release.get("target_commitish") != stage.get("sourceCommit")
            or not isinstance(draft, bool)
            or (tag_commit not in {None, stage.get("sourceCommit")} if draft else
                tag_commit != stage.get("sourceCommit"))
        ):
            raise ValueError("Prepared Playground release identity changed.")
    elif stage_name == "website":
        anchor_stage = TRAIN.preparation_stage(preparation, CORE_PREVIEW_STAGE)
        validate_published_release(store, release, anchor_stage)
    else:
        raise ValueError("Delivery stage is invalid.")
    if store.asset_bytes(
        repository, release_id, PREPARATION.PREPARATION_ASSET
    ) != preparation_bytes:
        raise ValueError("Delivery state anchor approval changed.")
    for path in upstream_paths:
        ensure_asset(store, repository, release_id, path.name, path.read_bytes())
    return repository, release_id


def delivery_candidate_kinds(stage_name: str) -> tuple[str, ...]:
    if stage_name == "playground":
        return (
            "playground-toolchain-candidate", "playground-site-candidate",
        )
    if stage_name == "website":
        return ("website-site-candidate",)
    raise ValueError("Delivery stage is invalid.")


def load_delivery_candidates(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    stage_name: str,
    upstream: dict[str, bytes],
) -> dict[str, dict[str, object]]:
    result: dict[str, dict[str, object]] = {}
    for kind in delivery_candidate_kinds(stage_name):
        toolchain_bytes = None
        if kind == "playground-site-candidate":
            toolchain = result.get("playground-toolchain-candidate")
            if toolchain is None:
                break
            value = toolchain.get("receiptBytes")
            if not isinstance(value, bytes):
                raise ValueError("Attached Playground toolchain receipt is invalid.")
            toolchain_bytes = value
        candidate = load_attached_candidate(
            store,
            preparation,
            preparation_digest,
            kind,
            upstream_receipts=upstream,
            toolchain_receipt_bytes=toolchain_bytes,
        )
        if candidate is None:
            break
        result[kind] = candidate
    return result


def retained_candidate_coordinates(
    stage_name: str, candidates: dict[str, dict[str, object]]
) -> list[dict[str, str]]:
    result = []
    for kind in delivery_candidate_kinds(stage_name):
        candidate = candidates.get(kind)
        if candidate is None:
            break
        receipt_bytes = candidate.get("receiptBytes")
        if not isinstance(receipt_bytes, bytes):
            raise ValueError("Retained delivery candidate receipt is invalid.")
        result.append({
            "kind": kind,
            "receiptSha256": hashlib.sha256(receipt_bytes).hexdigest(),
        })
    return COORDINATOR.validate_retained_candidates(stage_name, result)


def persisted_delivery_runs(
    store: GitHubStore,
    preparation: dict[str, object],
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
) -> list[tuple[dict[str, object], str]]:
    stage_name = str(stage["name"])
    repository, release_id = TRAIN.stage_state_anchor(preparation, stage_name)
    workflow_repository = str(stage["repository"])
    prefix = f"dispatch-coordinate-{stage_name}-"
    result = []
    identities: set[str] = set()
    for asset in store.assets(repository, release_id):
        name = asset.get("name")
        if not isinstance(name, str) or not name.startswith(prefix):
            continue
        suffix = name[len(prefix):]
        if re.fullmatch(r"[1-9][0-9]*\.json", suffix) is None:
            raise ValueError(f"Persisted dispatch coordinate name is invalid: {name}.")
        run_id = suffix.removesuffix(".json")
        contents = store.asset_bytes(repository, release_id, name)
        if contents is None:
            raise ValueError(f"Persisted dispatch coordinate is missing: {name}.")
        try:
            value = json.loads(contents)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"Persisted dispatch coordinate is invalid: {name}.") from error
        identity = value.get("dispatchAttemptIdentity") if isinstance(value, dict) else None
        expected = {
            "schemaVersion": 1,
            "stage": stage_name,
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, stage_name
            ),
            "repository": stage["repository"],
            "workflow": stage["workflow"],
            "workflowRunId": run_id,
            "dispatchAttemptIdentity": identity,
        }
        if (
            not isinstance(value, dict)
            or value != expected
            or not isinstance(identity, str)
            or COORDINATOR.SHA256.fullmatch(identity) is None
            or identity in identities
        ):
            raise ValueError(f"Persisted dispatch coordinate changed: {name}.")
        identities.add(identity)
        run = store.workflow_run(workflow_repository, run_id)
        validate_run(
            run,
            stage,
            run_id,
            str(run.get("run_attempt", "")),
            approved_actor_id,
            require_complete=False,
        )
        result.append((run, identity))
    return sorted(result, key=lambda item: int(item[0]["id"]))


def harvest_delivery_candidates(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    stage: dict[str, object],
    run: dict[str, object],
    dispatch_attempt_identity: str,
    approved_actor_id: str,
    upstream: dict[str, bytes],
    existing: dict[str, dict[str, object]],
) -> list[str]:
    run_id = int(run["id"])
    latest_attempt = int(run["run_attempt"])
    repository = str(stage["repository"])
    artifacts = store.run_artifacts(repository, str(run_id))
    names = {
        item.get("name") for item in artifacts if isinstance(item.get("name"), str)
    }
    collected: dict[str, dict[str, object]] = {}
    for kind in delivery_candidate_kinds(str(stage["name"])):
        if kind in existing:
            continue
        matches = []
        for attempt in range(1, latest_attempt + 1):
            receipt_name = candidate_receipt_artifact_name(kind, run_id, attempt)
            payload_name = TRAIN.delivery_candidate_payload_artifact_name(
                kind, run_id, attempt
            )
            has_receipt = receipt_name in names
            has_payload = payload_name in names
            if has_payload and not has_receipt:
                continue
            if has_receipt:
                matches.append(attempt)
        if len(matches) > 1:
            raise ValueError(f"Multiple delivery candidate attempts exist: {kind}.")
        if matches:
            collected[kind] = CandidateArtifactCollector(store).collect(
                preparation,
                preparation_digest,
                kind,
                run_id,
                matches[0],
                approved_actor_id,
                dispatch_attempt_identity,
            )
    newly_attached = []
    combined = dict(existing)
    for kind in delivery_candidate_kinds(str(stage["name"])):
        candidate = collected.get(kind)
        if candidate is None:
            continue
        toolchain_bytes = None
        if kind == "playground-site-candidate":
            toolchain = combined.get("playground-toolchain-candidate")
            if toolchain is None or not isinstance(toolchain.get("receiptBytes"), bytes):
                raise ValueError("Playground site candidate has no toolchain candidate.")
            toolchain_bytes = toolchain["receiptBytes"]
        attach_candidate(
            store,
            preparation,
            preparation_digest,
            kind,
            candidate,
            upstream_receipts=upstream,
            toolchain_receipt_bytes=toolchain_bytes,
        )
        combined[kind] = candidate
        newly_attached.append(kind)
    return newly_attached


def completion_receipt_from_artifact(
    store: GitHubStore,
    stage: dict[str, object],
    run_id: int,
    run_attempt: int,
) -> bytes:
    repository = str(stage["repository"])
    name = completion_receipt_artifact_name(
        str(stage["name"]), run_id, run_attempt
    )
    matches = [
        item for item in store.run_artifacts(repository, str(run_id))
        if item.get("name") == name
    ]
    workflow_run = matches[0].get("workflow_run") if len(matches) == 1 else None
    if (
        len(matches) != 1
        or matches[0].get("expired") is not False
        or not isinstance(matches[0].get("id"), int)
        or not isinstance(workflow_run, dict)
        or workflow_run.get("id") != run_id
        or workflow_run.get("head_sha") != stage.get("workflowCommit")
        or workflow_run.get("head_branch") != stage.get("workflowRef")
    ):
        raise ValueError("Delivery completion artifact is missing.")
    archive = store.artifact_bytes_bounded(
        repository, int(matches[0]["id"]), 8 * 1024 * 1024
    )
    try:
        bundle = zipfile.ZipFile(io.BytesIO(archive))
    except zipfile.BadZipFile as error:
        raise ValueError("Delivery completion artifact is not a ZIP archive.") from error
    with bundle:
        files = [item for item in bundle.infolist() if not item.is_dir()]
        if len(files) != 1:
            raise ValueError("Delivery completion artifact inventory is invalid.")
        item = files[0]
        path = PurePosixPath(item.filename)
        if (
            path.is_absolute()
            or any(part in {"", ".", ".."} for part in path.parts)
            or len(path.parts) != 1
            or path.name != COMPLETION_RECEIPT_FILE
            or item.flag_bits & 0x1
            or item.file_size < 1
            or item.file_size > 4 * 1024 * 1024
        ):
            raise ValueError("Delivery completion artifact member is invalid.")
        return bundle.read(item)


def wait_for_terminal_delivery_run(
    store: GitHubStore,
    stage: dict[str, object],
    request: dict[str, object],
    dispatch: dict[str, object],
    approved_actor_id: str,
    *,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> tuple[dict[str, object], str]:
    run_id = str(dispatch["workflowRunId"])
    deadline = clock() + STAGE_TIMEOUT_SECONDS
    while True:
        run = store.workflow_run(str(stage["repository"]), run_id)
        COORDINATOR.validate_workflow_run(
            run, request, dispatch, require_complete=False
        )
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed":
            return run, run_attempt
        remaining = deadline - clock()
        if remaining <= 0:
            raise TimeoutError(f"Timed out waiting for delivery stage: {stage['name']}.")
        sleep(min(POLL_SECONDS, remaining))


def publish_completed_playground_release(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_bytes: bytes,
    completion_bytes: bytes,
) -> None:
    repository = str(stage["repository"])
    release_id = int(stage["releaseId"])
    release = store.release(repository, release_id)
    draft = release.get("draft")
    tag_commit = store.tag_commit(repository, str(stage["ref"]))
    if (
        not release_matches_stage(release, stage)
        or release.get("target_commitish") != stage.get("sourceCommit")
        or not isinstance(draft, bool)
        or (tag_commit not in {None, stage.get("sourceCommit")} if draft else
            tag_commit != stage.get("sourceCommit"))
        or store.asset_bytes(
            repository, release_id, PREPARATION.PREPARATION_ASSET
        ) != preparation_bytes
        or store.asset_bytes(
            repository, release_id, receipt_asset_name("playground")
        ) != completion_bytes
    ):
        raise ValueError("Prepared Playground release identity changed.")
    if draft:
        release = store.publish_release(repository, release_id)
    validate_published_release(store, release, stage)


def run_delivery_stage(
    store: GitHubStore,
    preparation_path: Path,
    stage_name: str,
    approved_actor_id: str,
    output: Path,
) -> dict[str, object]:
    if stage_name not in {"playground", "website"}:
        raise ValueError("Delivery stage is invalid.")
    if not approved_actor_id.isdigit() or int(approved_actor_id) < 1:
        raise ValueError("Approved GitHub App actor ID is invalid.")
    preparation, preparation_digest = TRAIN.read_preparation(preparation_path)
    stage = TRAIN.preparation_stage(preparation, stage_name)
    upstream_paths = upstream_receipts(
        store,
        preparation,
        preparation_digest,
        stage,
        approved_actor_id,
        output.parent / "upstream",
    )
    upstream = {
        str(TRAIN.read_json(path)["stage"]): path.read_bytes()
        for path in upstream_paths
    }
    anchor_repository, anchor_release_id = prepare_delivery_anchor(
        store,
        preparation,
        stage,
        preparation_path.read_bytes(),
        upstream_paths,
    )
    candidates = load_delivery_candidates(
        store, preparation, preparation_digest, stage_name, upstream
    )
    completion_asset = receipt_asset_name(stage_name)
    existing_completion = store.asset_bytes(
        anchor_repository, anchor_release_id, completion_asset
    )
    if existing_completion is not None:
        receipt = validate_receipt_bytes(
            existing_completion, preparation, preparation_digest, stage_name
        )
        if set(candidates) != set(delivery_candidate_kinds(stage_name)):
            raise ValueError("Delivery completion has an incomplete candidate set.")
        producer = receipt.get("producer")
        if not isinstance(producer, dict):
            raise ValueError("Delivery completion producer is invalid.")
        records = persisted_delivery_runs(
            store,
            preparation,
            stage,
            preparation_digest,
            approved_actor_id,
        )
        dispatch_identity = producer.get("dispatchAttemptIdentity")
        if not isinstance(dispatch_identity, str) or len([
            item for item in records
            if item[0].get("id") == producer.get("runId")
            and item[1] == dispatch_identity
        ]) != 1:
            raise ValueError("Delivery completion has no persisted dispatch coordinate.")
        validate_delivery_completion(
            store,
            receipt,
            preparation,
            preparation_digest,
            candidates,
            upstream_receipts=upstream,
            approved_actor_id=approved_actor_id,
            expected_dispatch_attempt_identity=dispatch_identity,
        )
        if stage_name == "playground":
            publish_completed_playground_release(
                store,
                stage,
                preparation_path.read_bytes(),
                existing_completion,
            )
        write_exact(output, existing_completion)
        return {
            "status": "PASS",
            "stage": stage_name,
            "mode": "resume",
            "receiptSha256": hashlib.sha256(existing_completion).hexdigest(),
        }

    records = persisted_delivery_runs(
        store,
        preparation,
        stage,
        preparation_digest,
        approved_actor_id,
    )
    coordinate_attempts = {identity for _, identity in records}
    coordinate_run_ids = {int(run["id"]) for run, _ in records}
    intent_attempts = persisted_intents(
        store, preparation, stage, preparation_digest
    )
    if not coordinate_attempts.issubset(intent_attempts):
        raise ValueError(f"Persisted dispatch coordinate has no intent: {stage_name}.")
    failed = [
        item for item in records
        if item[0].get("status") == "completed"
        and item[0].get("conclusion") != "success"
    ]
    missing_kinds = set(delivery_candidate_kinds(stage_name)) - set(candidates)
    if missing_kinds and failed:
        newly_attached = []
        for run, identity in failed:
            newly_attached.extend(harvest_delivery_candidates(
                store,
                preparation,
                preparation_digest,
                stage,
                run,
                identity,
                approved_actor_id,
                upstream,
                load_delivery_candidates(
                    store,
                    preparation,
                    preparation_digest,
                    stage_name,
                    upstream,
                ),
            ))
        if newly_attached:
            raise RuntimeError(
                f"Retained delivery candidate from failed {stage_name} run; "
                "rerun the coordinator to reuse it."
            )
    retained = retained_candidate_coordinates(stage_name, candidates)
    request = COORDINATOR.create_dispatch(
        preparation_path=preparation_path,
        stage_name=stage_name,
        coordinator_repository=COORDINATOR.COORDINATOR_REPOSITORY,
        coordinator_run_id=os.environ.get("GITHUB_RUN_ID", ""),
        coordinator_run_attempt=os.environ.get("GITHUB_RUN_ATTEMPT", ""),
        upstream_receipt_paths={
            str(TRAIN.read_json(path)["stage"]): path for path in upstream_paths
        },
        retained_candidates=retained,
    )
    persisted, _ = persisted_recoverable_run(
        store, preparation, stage, preparation_digest, approved_actor_id
    )
    unresolved_attempts = intent_attempts - coordinate_attempts
    recovered = persisted[0] if persisted is not None else None
    recovered_identity = persisted[1] if persisted is not None else None
    if recovered is None:
        recovered = recoverable_delivery_run(
            store,
            stage,
            preparation_digest,
            approved_actor_id,
            excluded_run_ids=coordinate_run_ids,
        )
        if recovered is not None:
            if len(unresolved_attempts) != 1:
                raise ValueError(
                    f"Visible workflow run has no unique dispatch intent: {stage_name}."
                )
            recovered_identity = next(iter(unresolved_attempts))
    mode = "recovered"
    dispatch: dict[str, object]
    if recovered is None:
        if unresolved_attempts:
            raise RuntimeError(
                f"Dispatch intent has no visible workflow run yet: {stage_name}."
            )
        request_inputs = request["inputs"]
        assert isinstance(request_inputs, dict)
        current_identity = str(request_inputs["dispatch_attempt_identity"])
        ensure_asset(
            store,
            anchor_repository,
            anchor_release_id,
            f"dispatch-intent-{stage_name}-{current_identity}.json",
            dispatch_intent(stage, preparation_digest, current_identity),
        )
        mode = "dispatch"
        try:
            dispatch = COORDINATOR.dispatch_workflow(
                request, store.token, preparation_path
            )
        except Exception:
            recovered = recoverable_delivery_run(
                store,
                stage,
                preparation_digest,
                approved_actor_id,
                excluded_run_ids=coordinate_run_ids,
            )
            if recovered is None:
                raise RuntimeError(
                    f"Dispatch response was lost before the workflow run became visible: "
                    f"{stage_name}."
                )
            recovered_identity = current_identity
            mode = "recovered"
    if recovered is not None:
        if recovered_identity is None:
            raise ValueError("Recovered delivery run has no dispatch identity.")
        run_id = str(recovered["id"])
        dispatch = {
            "workflowRunId": run_id,
            "runUrl": f"https://api.github.com/repos/{stage['repository']}"
                      f"/actions/runs/{run_id}",
            "htmlUrl": f"https://github.com/{stage['repository']}/actions/runs/{run_id}",
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, stage_name
            ),
            "dispatchAttemptIdentity": recovered_identity,
        }
    ensure_asset(
        store,
        anchor_repository,
        anchor_release_id,
        f"dispatch-coordinate-{stage_name}-{dispatch['workflowRunId']}.json",
        dispatch_coordinates(stage, preparation_digest, dispatch),
    )
    run, run_attempt = wait_for_terminal_delivery_run(
        store, stage, request, dispatch, approved_actor_id
    )
    newly_attached = harvest_delivery_candidates(
        store,
        preparation,
        preparation_digest,
        stage,
        run,
        str(dispatch["dispatchAttemptIdentity"]),
        approved_actor_id,
        upstream,
        candidates,
    )
    if run.get("conclusion") != "success":
        detail = (
            f"; retained {', '.join(newly_attached)}" if newly_attached else ""
        )
        raise RuntimeError(
            f"Delivery stage failed before completion: {stage_name}{detail}."
        )
    candidates = load_delivery_candidates(
        store, preparation, preparation_digest, stage_name, upstream
    )
    if set(candidates) != set(delivery_candidate_kinds(stage_name)):
        raise ValueError("Successful delivery run has an incomplete candidate set.")
    completion_bytes = completion_receipt_from_artifact(
        store, stage, int(run["id"]), int(run_attempt)
    )
    completion = validate_receipt_bytes(
        completion_bytes, preparation, preparation_digest, stage_name
    )
    producer = completion.get("producer")
    if (
        not isinstance(producer, dict)
        or producer.get("runId") != run.get("id")
        or producer.get("runAttempt") != int(run_attempt)
    ):
        raise ValueError("Delivery completion does not match its artifact run.")
    validate_delivery_completion(
        store,
        completion,
        preparation,
        preparation_digest,
        candidates,
        upstream_receipts=upstream,
        approved_actor_id=approved_actor_id,
        expected_dispatch_attempt_identity=str(dispatch["dispatchAttemptIdentity"]),
    )
    ensure_asset(
        store,
        anchor_repository,
        anchor_release_id,
        completion_asset,
        completion_bytes,
    )
    if stage_name == "playground":
        publish_completed_playground_release(
            store,
            stage,
            preparation_path.read_bytes(),
            completion_bytes,
        )
    write_exact(output, completion_bytes)
    return {
        "status": "PASS",
        "stage": stage_name,
        "mode": mode,
        "workflowRunId": dispatch["workflowRunId"],
        "receiptSha256": hashlib.sha256(completion_bytes).hexdigest(),
    }


def run_stage(
    store: GitHubStore,
    preparation_path: Path,
    stage_name: str,
    approved_actor_id: str,
    output: Path,
) -> dict[str, object]:
    if not approved_actor_id.isdigit() or int(approved_actor_id) < 1:
        raise ValueError("Approved GitHub App actor ID is invalid.")
    if stage_name in {"playground", "website"}:
        return run_delivery_stage(
            store, preparation_path, stage_name, approved_actor_id, output
        )
    preparation, preparation_digest = TRAIN.read_preparation(preparation_path)
    stage = TRAIN.preparation_stage(preparation, stage_name)
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}:
        raise ValueError("Only package publication stages are supported here.")
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    repository = str(stage["repository"])
    upstream_directory = output.parent / "upstream"
    upstream_paths = upstream_receipts(
        store,
        preparation,
        preparation_digest,
        stage,
        approved_actor_id,
        upstream_directory,
    )
    preparation_bytes = preparation_path.read_bytes()
    publish_prepared_release(store, stage, upstream_paths, preparation_bytes)

    own_asset = receipt_asset_name(stage_name)
    existing = store.asset_bytes(repository, release_id, own_asset)
    if existing is not None:
        receipt = validate_receipt_bytes(
            existing, preparation, preparation_digest, stage_name
        )
        validate_receipt_run(store, receipt, stage, approved_actor_id)
        write_exact(output, existing)
        return {
            "status": "PASS",
            "stage": stage_name,
            "mode": "resume",
            "receiptSha256": hashlib.sha256(existing).hexdigest(),
        }

    request = COORDINATOR.create_dispatch(
        preparation_path=preparation_path,
        stage_name=stage_name,
        coordinator_repository=COORDINATOR.COORDINATOR_REPOSITORY,
        coordinator_run_id=os.environ.get("GITHUB_RUN_ID", ""),
        coordinator_run_attempt=os.environ.get("GITHUB_RUN_ATTEMPT", ""),
        upstream_receipt_paths={
            str(TRAIN.read_json(path)["stage"]): path for path in upstream_paths
        },
    )
    persisted, coordinate_attempts = persisted_recoverable_run(
        store, preparation, stage, preparation_digest, approved_actor_id
    )
    intent_attempts = persisted_intents(
        store, preparation, stage, preparation_digest
    )
    if not coordinate_attempts.issubset(intent_attempts):
        raise ValueError(f"Persisted dispatch coordinate has no intent: {stage_name}.")
    unresolved_attempts = intent_attempts - coordinate_attempts
    recovered = persisted[0] if persisted is not None else None
    recovered_attempt = persisted[1] if persisted is not None else None
    if recovered is None:
        recovered = recoverable_run(
            store, stage, preparation_digest, approved_actor_id
        )
        if recovered is not None:
            if len(unresolved_attempts) != 1:
                raise ValueError(
                    f"Visible workflow run has no unique dispatch intent: {stage_name}."
                )
            recovered_attempt = next(iter(unresolved_attempts))
    mode = "recovered"
    if recovered is None:
        if unresolved_attempts:
            raise RuntimeError(
                f"Dispatch intent has no visible workflow run yet: {stage_name}."
            )
        request_inputs = request.get("inputs")
        assert isinstance(request_inputs, dict)
        current_attempt = str(request_inputs["dispatch_attempt_identity"])
        intent_name = f"dispatch-intent-{stage_name}-{current_attempt}.json"
        intent_bytes = dispatch_intent(stage, preparation_digest, current_attempt)
        ensure_asset(
            store, repository, release_id, intent_name, intent_bytes
        )
        mode = "dispatch"
        try:
            dispatch = COORDINATOR.dispatch_workflow(
                request, store.token, preparation_path
            )
        except Exception:
            recovered = recoverable_run(
                store, stage, preparation_digest, approved_actor_id
            )
            if recovered is None:
                raise RuntimeError(
                    f"Dispatch response was lost before the workflow run became visible: "
                    f"{stage_name}."
                )
            mode = "recovered"
            recovered_attempt = current_attempt
    if recovered is not None:
        assert recovered_attempt is not None
        run_id = str(recovered["id"])
        dispatch = {
            "workflowRunId": run_id,
            "runUrl": (
                f"https://api.github.com/repos/{repository}/actions/runs/{run_id}"
            ),
            "htmlUrl": f"https://github.com/{repository}/actions/runs/{run_id}",
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, stage_name
            ),
            "dispatchAttemptIdentity": recovered_attempt,
        }
    coordinate_name = (
        f"dispatch-coordinate-{stage_name}-{dispatch['workflowRunId']}.json"
    )
    ensure_asset(
        store,
        repository,
        release_id,
        coordinate_name,
        dispatch_coordinates(stage, preparation_digest, dispatch),
    )
    _, run_attempt = wait_for_run(
        store, stage, request, dispatch, approved_actor_id
    )
    contents = receipt_from_artifact(
        store, stage, str(dispatch["workflowRunId"]), run_attempt
    )
    receipt = validate_receipt_bytes(
        contents, preparation, preparation_digest, stage_name
    )
    validate_receipt_run(
        store,
        receipt,
        stage,
        approved_actor_id,
        str(dispatch["workflowRunId"]),
        run_attempt,
    )
    ensure_asset(store, repository, release_id, own_asset, contents)
    write_exact(output, contents)
    return {
        "status": "PASS",
        "stage": stage_name,
        "mode": mode,
        "workflowRunId": dispatch["workflowRunId"],
        "receiptSha256": hashlib.sha256(contents).hexdigest(),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    start = subparsers.add_parser("prepare-start")
    start.add_argument("--release-id", type=int, required=True)
    start.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    start.add_argument("--output", type=Path, required=True)
    stage = subparsers.add_parser("stage")
    stage.add_argument("--preparation", type=Path, required=True)
    stage.add_argument("--stage", required=True)
    stage.add_argument("--approved-actor-id", required=True)
    stage.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    stage.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    token = os.environ.get(arguments.token_environment, "").strip()
    store = GitHubStore(token)
    if arguments.command == "prepare-start":
        result = prepare_start(store, arguments.release_id, arguments.output)
    else:
        result = run_stage(
            store,
            arguments.preparation,
            arguments.stage,
            arguments.approved_actor_id,
            arguments.output,
        )
    print("NETWASM_RELEASE_STAGE " + json.dumps(result, sort_keys=True), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

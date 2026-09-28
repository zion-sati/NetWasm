#!/usr/bin/env python3
"""Rehearse the coordinated release train without external side effects."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
from typing import Callable
from unittest.mock import patch
import urllib.request
import urllib.parse
import zipfile


ROOT = Path(__file__).resolve().parent
ACTOR_ID = 12345
COORDINATOR_RUN_ID = "9000"
CORE_REPOSITORY = "zion-sati/NetWasm"
REVISION = re.compile(r"[0-9a-f]{40}")
JOB = re.compile(r"^  ([a-z0-9-]+):\s*$")
STAGE_ARGUMENT = re.compile(r"--stage ([a-z0-9-]+)")
CORE_CONSUMED_FILES = (
    "eng/release-coordinator.py",
    "eng/release-orchestrator.py",
    "eng/release-preparation.py",
    "eng/release-receiver.py",
    "eng/release-rehearsal.py",
    "eng/release-train.py",
)
DOWNSTREAM_CONSUMED_FILES = {
    "zion-sati/NetWasm.Playground": (
        ".github/workflows/pages.yml",
        ".github/workflows/release.yml",
        "eng/actions-delivery.py",
        "eng/release-coordinator.py",
        "eng/delivery-receipt.py",
        "eng/release-train.py",
    ),
    "zion-sati/netwasm.com": (
        ".github/workflows/pages.yml",
        "eng/actions-delivery.py",
        "eng/release-coordinator.py",
        "eng/release-train.py",
        "eng/website-delivery.py",
    ),
}


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError(f"Cannot load rehearsal dependency: {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ORCHESTRATOR = load("rehearsal_orchestrator", ROOT / "release-orchestrator.py")
PREPARATION = load("rehearsal_preparation", ROOT / "release-preparation.py")
RECEIVER = load("rehearsal_receiver", ROOT / "release-receiver.py")
TRAIN = ORCHESTRATOR.TRAIN
COORDINATOR = ORCHESTRATOR.COORDINATOR


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=True, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def json_bytes(value: object) -> bytes:
    return (
        json.dumps(value, ensure_ascii=True, indent=2, sort_keys=True) + "\n"
    ).encode()


def sha256(contents: bytes) -> str:
    return hashlib.sha256(contents).hexdigest()


def load_revisions(path: Path, core_revision: str) -> dict[str, str]:
    pinned = TRAIN.read_json(path)
    if not isinstance(pinned, dict):
        raise ValueError("Rehearsal revisions must be a JSON object.")
    revisions = {str(key): str(value) for key, value in pinned.items()}
    expected = set(PREPARATION.REPOSITORIES) - {CORE_REPOSITORY}
    if set(revisions) != expected:
        raise ValueError(
            "Rehearsal revisions must contain exactly the four downstream "
            "repositories."
        )
    revisions[CORE_REPOSITORY] = core_revision
    for repository, revision in revisions.items():
        if REVISION.fullmatch(revision) is None:
            raise ValueError(
                f"Rehearsal revision for {repository} is not a full commit SHA."
            )
    return {
        repository: revisions[repository]
        for repository in PREPARATION.REPOSITORIES
    }


def git_revision(root: Path) -> str:
    result = subprocess.run(
        ["git", "-C", str(root), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def git_file(root: Path, revision: str, path: str) -> bytes | None:
    result = subprocess.run(
        ["git", "-C", str(root), "show", f"{revision}:{path}"],
        check=False,
        capture_output=True,
    )
    return result.stdout if result.returncode == 0 else None


def core_workflow_directory(root: Path) -> Path:
    public = root / ".github" / "workflows"
    if public.is_dir():
        return public
    return root / "scripts" / "oss-export" / "templates" / "netwasm" / ".github" / "workflows"


def source_file_evidence(
    *,
    repository: str,
    root: Path,
    revision: str,
    paths: tuple[str, ...],
    allow_dirty_development: bool,
) -> dict[str, object]:
    files = []
    dirty = []
    for path in paths:
        actual_path = root / path
        actual = actual_path.read_bytes() if actual_path.is_file() else None
        expected = git_file(root, revision, path)
        matches = actual is not None and expected == actual
        if not matches:
            dirty.append(path)
        files.append({
            "path": path,
            "sha256": sha256(actual) if actual is not None else None,
            "matchesPinnedCommit": matches,
        })
    if dirty and not allow_dirty_development:
        raise ValueError(
            f"Rehearsal consumed files for {repository} do not match the pinned "
            f"commit: {', '.join(dirty)}."
        )
    return {
        "repository": repository,
        "revision": revision,
        "mode": "development-dirty" if dirty else "pinned-clean",
        "files": files,
    }


def validate_source_revisions(
    revisions: dict[str, str],
    playground_root: Path,
    website_root: Path,
    *,
    allow_dirty_development: bool,
) -> list[dict[str, object]]:
    roots = {
        CORE_REPOSITORY: ROOT.parent,
        "zion-sati/NetWasm.Playground": playground_root,
        "zion-sati/netwasm.com": website_root,
    }
    evidence = []
    for repository, root in roots.items():
        root = root.resolve()
        actual = git_revision(root)
        expected = revisions[repository]
        if actual != expected:
            raise ValueError(
                f"Rehearsal source checkout for {repository} is {actual}, "
                f"expected {expected}."
            )
        if repository == CORE_REPOSITORY:
            workflow_root = core_workflow_directory(root).relative_to(root).as_posix()
            paths = CORE_CONSUMED_FILES + (
                f"{workflow_root}/coordinated-release.yml",
                f"{workflow_root}/release.yml",
            )
        else:
            paths = DOWNSTREAM_CONSUMED_FILES[repository]
        evidence.append(source_file_evidence(
            repository=repository,
            root=root,
            revision=expected,
            paths=paths,
            allow_dirty_development=allow_dirty_development,
        ))
    return evidence


def workflow_jobs(contents: str) -> tuple[list[str], dict[str, str]]:
    jobs = []
    needs = {}
    current = None
    in_jobs = False
    for line in contents.splitlines():
        if line == "jobs:":
            in_jobs = True
            continue
        if not in_jobs:
            continue
        match = JOB.fullmatch(line)
        if match is not None:
            current = match.group(1)
            jobs.append(current)
            continue
        if current is not None and line.startswith("    needs:"):
            needs[current] = line.split(":", 1)[1].strip()
    return jobs, needs


def workflow_run_name(contents: str) -> str | None:
    for line in contents.splitlines():
        if line.startswith("run-name:"):
            return line.split(":", 1)[1].strip()
    return None


def require_workflow_contract(
    *,
    path: Path,
    expected_run_name: str | None,
    expected_jobs: list[str],
    expected_needs: dict[str, str],
    required_fragments: tuple[str, ...],
) -> dict[str, object]:
    contents = path.read_text(encoding="utf-8")
    actual_run_name = workflow_run_name(contents)
    jobs, needs = workflow_jobs(contents)
    if actual_run_name != expected_run_name:
        raise ValueError(f"Workflow run name changed: {path.name}.")
    if jobs != expected_jobs:
        raise ValueError(f"Workflow job inventory changed: {path.name}.")
    if any(needs.get(job) != value for job, value in expected_needs.items()):
        raise ValueError(f"Workflow job dependency changed: {path.name}.")
    missing = [fragment for fragment in required_fragments if fragment not in contents]
    if missing:
        raise ValueError(f"Workflow delivery contract changed: {path.name}.")
    return {
        "path": path.name,
        "sha256": sha256(contents.encode()),
        "runName": actual_run_name,
        "jobs": jobs,
        "needs": needs,
    }


def validate_workflow_contracts(
    playground_root: Path, website_root: Path
) -> dict[str, object]:
    core = core_workflow_directory(ROOT.parent)
    coordinator_contents = (core / "coordinated-release.yml").read_text(encoding="utf-8")
    stage_order = STAGE_ARGUMENT.findall(coordinator_contents)
    expected_stage_order = [item[0] for item in TRAIN.PREPARATION_STAGES]
    if stage_order != expected_stage_order:
        raise ValueError("Coordinated release workflow stage order changed.")
    core_release = require_workflow_contract(
        path=core / "release.yml",
        expected_run_name="Release ${{ inputs.coordinated_stage }} ${{ inputs.stage_identity }}",
        expected_jobs=["build", "host-tools", "aggregate", "publish"],
        expected_needs={
            "host-tools": "build",
            "aggregate": "[build, host-tools]",
            "publish": "[build, aggregate]",
        },
        required_fragments=("eng/release-receiver.py", "eng/release-train.py"),
    )
    playground_release = require_workflow_contract(
        path=playground_root / ".github/workflows/release.yml",
        expected_run_name="Release ${{ inputs.coordinated_stage }} ${{ inputs.stage_identity }}",
        expected_jobs=["receive", "toolchain-candidate", "pages"],
        expected_needs={
            "toolchain-candidate": "receive",
            "pages": "[receive, toolchain-candidate]",
        },
        required_fragments=(
            "--name toolchain-candidate",
            "delivery-candidate-receipt-playground-toolchain-candidate-",
        ),
    )
    playground_pages = require_workflow_contract(
        path=playground_root / ".github/workflows/pages.yml",
        expected_run_name=None,
        expected_jobs=[
            "prepare-site", "stage-site", "predeploy", "deploy",
            "bind-deployment", "verify-live", "complete-delivery",
        ],
        expected_needs={
            "stage-site": "prepare-site",
            "predeploy": "[prepare-site, stage-site]",
            "deploy": "[prepare-site, stage-site, predeploy]",
            "bind-deployment": "[prepare-site, deploy]",
            "verify-live": "[prepare-site, deploy, bind-deployment]",
            "complete-delivery": "[prepare-site, deploy, bind-deployment, verify-live]",
        },
        required_fragments=(
            "--name 'pages / prepare-site'",
            "--job-name 'pages / deploy'",
            "--producer-job-name 'pages / complete-delivery'",
            "--live-job-prefix 'pages / verify-live'",
            "delivery-evidence-playground-",
            "delivery-completion-playground-",
        ),
    )
    website = require_workflow_contract(
        path=website_root / ".github/workflows/pages.yml",
        expected_run_name="Release website ${{ inputs.stage_identity }}",
        expected_jobs=[
            "receive", "site-candidate", "stage-site", "predeploy", "deploy",
            "bind-deployment", "verify-live", "complete-delivery",
        ],
        expected_needs={
            "site-candidate": "receive",
            "stage-site": "[receive, site-candidate]",
            "predeploy": "[receive, site-candidate, stage-site]",
            "deploy": "[stage-site, predeploy]",
            "bind-deployment": "[receive, deploy]",
            "verify-live": "[receive, site-candidate, deploy, bind-deployment]",
            "complete-delivery": "[receive, site-candidate, bind-deployment, verify-live]",
        },
        required_fragments=(
            "--name site-candidate",
            "--job-name deploy",
            "--producer-job-name complete-delivery",
            "--live-job-name verify-live",
            "delivery-evidence-website-",
        ),
    )
    return {
        "coreStageOrder": stage_order,
        "coreRelease": core_release,
        "playgroundRelease": playground_release,
        "playgroundPages": playground_pages,
        "websitePages": website,
    }


def artifact_zip(name: str, contents: bytes) -> bytes:
    result = io.BytesIO()
    with zipfile.ZipFile(result, "w", compression=zipfile.ZIP_STORED) as archive:
        info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
        info.external_attr = 0o100644 << 16
        archive.writestr(info, contents)
    return result.getvalue()


def artifact_member(contents: bytes, name: str) -> bytes:
    with zipfile.ZipFile(io.BytesIO(contents)) as archive:
        if archive.namelist() != [name]:
            raise ValueError("Rehearsal artifact inventory changed.")
        return archive.read(name)


class JsonResponse(io.BytesIO):
    def __init__(self, value: object, status: int = 200) -> None:
        super().__init__(json.dumps(value, separators=(",", ":")).encode())
        self.status = status

    def __enter__(self):
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


class RehearsalStore:
    """Stateful in-memory implementation of the orchestrator effect boundary."""

    def __init__(self, transcript: list[dict[str, object]]) -> None:
        self.token = "rehearsal-token"
        self.transcript = transcript
        self.releases: dict[tuple[str, int], dict[str, object]] = {}
        self.release_assets: dict[tuple[str, int, str], bytes] = {}
        self.tags: dict[tuple[str, str], str] = {}
        self.runs: dict[tuple[str, int], dict[str, object]] = {}
        self.run_attempts: dict[tuple[str, int, int], dict[str, object]] = {}
        self.jobs: dict[tuple[str, int, int], list[dict[str, object]]] = {}
        self.artifacts: dict[tuple[str, int], list[dict[str, object]]] = {}
        self.artifact_contents: dict[tuple[str, int], bytes] = {}
        self.deployments: dict[tuple[str, int], dict[str, object]] = {}
        self.deployment_status_values: dict[
            tuple[str, int], list[dict[str, object]]
        ] = {}
        self.hidden_run_queries: dict[tuple[str, int], int] = {}
        self.next_run_id = 10000
        self.next_artifact_id = 20000
        self.next_job_id = 30000
        self.next_deployment_id = 40000
        self.publish_count = 0
        self.dispatch_count = 0
        self.package_push_count = 0
        self.build_count = 0
        self.deploy_count = 0
        self.attachment_fault_receipt: str | None = None
        self.attachment_fault_prerequisite: str | None = None
        self.attachment_faulted = False

    def event(self, event_name: str, **values: object) -> None:
        self.transcript.append({"event": event_name, **values})

    def release(self, repository: str, release_id: int) -> dict[str, object]:
        return self.releases[(repository, release_id)]

    def asset_bytes(
        self, repository: str, release_id: int, name: str
    ) -> bytes | None:
        return self.release_assets.get((repository, release_id, name))

    def upload_asset(
        self, repository: str, release_id: int, name: str, contents: bytes
    ) -> None:
        key = (repository, release_id, name)
        if key in self.release_assets and self.release_assets[key] != contents:
            raise ValueError(f"Rehearsal asset replacement attempted: {name}.")
        prerequisite = self.attachment_fault_prerequisite
        if (
            name == self.attachment_fault_receipt
            and not self.attachment_faulted
            and prerequisite is not None
            and (repository, release_id, prerequisite) in self.release_assets
        ):
            self.attachment_faulted = True
            self.event(
                "simulated-release-attachment-interruption",
                repository=repository,
                releaseId=release_id,
                archive=prerequisite,
                receipt=name,
            )
            raise RuntimeError("simulated release attachment interruption")
        self.release_assets[key] = contents
        self.event(
            "release-asset",
            repository=repository,
            releaseId=release_id,
            name=name,
            sha256=sha256(contents),
        )

    def release_asset_bytes_bounded(
        self,
        repository: str,
        release_id: int,
        asset_name: str,
        maximum_bytes: int,
    ) -> bytes | None:
        contents = self.asset_bytes(repository, release_id, asset_name)
        if contents is not None and len(contents) > maximum_bytes:
            raise ValueError("Rehearsal release asset exceeds its byte limit.")
        return contents

    def assets(self, repository: str, release_id: int) -> list[dict[str, object]]:
        return [
            {"id": index, "name": name}
            for index, (candidate_repository, candidate_release_id, name)
            in enumerate(self.release_assets, 1)
            if candidate_repository == repository
            and candidate_release_id == release_id
        ]

    def tag_commit(self, repository: str, tag: str) -> str | None:
        return self.tags.get((repository, tag))

    def publish_release(
        self, repository: str, release_id: int
    ) -> dict[str, object]:
        release = self.releases[(repository, release_id)]
        release["draft"] = False
        self.tags[(repository, str(release["tag_name"]))] = str(
            release["target_commitish"]
        )
        self.publish_count += 1
        self.event(
            "simulated-release-publication",
            repository=repository,
            releaseId=release_id,
        )
        return release

    def workflow_runs(
        self, repository: str, workflow: str, ref: str
    ) -> list[dict[str, object]]:
        result = []
        for (candidate_repository, run_id), value in self.runs.items():
            if (
                candidate_repository != repository
                or value.get("path") != workflow
                or value.get("head_branch") != ref
            ):
                continue
            key = (repository, run_id)
            remaining = self.hidden_run_queries.get(key, 0)
            if remaining:
                self.hidden_run_queries[key] = remaining - 1
                self.event(
                    "workflow-run-not-yet-visible",
                    repository=repository,
                    runId=run_id,
                )
                continue
            result.append(value)
        return result

    def workflow_run(self, repository: str, run_id: str) -> dict[str, object]:
        return self.runs[(repository, int(run_id))]

    def workflow_run_attempt(
        self, repository: str, run_id: int, run_attempt: int
    ) -> dict[str, object]:
        return self.run_attempts[(repository, run_id, run_attempt)]

    def workflow_jobs(
        self, repository: str, run_id: int, run_attempt: int
    ) -> list[dict[str, object]]:
        return self.jobs[(repository, run_id, run_attempt)]

    def run_artifacts(
        self, repository: str, run_id: str
    ) -> list[dict[str, object]]:
        return self.artifacts.get((repository, int(run_id)), [])

    def artifact_bytes(self, repository: str, artifact_id: int) -> bytes:
        return self.artifact_contents[(repository, artifact_id)]

    def artifact_bytes_bounded(
        self, repository: str, artifact_id: int, maximum_bytes: int
    ) -> bytes:
        contents = self.artifact_bytes(repository, artifact_id)
        if len(contents) > maximum_bytes:
            raise ValueError("Rehearsal artifact exceeds its byte limit.")
        return contents

    def deployment(
        self, repository: str, deployment_id: int
    ) -> dict[str, object]:
        return self.deployments[(repository, deployment_id)]

    def deployment_statuses(
        self, repository: str, deployment_id: int
    ) -> list[dict[str, object]]:
        return self.deployment_status_values[(repository, deployment_id)]

    def add_artifact(
        self,
        repository: str,
        run: dict[str, object],
        name: str,
        file_name: str,
        contents: bytes,
    ) -> int:
        artifact_id = self.next_artifact_id
        self.next_artifact_id += 1
        run_id = int(run["id"])
        artifact = {
            "id": artifact_id,
            "name": name,
            "expired": False,
            "workflow_run": {
                "id": run_id,
                "head_sha": run["head_sha"],
                "head_branch": run["head_branch"],
            },
        }
        self.artifacts.setdefault((repository, run_id), []).append(artifact)
        self.artifact_contents[(repository, artifact_id)] = artifact_zip(
            file_name, contents
        )
        self.event(
            "workflow-artifact",
            repository=repository,
            runId=run_id,
            artifactId=artifact_id,
            name=name,
            sha256=sha256(contents),
        )
        return artifact_id

    def add_job(
        self, repository: str, run: dict[str, object], name: str
    ) -> int:
        job_id = self.next_job_id
        self.next_job_id += 1
        run_id = int(run["id"])
        run_attempt = int(run["run_attempt"])
        self.jobs.setdefault((repository, run_id, run_attempt), []).append({
            "id": job_id,
            "name": name,
            "run_id": run_id,
            "run_attempt": run_attempt,
            "status": "completed",
            "conclusion": "success",
            "head_sha": run["head_sha"],
        })
        return job_id


class ReleaseRehearsal:
    def __init__(
        self,
        *,
        revisions: dict[str, str],
        playground_root: Path,
        website_root: Path,
        failure_stage: str | None = None,
        lost_response_stage: str | None = None,
        delivery_interruption: str | None = None,
        delayed_visibility: bool = False,
        attachment_interruption: bool = False,
    ) -> None:
        self.revisions = revisions
        self.playground = load(
            "rehearsal_playground_delivery",
            playground_root / "eng/delivery-receipt.py",
        )
        self.website = load(
            "rehearsal_website_delivery",
            website_root / "eng/website-delivery.py",
        )
        self.playground_actions = load(
            "rehearsal_playground_actions_delivery",
            playground_root / "eng/actions-delivery.py",
        )
        self.website_actions = load(
            "rehearsal_website_actions_delivery",
            website_root / "eng/actions-delivery.py",
        )
        self.transcript: list[dict[str, object]] = []
        self.store = RehearsalStore(self.transcript)
        self.failure_stage = failure_stage
        self.lost_response_stage = lost_response_stage
        self.delivery_interruption = delivery_interruption
        self.delayed_visibility = delayed_visibility
        self.attachment_interruption = attachment_interruption
        self.failed_once: set[str] = set()
        self.lost_once: set[str] = set()
        self.receipts: dict[str, bytes] = {}
        self.candidates: dict[str, dict[str, object]] = {}
        self.candidate_history: list[dict[str, object]] = []
        self.partial_candidates: list[dict[str, object]] = []
        self.effect_guards_verified = False
        self.real_dispatch_workflow = COORDINATOR.dispatch_workflow
        if attachment_interruption:
            self.store.attachment_fault_receipt = (
                ORCHESTRATOR.candidate_receipt_asset_name(
                    "playground-toolchain-candidate"
                )
            )
            self.store.attachment_fault_prerequisite = "toolchain.tar.gz"
        self.root_context = tempfile.TemporaryDirectory()
        self.root = Path(self.root_context.name)
        self.preparation_path = self.root / "release-preparation.json"
        self.preparation = self._create_preparation()
        write_json(self.preparation_path, self.preparation)
        self.preparation_bytes = self.preparation_path.read_bytes()
        _, self.preparation_digest = TRAIN.read_preparation(self.preparation_path)
        self._seed_releases()

    def close(self) -> None:
        self.root_context.cleanup()

    def _create_preparation(self) -> dict[str, object]:
        if set(self.revisions) != set(PREPARATION.REPOSITORIES):
            raise ValueError("Rehearsal revisions do not cover every repository.")
        if any(TRAIN.COMMIT.fullmatch(value) is None for value in self.revisions.values()):
            raise ValueError("Rehearsal revision is invalid.")
        repositories = {
            repository: {
                "sourceCommit": commit,
                "infrastructureCommit": commit,
                "workflowCommit": commit,
                "workflowRef": "main",
            }
            for repository, commit in self.revisions.items()
        }
        coordinates = {
            "schemaVersion": 1,
            "version": "0.5.0",
            "repositories": repositories,
        }
        release_ids = {
            name: 1001 + index
            for index, name in enumerate(PREPARATION.PACKAGE_STAGE_NAMES)
        }
        return PREPARATION.create_preparation(coordinates, release_ids)

    def _seed_releases(self) -> None:
        for stage in self.preparation["stages"]:
            release_id = stage["releaseId"]
            if release_id is None:
                continue
            repository = str(stage["repository"])
            key = (repository, int(release_id))
            core_preview = stage["name"] == "core-preview"
            self.store.releases[key] = {
                "id": release_id,
                "tag_name": stage["ref"],
                "target_commitish": stage["sourceCommit"],
                "draft": not core_preview,
                "prerelease": stage["prerelease"],
            }
            self.store.release_assets[
                (*key, PREPARATION.PREPARATION_ASSET)
            ] = self.preparation_bytes
            if core_preview:
                self.store.tags[(repository, str(stage["ref"]))] = str(
                    stage["sourceCommit"]
                )

    def _stage(self, name: str) -> dict[str, object]:
        return TRAIN.preparation_stage(self.preparation, name)

    def _run(self, stage_name: str, conclusion: str = "success") -> dict[str, object]:
        stage = self._stage(stage_name)
        run_id = self.store.next_run_id
        self.store.next_run_id += 1
        run = {
            "id": run_id,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "display_title": ORCHESTRATOR.expected_run_title(
                self.preparation_digest, stage_name
            ),
            "status": "completed",
            "conclusion": conclusion,
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": ACTOR_ID},
        }
        repository = str(stage["repository"])
        self.store.runs[(repository, run_id)] = run
        self.store.run_attempts[(repository, run_id, 1)] = run
        self.store.jobs[(repository, run_id, 1)] = []
        self.store.dispatch_count += 1
        self.store.event(
            "workflow-dispatch",
            stage=stage_name,
            repository=repository,
            runId=run_id,
            conclusion=conclusion,
        )
        return run

    def _receiver_inputs(
        self,
        request: dict[str, object],
        run: dict[str, object],
    ) -> tuple[Path, Path]:
        stage_name = str(request["inputs"]["coordinated_stage"])
        stage = self._stage(stage_name)
        directory = self.root / "receivers" / f"{stage_name}-{run['id']}"
        upstream_directory = directory / "upstream"
        run_directory = directory / "upstream-runs"
        upstream_directory.mkdir(parents=True)
        run_directory.mkdir(parents=True)
        inputs_path = directory / "inputs.json"
        write_json(inputs_path, request["inputs"])
        coordinates = json.loads(str(request["inputs"]["upstream_receipts"]))
        for coordinate in coordinates:
            upstream_stage_name = str(coordinate["stage"])
            contents = self.receipts[upstream_stage_name]
            if sha256(contents) != coordinate["sha256"]:
                raise ValueError("Rehearsal upstream receipt coordinate changed.")
            (upstream_directory / str(coordinate["fileName"])).write_bytes(contents)
            receipt = json.loads(contents)
            package_stage = upstream_stage_name in {
                item[0] for item in TRAIN.PREPARATION_STAGES[:6]
            }
            producer = receipt["publication" if package_stage else "producer"]
            upstream_stage = self._stage(upstream_stage_name)
            repository = str(upstream_stage["repository"])
            upstream_run = self.store.workflow_run_attempt(
                repository,
                int(producer["runId"]),
                int(producer["runAttempt"]),
            )
            write_json(run_directory / f"{upstream_stage_name}.json", upstream_run)
            if not package_stage:
                write_json(
                    run_directory / f"{upstream_stage_name}-jobs.json",
                    {"jobs": self.store.workflow_jobs(
                        repository,
                        int(producer["runId"]),
                        int(producer["runAttempt"]),
                    )},
                )
        anchor_repository, anchor_release_id = TRAIN.stage_state_anchor(
            self.preparation, stage_name
        )
        release = self.store.release(anchor_repository, anchor_release_id)
        release_path = directory / "release.json"
        write_json(release_path, release)
        tag_commit = self.store.tag_commit(
            anchor_repository, str(release["tag_name"])
        ) or ""
        result = RECEIVER.verify_receiver(
            inputs_path=inputs_path,
            preparation_path=self.preparation_path,
            release_path=release_path,
            upstream_directory=upstream_directory,
            upstream_run_directory=run_directory,
            repository=str(stage["repository"]),
            event_name="workflow_dispatch",
            workflow_identity=(
                f"{stage['repository']}/{stage['workflow']}@refs/heads/"
                f"{stage['workflowRef']}"
            ),
            workflow_sha=str(stage["workflowCommit"]),
            workflow_ref=str(stage["workflowRef"]),
            actor_id=str(ACTOR_ID),
            approved_actor_id=str(ACTOR_ID),
            tag_commit=tag_commit,
        )
        self.store.event(
            "receiver-pass",
            stage=stage_name,
            runId=run["id"],
            preparationSha256=result["preparationSha256"],
        )
        return inputs_path, upstream_directory

    def _package_train(
        self, stage_name: str
    ) -> tuple[Path, Path]:
        stage = self._stage(stage_name)
        repository = str(stage["repository"])
        preview_name = next(
            name
            for name, candidate_repository, *_ in TRAIN.PREPARATION_STAGES[:6]
            if candidate_repository == repository and name.endswith("-preview")
        )
        stable_name = next(
            name
            for name, candidate_repository, *_ in TRAIN.PREPARATION_STAGES[:6]
            if candidate_repository == repository and name.endswith("-stable")
        )
        preview_stage = self._stage(preview_name)
        stable_stage = self._stage(stable_name)
        repository_slug = repository.rsplit("/", 1)[1].replace(".", "-").lower()
        package_id = f"Rehearsal.{repository_slug}"

        def package(version: str, digit: str) -> dict[str, object]:
            return {
                "id": package_id,
                "version": version,
                "fileName": f"{package_id}.{version}.nupkg",
                "size": 123,
                "sha256": digit * 64,
            }

        def candidate(
            version: str, tag: str, digit: str
        ) -> dict[str, object]:
            return {
                "version": version,
                "sourceCommit": stage["sourceCommit"],
                "producingReleaseTag": preview_stage["ref"],
                "bundleFile": f"{repository_slug}-{digit}.zip",
                "bundleSize": 456,
                "bundleSha256": digit * 64,
                "manifestSha256": str(int(digit) + 1) * 64,
                "receiptSha256": str(int(digit) + 2) * 64,
                "packages": [package(version, str(int(digit) + 3))],
                "releaseTag": tag,
            }

        train = {
            "schemaVersion": 2,
            "repository": repository,
            "sourceCommit": stage["sourceCommit"],
            "producingReleaseTag": preview_stage["ref"],
            "workflowRunId": "8000",
            "workflowRunAttempt": "1",
            "artifactName": f"{repository_slug}-release-train",
            "toolchainSha256": "1" * 64,
            "nativeToolchain": {"emscripten": "6.0.7"},
            "preparationSha256": self.preparation_digest,
            "infrastructureCommit": stage["infrastructureCommit"],
            "preview": candidate(
                "0.5.0-preview.1", str(preview_stage["ref"]), "2"
            ),
            "stable": candidate("0.5.0", str(stable_stage["ref"]), "5"),
        }
        selected = train["preview" if stage_name.endswith("-preview") else "stable"]
        selected_package = selected["packages"][0]
        feed = {
            "schemaVersion": 1,
            "status": "PASS",
            "repository": repository,
            "releaseVersion": selected["version"],
            "releaseTag": stage["ref"],
            "sourceCommit": stage["sourceCommit"],
            "feed": "https://api.nuget.org/v3/index.json",
            "packages": [{
                "id": selected_package["id"],
                "version": selected["version"],
                "fileName": selected_package["fileName"],
                "candidateSha256": selected_package["sha256"],
                "normalizedPayloadSha256": "9" * 64,
            }],
        }
        directory = self.root / "package-inputs" / stage_name
        train_path = directory / "release-train.json"
        feed_path = directory / "feed-receipt.json"
        write_json(train_path, train)
        write_json(feed_path, feed)
        return train_path, feed_path

    def _simulate_package(
        self,
        request: dict[str, object],
        run: dict[str, object],
        fail: bool,
    ) -> None:
        stage_name = str(request["inputs"]["coordinated_stage"])
        _, upstream_directory = self._receiver_inputs(request, run)
        if fail:
            return
        train_path, feed_path = self._package_train(stage_name)
        coordinates = json.loads(str(request["inputs"]["upstream_receipts"]))
        upstream_paths = [
            upstream_directory / str(item["fileName"])
            for item in coordinates
        ]
        receipt = TRAIN.create_publication_receipt(
            preparation_path=self.preparation_path,
            stage_name=stage_name,
            train_path=train_path,
            feed_receipt_path=feed_path,
            publication_run_id=str(run["id"]),
            publication_run_attempt=str(run["run_attempt"]),
            upstream_receipt_paths=upstream_paths,
            upstream_receipt_sha256=[sha256(path.read_bytes()) for path in upstream_paths],
        )
        contents = json_bytes(receipt)
        repository = str(request["repository"])
        self.store.add_artifact(
            repository,
            run,
            f"publication-receipt-{stage_name}-{run['id']}-{run['run_attempt']}",
            ORCHESTRATOR.RECEIPT_FILE,
            contents,
        )
        self.receipts[stage_name] = contents
        self.store.package_push_count += 1
        self.store.event(
            "simulated-package-publication",
            stage=stage_name,
            receiptSha256=sha256(contents),
        )

    def _candidate_artifact(
        self,
        *,
        stage_name: str,
        kind: str,
        request: dict[str, object],
        run: dict[str, object],
        inputs_path: Path,
        upstream_directory: Path,
        prior_receipt_path: Path | None = None,
    ) -> tuple[Path, bytes]:
        repository = str(request["repository"])
        run_id = int(run["id"])
        run_attempt = int(run["run_attempt"])
        directory = self.root / "delivery" / f"{stage_name}-{run_id}" / kind
        directory.mkdir(parents=True)
        producer_job_name = {
            "playground-toolchain-candidate": "toolchain-candidate",
            "playground-site-candidate": "pages / prepare-site",
            "website-site-candidate": "site-candidate",
        }[kind]
        self.store.add_job(repository, run, producer_job_name)
        jobs_path = directory / "jobs.json"
        write_json(jobs_path, {
            "jobs": self.store.workflow_jobs(repository, run_id, run_attempt)
        })
        actions = (
            self.playground_actions
            if stage_name == "playground"
            else self.website_actions
        )
        producer_job = actions.resolve_job(
            jobs_path,
            name=producer_job_name,
            run_id=run_id,
            run_attempt=run_attempt,
            head_sha=str(run["head_sha"]),
            require_success=False,
        )
        producer_job_id = int(producer_job["id"])
        archive_name = {
            "playground-toolchain-candidate": "toolchain.tar.gz",
            "playground-site-candidate": "playground-site.zip",
            "website-site-candidate": "website-site.zip",
        }[kind]
        archive_bytes = f"rehearsed {kind}\n".encode()
        payload_name = TRAIN.delivery_candidate_payload_artifact_name(
            kind, run_id, run_attempt
        )
        payload_id = self.store.add_artifact(
            repository, run, payload_name, archive_name, archive_bytes
        )
        archive_path = directory / archive_name
        archive_path.write_bytes(archive_bytes)
        if stage_name == "playground":
            toolchain = {
                "id": sha256(b"rehearsal toolchain"),
                "manifestSha256": sha256(b"rehearsal manifest"),
            }
            toolchain_path = directory / "toolchain-index.json"
            write_json(toolchain_path, toolchain)
            site_identity_path = None
            if kind == "playground-site-candidate":
                site_identity_path = directory / "site-identity.json"
                write_json(site_identity_path, {
                    "toolchain": toolchain,
                    "indexHtmlSha256": sha256(b"rehearsal playground index"),
                })
            receipt = self.playground.candidate_receipt(
                preparation_path=self.preparation_path,
                inputs_path=inputs_path,
                kind=kind,
                archive_path=archive_path,
                artifact_id=payload_id,
                run_id=run_id,
                run_attempt=run_attempt,
                job_id=producer_job_id,
                actor_id=ACTOR_ID,
                toolchain_index_path=toolchain_path,
                site_identity_path=site_identity_path,
                toolchain_receipt_path=prior_receipt_path,
            )
        else:
            site_identity_path = directory / "site-identity.json"
            write_json(site_identity_path, {
                "indexHtmlSha256": sha256(b"rehearsal website index"),
            })
            receipt = self.website.candidate_receipt(
                preparation_path=self.preparation_path,
                inputs_path=inputs_path,
                archive_path=archive_path,
                artifact_id=payload_id,
                run_id=run_id,
                run_attempt=run_attempt,
                job_id=producer_job_id,
                actor_id=ACTOR_ID,
                site_identity_path=site_identity_path,
            )
        receipt_bytes = json_bytes(receipt)
        receipt_path = directory / ORCHESTRATOR.CANDIDATE_RECEIPT_FILE
        receipt_path.write_bytes(receipt_bytes)
        self.store.add_artifact(
            repository,
            run,
            ORCHESTRATOR.candidate_receipt_artifact_name(
                kind, run_id, run_attempt
            ),
            ORCHESTRATOR.CANDIDATE_RECEIPT_FILE,
            receipt_bytes,
        )
        self.candidates[kind] = {
            "receipt": receipt_bytes,
            "archive": archive_bytes,
            "payloadArtifactId": payload_id,
        }
        self.candidate_history.append({
            "kind": kind,
            "runId": run_id,
            "producerJobId": producer_job_id,
            "payloadArtifactId": payload_id,
            "archiveSha256": sha256(archive_bytes),
            "receiptSha256": sha256(receipt_bytes),
        })
        self.store.build_count += 1
        self.store.event(
            "simulated-candidate-build",
            stage=stage_name,
            kind=kind,
            receiptSha256=sha256(receipt_bytes),
            archiveSha256=sha256(archive_bytes),
        )
        return receipt_path, receipt_bytes

    def _partial_candidate_payload(
        self,
        *,
        stage_name: str,
        kind: str,
        request: dict[str, object],
        run: dict[str, object],
    ) -> None:
        repository = str(request["repository"])
        run_id = int(run["id"])
        run_attempt = int(run["run_attempt"])
        archive_name = {
            "playground-toolchain-candidate": "toolchain.tar.gz",
            "website-site-candidate": "website-site.zip",
        }[kind]
        archive_bytes = f"interrupted {kind}\n".encode()
        payload_name = TRAIN.delivery_candidate_payload_artifact_name(
            kind, run_id, run_attempt
        )
        payload_id = self.store.add_artifact(
            repository, run, payload_name, archive_name, archive_bytes
        )
        partial = {
            "kind": kind,
            "runId": run_id,
            "payloadArtifactId": payload_id,
            "archiveFileName": archive_name,
            "archiveSha256": sha256(archive_bytes),
        }
        self.partial_candidates.append(partial)
        self.store.event("simulated-archive-before-receipt", **partial)

    def _deployment(
        self,
        stage_name: str,
        run: dict[str, object],
        deployment_job_id: int,
    ) -> dict[str, object]:
        stage = self._stage(stage_name)
        repository = str(stage["repository"])
        deployment_id = self.store.next_deployment_id
        self.store.next_deployment_id += 1
        url = (
            "https://playground.netwasm.com/"
            if stage_name == "playground"
            else "https://www.netwasm.com/"
        )
        receipt = {
            "id": deployment_id,
            "environment": "github-pages",
            "url": url,
            "runId": int(run["id"]),
            "runAttempt": int(run["run_attempt"]),
            "jobId": deployment_job_id,
        }
        self.store.deployments[(repository, deployment_id)] = {
            "id": deployment_id,
            "sha": stage["workflowCommit"],
            "ref": stage["workflowRef"],
            "task": "deploy",
            "environment": "github-pages",
            "creator": {"id": ACTOR_ID},
        }
        self.store.deployment_status_values[(repository, deployment_id)] = [{
            "state": "success",
            "environment": "github-pages",
            "environment_url": url,
            "log_url": (
                f"https://github.com/{repository}/actions/runs/{run['id']}"
                f"/job/{deployment_job_id}"
            ),
            "creator": {"id": ACTOR_ID},
        }]
        self.store.deploy_count += 1
        self.store.event(
            "simulated-pages-deployment",
            stage=stage_name,
            deploymentId=deployment_id,
            url=url,
        )
        directory = self.root / "delivery" / f"{stage_name}-{run['id']}" / "deployment-resolution"
        statuses = directory / "statuses"
        statuses.mkdir(parents=True)
        jobs_path = directory / "jobs.json"
        deployments_path = directory / "deployments.json"
        write_json(jobs_path, {"jobs": self.store.workflow_jobs(
            repository, int(run["id"]), int(run["run_attempt"])
        )})
        write_json(deployments_path, [self.store.deployments[(repository, deployment_id)]])
        write_json(
            statuses / f"{deployment_id}.json",
            self.store.deployment_status_values[(repository, deployment_id)],
        )
        actions = (
            self.playground_actions
            if stage_name == "playground"
            else self.website_actions
        )
        resolved = actions.deployment_record(
            jobs_path=jobs_path,
            deployments_path=deployments_path,
            statuses_directory=statuses,
            repository=repository,
            run_id=int(run["id"]),
            run_attempt=int(run["run_attempt"]),
            job_name="pages / deploy" if stage_name == "playground" else "deploy",
            head_sha=str(run["head_sha"]),
            workflow_ref=str(stage["workflowRef"]),
            actor_id=ACTOR_ID,
            expected_url=url,
        )
        if resolved != receipt:
            raise AssertionError("Actions deployment resolver changed coordinates.")
        return resolved

    def _evidence_artifact(
        self,
        *,
        stage_name: str,
        run: dict[str, object],
        job_id: int,
        evidence: dict[str, object],
        browser: str | None = None,
    ) -> dict[str, object]:
        repository = str(self._stage(stage_name)["repository"])
        contents = json_bytes(evidence)
        name = TRAIN.delivery_evidence_artifact_name(
            f"{stage_name}-completion",
            int(run["id"]),
            int(run["run_attempt"]),
            job_id,
            browser=browser,
        )
        artifact_id = self.store.add_artifact(
            repository, run, name, "delivery-evidence.json", contents
        )
        return {
            "artifactId": artifact_id,
            "artifactName": name,
            "fileName": "delivery-evidence.json",
            "sha256": sha256(contents),
        }

    def _playground_completion(
        self,
        request: dict[str, object],
        run: dict[str, object],
        inputs_path: Path,
    ) -> bytes:
        repository = str(request["repository"])
        run_id = int(run["id"])
        run_attempt = int(run["run_attempt"])
        directory = self.root / "delivery" / f"playground-{run_id}" / "completion"
        directory.mkdir(parents=True)
        toolchain_path = directory / "toolchain-receipt.json"
        site_path = directory / "site-receipt.json"
        toolchain_path.write_bytes(
            self.candidates["playground-toolchain-candidate"]["receipt"]
        )
        site_path.write_bytes(self.candidates["playground-site-candidate"]["receipt"])
        site_receipt = json.loads(site_path.read_bytes())
        site = site_receipt["site"]
        toolchain = site_receipt["toolchain"]
        deployment_job_id = self.store.add_job(repository, run, "pages / deploy")
        deployment = self._deployment("playground", run, deployment_job_id)
        deployment_path = directory / "deployment.json"
        write_json(deployment_path, deployment)
        evidence_directory = directory / "evidence"
        for browser in ("chromium", "firefox", "webkit"):
            job_id = self.store.add_job(
                repository, run, f"pages / verify-live ({browser})"
            )
            evidence = self.playground.live_evidence(
                preparation_path=self.preparation_path,
                inputs_path=inputs_path,
                deployment_path=deployment_path,
                browser=browser,
                run_id=run_id,
                run_attempt=run_attempt,
                job_id=job_id,
                site_identity_sha256=site["identitySha256"],
                toolchain_id=toolchain["id"],
                toolchain_manifest_sha256=toolchain["manifestSha256"],
            )
            evidence_path = evidence_directory / browser / "delivery-evidence.json"
            write_json(evidence_path, evidence)
            self._evidence_artifact(
                stage_name="playground",
                run=run,
                job_id=job_id,
                evidence=evidence,
                browser=browser,
            )
        self.store.add_job(repository, run, "pages / complete-delivery")
        metadata_path = directory / "completion-metadata.json"
        jobs_path = directory / "jobs.json"
        artifacts_path = directory / "artifacts.json"
        write_json(jobs_path, {"jobs": self.store.workflow_jobs(
            repository, run_id, run_attempt
        )})
        write_json(artifacts_path, {"artifacts": self.store.run_artifacts(
            repository, str(run_id)
        )})
        metadata = self.playground_actions.completion_metadata(
            jobs_path=jobs_path,
            artifacts_path=artifacts_path,
            evidence_directory=evidence_directory,
            deployment_path=deployment_path,
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            producer_job_name="pages / complete-delivery",
            live_job_prefix="pages / verify-live",
            head_sha=str(run["head_sha"]),
            dispatch_attempt_identity=str(
                request["inputs"]["dispatch_attempt_identity"]
            ),
            site_identity_sha256=str(site["identitySha256"]),
            toolchain_id=str(toolchain["id"]),
            toolchain_manifest_sha256=str(toolchain["manifestSha256"]),
        )
        write_json(metadata_path, metadata)
        receipt = self.playground.completion_receipt(
            preparation_path=self.preparation_path,
            inputs_path=inputs_path,
            metadata_path=metadata_path,
            toolchain_receipt_path=toolchain_path,
            site_receipt_path=site_path,
            run_id=run_id,
            run_attempt=run_attempt,
            actor_id=ACTOR_ID,
        )
        return json_bytes(receipt)

    def _website_completion(
        self,
        request: dict[str, object],
        run: dict[str, object],
        inputs_path: Path,
        upstream_directory: Path,
    ) -> bytes:
        repository = str(request["repository"])
        run_id = int(run["id"])
        run_attempt = int(run["run_attempt"])
        directory = self.root / "delivery" / f"website-{run_id}" / "completion"
        directory.mkdir(parents=True)
        site_path = directory / "site-receipt.json"
        site_path.write_bytes(self.candidates["website-site-candidate"]["receipt"])
        site_receipt = json.loads(site_path.read_bytes())
        deployment_job_id = self.store.add_job(repository, run, "deploy")
        deployment = self._deployment("website", run, deployment_job_id)
        deployment_path = directory / "deployment.json"
        write_json(deployment_path, deployment)
        live_job_id = self.store.add_job(repository, run, "verify-live")
        playground_digest = sha256(self.receipts["playground"])
        evidence = self.website.live_evidence(
            preparation_path=self.preparation_path,
            inputs_path=inputs_path,
            deployment_path=deployment_path,
            run_id=run_id,
            run_attempt=run_attempt,
            job_id=live_job_id,
            site_identity_sha256=site_receipt["site"]["identitySha256"],
            playground_completion_sha256=playground_digest,
        )
        evidence_path = directory / "evidence" / "delivery-evidence.json"
        write_json(evidence_path, evidence)
        self._evidence_artifact(
            stage_name="website",
            run=run,
            job_id=live_job_id,
            evidence=evidence,
        )
        self.store.add_job(repository, run, "complete-delivery")
        metadata_path = directory / "completion-metadata.json"
        jobs_path = directory / "jobs.json"
        artifacts_path = directory / "artifacts.json"
        write_json(jobs_path, {"jobs": self.store.workflow_jobs(
            repository, run_id, run_attempt
        )})
        write_json(artifacts_path, {"artifacts": self.store.run_artifacts(
            repository, str(run_id)
        )})
        metadata = self.website_actions.completion_metadata(
            jobs_path=jobs_path,
            artifacts_path=artifacts_path,
            evidence_path=evidence_path,
            deployment_path=deployment_path,
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            producer_job_name="complete-delivery",
            live_job_name="verify-live",
            head_sha=str(run["head_sha"]),
            dispatch_attempt_identity=str(
                request["inputs"]["dispatch_attempt_identity"]
            ),
            site_identity_sha256=str(site_receipt["site"]["identitySha256"]),
            playground_completion_sha256=playground_digest,
        )
        write_json(metadata_path, metadata)
        receipt = self.website.completion_receipt(
            preparation_path=self.preparation_path,
            inputs_path=inputs_path,
            metadata_path=metadata_path,
            site_receipt_path=site_path,
            upstream_directory=upstream_directory,
            run_id=run_id,
            run_attempt=run_attempt,
            actor_id=ACTOR_ID,
        )
        return json_bytes(receipt)

    def _simulate_delivery(
        self,
        request: dict[str, object],
        run: dict[str, object],
        fail: bool,
    ) -> None:
        stage_name = str(request["inputs"]["coordinated_stage"])
        inputs_path, upstream_directory = self._receiver_inputs(request, run)
        kinds = ORCHESTRATOR.delivery_candidate_kinds(stage_name)
        retained = json.loads(str(request["inputs"]["retained_candidates"]))
        retained_kinds = {
            str(item["kind"]): str(item["receiptSha256"])
            for item in retained
        }
        if retained:
            expected = {
                kind: sha256(self.candidates[kind]["receipt"])
                for kind in retained_kinds
                if kind in self.candidates
            }
            if retained_kinds != expected:
                raise ValueError("Retained rehearsal candidate coordinates changed.")
            for kind, digest in retained_kinds.items():
                retained_receipt = json.loads(self.candidates[kind]["receipt"])
                self.store.event(
                    "retained-candidate-reused",
                    stage=stage_name,
                    kind=kind,
                    payloadArtifactId=self.candidates[kind]["payloadArtifactId"],
                    producerJobId=retained_receipt["producer"]["jobId"],
                    receiptSha256=digest,
                    archiveSha256=sha256(self.candidates[kind]["archive"]),
                )
        if fail and self.delivery_interruption == "archive-before-receipt":
            self._partial_candidate_payload(
                stage_name=stage_name,
                kind=kinds[0],
                request=request,
                run=run,
            )
            return
        prior = None
        for index, kind in enumerate(kinds):
            if kind in retained_kinds:
                prior = self.root / "retained" / f"{kind}.json"
                prior.parent.mkdir(parents=True, exist_ok=True)
                prior.write_bytes(self.candidates[kind]["receipt"])
                continue
            prior, _ = self._candidate_artifact(
                stage_name=stage_name,
                kind=kind,
                request=request,
                run=run,
                inputs_path=inputs_path,
                upstream_directory=upstream_directory,
                prior_receipt_path=prior,
            )
            if (
                fail
                and self.delivery_interruption == "toolchain-only"
                and stage_name == "playground"
                and index == 0
            ):
                return
        if fail:
            return
        completion = (
            self._playground_completion(request, run, inputs_path)
            if stage_name == "playground"
            else self._website_completion(
                request, run, inputs_path, upstream_directory
            )
        )
        repository = str(request["repository"])
        self.store.add_artifact(
            repository,
            run,
            ORCHESTRATOR.completion_receipt_artifact_name(
                stage_name, int(run["id"]), int(run["run_attempt"])
            ),
            ORCHESTRATOR.COMPLETION_RECEIPT_FILE,
            completion,
        )
        self.receipts[stage_name] = completion
        self.store.event(
            "delivery-completion",
            stage=stage_name,
            receiptSha256=sha256(completion),
        )

    def _simulate_dispatch(
        self,
        request: dict[str, object],
    ) -> dict[str, object]:
        COORDINATOR.validate_dispatch_request(request, self.preparation_path)
        stage_name = str(request["inputs"]["coordinated_stage"])
        fail = self.failure_stage == stage_name and stage_name not in self.failed_once
        if fail:
            self.failed_once.add(stage_name)
        run = self._run(stage_name, "failure" if fail else "success")
        try:
            if stage_name in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}:
                self._simulate_package(request, run, fail)
            else:
                self._simulate_delivery(request, run, fail)
        except Exception as error:
            self.store.event(
                "rehearsal-producer-error",
                stage=stage_name,
                error=f"{type(error).__name__}: {error}",
            )
            raise
        result = {
            "workflowRunId": str(run["id"]),
            "runUrl": (
                f"https://api.github.com/repos/{request['repository']}"
                f"/actions/runs/{run['id']}"
            ),
            "htmlUrl": (
                f"https://github.com/{request['repository']}"
                f"/actions/runs/{run['id']}"
            ),
            "stageIdentity": request["inputs"]["stage_identity"],
            "dispatchAttemptIdentity": request["inputs"][
                "dispatch_attempt_identity"
            ],
        }
        if (
            self.lost_response_stage == stage_name
            and stage_name not in self.lost_once
        ):
            self.lost_once.add(stage_name)
            if self.delayed_visibility:
                self.store.hidden_run_queries[(str(request["repository"]), int(run["id"]))] = 1
            self.store.event(
                "simulated-lost-dispatch-response",
                stage=stage_name,
                runId=run["id"],
                delayedVisibility=self.delayed_visibility,
            )
            raise RuntimeError("simulated lost dispatch response")
        return result

    def _open_dispatch(self, api_request: object, timeout: int) -> JsonResponse:
        if timeout != 30 or not isinstance(api_request, urllib.request.Request):
            raise ValueError("Rehearsal dispatch HTTP request is invalid.")
        if api_request.get_method() != "POST" or api_request.data is None:
            raise ValueError("Rehearsal dispatch HTTP method is invalid.")
        if (
            api_request.get_header("Authorization")
            != f"Bearer {self.store.token}"
            or api_request.get_header("Content-type") != "application/json"
            or api_request.get_header("User-agent")
            != "NetWasm-release-coordinator"
        ):
            raise ValueError("Rehearsal dispatch HTTP headers are invalid.")
        match = re.fullmatch(
            r"https://api\.github\.com/repos/([^/]+/[^/]+)/actions/workflows/([^/]+)/dispatches",
            api_request.full_url,
        )
        if match is None:
            raise ValueError("Rehearsal dispatch HTTP target is invalid.")
        repository = match.group(1)
        workflow_name = urllib.parse.unquote(match.group(2))
        body = json.loads(api_request.data)
        if set(body) != {"ref", "inputs", "return_run_details"} or body[
            "return_run_details"
        ] is not True:
            raise ValueError("Rehearsal dispatch HTTP body is invalid.")
        inputs = body["inputs"]
        if not isinstance(inputs, dict):
            raise ValueError("Rehearsal dispatch inputs are invalid.")
        stage = self._stage(str(inputs.get("coordinated_stage")))
        request = {
            "repository": repository,
            "workflow": f".github/workflows/{workflow_name}",
            "ref": body["ref"],
            "expectedWorkflowSha": stage["workflowCommit"],
            "inputs": inputs,
            "return_run_details": True,
        }
        result = self._simulate_dispatch(request)
        return JsonResponse({
            "workflow_run_id": int(result["workflowRunId"]),
            "run_url": result["runUrl"],
            "html_url": result["htmlUrl"],
        })

    def _dispatch_through_http(
        self,
        request: dict[str, object],
        token: str,
        preparation_path: Path,
    ) -> dict[str, object]:
        return self.real_dispatch_workflow(
            request,
            token,
            preparation_path,
            open_request=self._open_dispatch,
        )

    @staticmethod
    def _blocked_effect(*_: object, **__: object) -> None:
        raise AssertionError("Rehearsal blocked an external effect.")

    def _verify_effect_guards(self) -> None:
        def socket_probe(method: str) -> object:
            connection = socket.socket()
            try:
                return getattr(connection, method)(("127.0.0.1", 9))
            finally:
                connection.close()

        probes: tuple[Callable[[], object], ...] = (
            lambda: urllib.request.build_opener().open("https://example.invalid"),
            lambda: socket.create_connection(("127.0.0.1", 9)),
            lambda: socket_probe("connect"),
            lambda: socket_probe("connect_ex"),
            lambda: subprocess.run(["false"]),
            lambda: subprocess.Popen(["false"]),
            lambda: os.system("false"),
        )
        for probe in probes:
            try:
                probe()
            except AssertionError:
                continue
            raise AssertionError("Rehearsal external-effect guard did not activate.")
        self.effect_guards_verified = True

    @property
    def stage_names(self) -> list[str]:
        return [item[0] for item in TRAIN.PREPARATION_STAGES]

    def _execute(
        self,
        *,
        coordinator_attempt: int,
        start_index: int = 0,
    ) -> list[dict[str, object]]:
        results = []
        with patch.dict(os.environ, {
            "GITHUB_RUN_ID": COORDINATOR_RUN_ID,
            "GITHUB_RUN_ATTEMPT": str(coordinator_attempt),
        }), patch.object(
            COORDINATOR, "dispatch_workflow", side_effect=self._dispatch_through_http
        ), patch.object(
            urllib.request.OpenerDirector,
            "open",
            side_effect=self._blocked_effect,
        ), patch.object(
            socket,
            "create_connection",
            side_effect=self._blocked_effect,
        ), patch.object(
            socket.socket,
            "connect",
            side_effect=self._blocked_effect,
        ), patch.object(
            socket.socket,
            "connect_ex",
            side_effect=self._blocked_effect,
        ), patch.object(
            subprocess,
            "run",
            side_effect=self._blocked_effect,
        ), patch.object(
            subprocess,
            "Popen",
            side_effect=self._blocked_effect,
        ), patch.object(
            os,
            "system",
            side_effect=self._blocked_effect,
        ):
            if not self.effect_guards_verified:
                self._verify_effect_guards()
            for stage_name in self.stage_names[start_index:]:
                output = self.root / "outputs" / f"{stage_name}.json"
                result = ORCHESTRATOR.run_stage(
                    self.store,
                    self.preparation_path,
                    stage_name,
                    str(ACTOR_ID),
                    output,
                )
                results.append(result)
                self.store.event(
                    "stage-pass",
                    stage=stage_name,
                    mode=result["mode"],
                    receiptSha256=result["receiptSha256"],
                )
        return results

    def rehearse(self, *, resume_after_failure: bool = False) -> dict[str, object]:
        start_output = self.root / "prepared-start.json"
        core_preview = self._stage("core-preview")
        start = ORCHESTRATOR.prepare_start(
            self.store, int(core_preview["releaseId"]), start_output
        )
        self.store.event(
            "intentional-start",
            releaseId=start["releaseId"],
            preparationSha256=start["preparationSha256"],
        )
        completed: list[dict[str, object]] = []
        failed_index = None
        try:
            completed.extend(self._execute(coordinator_attempt=1))
        except (RuntimeError, ValueError) as error:
            resume_stage = self.failure_stage or (
                self.lost_response_stage if self.delayed_visibility else None
            ) or ("playground" if self.attachment_interruption else None)
            if resume_stage is None or not resume_after_failure:
                raise
            failed_index = self.stage_names.index(resume_stage)
            self.store.event(
                "expected-stage-failure",
                stage=resume_stage,
                error=str(error),
            )
            completed.extend(self._execute(
                coordinator_attempt=2,
                start_index=failed_index,
            ))
        observed = [
            str(item["stage"])
            for item in self.transcript
            if item.get("event") == "stage-pass"
        ]
        if observed != self.stage_names:
            raise AssertionError("Rehearsal stage order changed.")
        before = {
            "dispatch": self.store.dispatch_count,
            "build": self.store.build_count,
            "packagePush": self.store.package_push_count,
            "deploy": self.store.deploy_count,
            "publish": self.store.publish_count,
            "assets": {
                key: sha256(value)
                for key, value in self.store.release_assets.items()
            },
        }
        rerun = self._execute(coordinator_attempt=3)
        after = {
            "dispatch": self.store.dispatch_count,
            "build": self.store.build_count,
            "packagePush": self.store.package_push_count,
            "deploy": self.store.deploy_count,
            "publish": self.store.publish_count,
            "assets": {
                key: sha256(value)
                for key, value in self.store.release_assets.items()
            },
        }
        if before != after or any(item["mode"] != "resume" for item in rerun):
            raise AssertionError("Completed rehearsal rerun repeated an external effect.")
        website_anchor = TRAIN.stage_state_anchor(self.preparation, "website")
        if website_anchor != (
            str(core_preview["repository"]), int(core_preview["releaseId"])
        ):
            raise AssertionError("Website rehearsal state is not on the Core anchor.")
        receipt_digests = {
            stage: sha256(contents) for stage, contents in self.receipts.items()
        }
        if not self.effect_guards_verified:
            raise AssertionError("Rehearsal external-effect guards were not verified.")
        partial_unchanged = all(
            sha256(artifact_member(
                self.store.artifact_bytes(
                    str(self._stage("playground")["repository"]),
                    int(item["payloadArtifactId"]),
                ),
                str(item["archiveFileName"]),
            )) == item["archiveSha256"]
            for item in self.partial_candidates
        )
        reused = [
            item for item in self.transcript
            if item.get("event") == "retained-candidate-reused"
        ]
        retained_preserved = all(any(
            history["kind"] == event["kind"]
            and history["payloadArtifactId"] == event["payloadArtifactId"]
            and history["producerJobId"] == event["producerJobId"]
            and history["receiptSha256"] == event["receiptSha256"]
            and history["archiveSha256"] == event["archiveSha256"]
            for history in self.candidate_history
        ) for event in reused)
        lost_response_not_duplicated = (
            not self.delayed_visibility
            or sum(
                item.get("event") == "workflow-dispatch"
                and item.get("stage") == self.lost_response_stage
                for item in self.transcript
            ) == 1
        )
        attachment_reused = True
        if self.attachment_interruption:
            playground_dispatches = sum(
                item.get("event") == "workflow-dispatch"
                and item.get("stage") == "playground"
                for item in self.transcript
            )
            archive_uploads = sum(
                item.get("event") == "release-asset"
                and item.get("name") == "toolchain.tar.gz"
                for item in self.transcript
            )
            candidate_kinds = [
                str(item["kind"]) for item in self.candidate_history
            ]
            attachment_reused = (
                self.store.attachment_faulted
                and playground_dispatches == 1
                and archive_uploads == 1
                and candidate_kinds.count("playground-toolchain-candidate") == 1
                and candidate_kinds.count("playground-site-candidate") == 1
            )
        if not (
            partial_unchanged
            and retained_preserved
            and lost_response_not_duplicated
            and attachment_reused
        ):
            raise AssertionError("Rehearsal recovery coordinates changed.")
        return {
            "schemaVersion": 1,
            "status": "PASS",
            "externalEffects": "simulated",
            "revisions": self.revisions,
            "preparationSha256": self.preparation_digest,
            "stageOrder": self.stage_names,
            "failureStage": self.failure_stage,
            "lostResponseStage": self.lost_response_stage,
            "deliveryInterruption": self.delivery_interruption,
            "attachmentInterruption": self.attachment_interruption,
            "receiptSha256": receipt_digests,
            "candidateHistory": self.candidate_history,
            "partialCandidates": self.partial_candidates,
            "simulatedEffects": {
                "dispatches": self.store.dispatch_count,
                "candidateBuilds": self.store.build_count,
                "packagePushes": self.store.package_push_count,
                "pagesDeployments": self.store.deploy_count,
                "releasePublications": self.store.publish_count,
            },
            "assertions": {
                "eightStagesCompleted": True,
                "completedRerunHadNoEffects": True,
                "websiteUsesCorePreviewAnchor": True,
                "networkBlocked": True,
                "publicationSubprocessesBlocked": True,
                "effectGuardsExercised": True,
                "partialCandidateArchivesUnchanged": partial_unchanged,
                "retainedCandidateCoordinatesPreserved": retained_preserved,
                "lostResponseDidNotDuplicateDispatch": lost_response_not_duplicated,
                "releaseAttachmentResumedWithoutRebuild": attachment_reused,
            },
            "events": self.transcript,
        }


def default_revisions() -> dict[str, str]:
    return {
        repository: f"{index:x}" * 40
        for index, repository in enumerate(PREPARATION.REPOSITORIES, 1)
    }


def run_scenario(
    *,
    revisions: dict[str, str],
    playground_root: Path,
    website_root: Path,
    failure_stage: str | None = None,
    lost_response_stage: str | None = None,
    delivery_interruption: str | None = None,
    delayed_visibility: bool = False,
    attachment_interruption: bool = False,
) -> dict[str, object]:
    rehearsal = ReleaseRehearsal(
        revisions=revisions,
        playground_root=playground_root,
        website_root=website_root,
        failure_stage=failure_stage,
        lost_response_stage=lost_response_stage,
        delivery_interruption=delivery_interruption,
        delayed_visibility=delayed_visibility,
        attachment_interruption=attachment_interruption,
    )
    try:
        return rehearsal.rehearse(
            resume_after_failure=(
                failure_stage is not None
                or (lost_response_stage is not None and delayed_visibility)
                or attachment_interruption
            )
        )
    finally:
        rehearsal.close()


def run_tamper_scenario(
    *,
    revisions: dict[str, str],
    playground_root: Path,
    website_root: Path,
    mutation: str,
) -> dict[str, object]:
    rehearsal = ReleaseRehearsal(
        revisions=revisions,
        playground_root=playground_root,
        website_root=website_root,
    )
    try:
        rehearsal.rehearse()
        stage_name = "website"
        if mutation == "receipt":
            stage_name = "core-stable"
            stage = rehearsal._stage(stage_name)
            key = (
                str(stage["repository"]),
                int(stage["releaseId"]),
                ORCHESTRATOR.receipt_asset_name(stage_name),
            )
            value = json.loads(rehearsal.store.release_assets[key])
            value["preparationSha256"] = "f" * 64
            rehearsal.store.release_assets[key] = json_bytes(value)
        elif mutation == "archive":
            stage = rehearsal._stage(stage_name)
            anchor = TRAIN.stage_state_anchor(rehearsal.preparation, stage_name)
            receipt_key = (
                *anchor,
                ORCHESTRATOR.candidate_receipt_asset_name(
                    "website-site-candidate"
                ),
            )
            receipt = json.loads(rehearsal.store.release_assets[receipt_key])
            archive_name = str(receipt["archive"]["fileName"])
            rehearsal.store.release_assets[(*anchor, archive_name)] += b"tampered"
        elif mutation in {"actor", "attempt"}:
            stage = rehearsal._stage(stage_name)
            anchor = TRAIN.stage_state_anchor(rehearsal.preparation, stage_name)
            completion = json.loads(rehearsal.store.release_assets[
                (*anchor, ORCHESTRATOR.receipt_asset_name(stage_name))
            ])
            producer = completion["producer"]
            key = (
                str(stage["repository"]),
                int(producer["runId"]),
                int(producer["runAttempt"]),
            )
            run = rehearsal.store.run_attempts[key]
            if mutation == "actor":
                run["actor"] = {"id": ACTOR_ID + 1}
            else:
                run["run_attempt"] = int(producer["runAttempt"]) + 1
        elif mutation == "missing-browser-lane":
            stage_name = "playground"
            stage = rehearsal._stage(stage_name)
            anchor = TRAIN.stage_state_anchor(rehearsal.preparation, stage_name)
            key = (*anchor, ORCHESTRATOR.receipt_asset_name(stage_name))
            value = json.loads(rehearsal.store.release_assets[key])
            value["liveChecks"] = value["liveChecks"][:-1]
            rehearsal.store.release_assets[key] = json_bytes(value)
        else:
            raise ValueError(f"Unknown rehearsal tamper mutation: {mutation}.")
        effects = (
            rehearsal.store.dispatch_count,
            rehearsal.store.build_count,
            rehearsal.store.package_push_count,
            rehearsal.store.deploy_count,
            rehearsal.store.publish_count,
        )
        try:
            rehearsal._execute(
                coordinator_attempt=4,
                start_index=rehearsal.stage_names.index(stage_name),
            )
        except (ValueError, RuntimeError) as error:
            after = (
                rehearsal.store.dispatch_count,
                rehearsal.store.build_count,
                rehearsal.store.package_push_count,
                rehearsal.store.deploy_count,
                rehearsal.store.publish_count,
            )
            if effects != after:
                raise AssertionError(
                    "Tampered evidence triggered a simulated external effect."
                ) from error
            return {
                "schemaVersion": 1,
                "status": "PASS",
                "externalEffects": "simulated",
                "mutation": mutation,
                "rejectedStage": stage_name,
                "errorType": type(error).__name__,
                "error": str(error),
                "assertions": {
                    "tamperRejected": True,
                    "noEffectAfterTamper": True,
                },
            }
        raise AssertionError(f"Tampered rehearsal evidence was accepted: {mutation}.")
    finally:
        rehearsal.close()


def run_matrix(
    *,
    revisions: dict[str, str],
    playground_root: Path,
    website_root: Path,
    source_attribution: list[dict[str, object]] | None = None,
    workflow_contracts: dict[str, object] | None = None,
) -> dict[str, object]:
    scenarios = [{
        "name": "success",
        "result": run_scenario(
            revisions=revisions,
            playground_root=playground_root,
            website_root=website_root,
        ),
    }]
    for stage_name in [item[0] for item in TRAIN.PREPARATION_STAGES]:
        scenarios.append({
            "name": f"failure-resume-{stage_name}",
            "result": run_scenario(
                revisions=revisions,
                playground_root=playground_root,
                website_root=website_root,
                failure_stage=stage_name,
            ),
        })
        scenarios.append({
            "name": f"lost-response-{stage_name}",
            "result": run_scenario(
                revisions=revisions,
                playground_root=playground_root,
                website_root=website_root,
                lost_response_stage=stage_name,
                delayed_visibility=True,
            ),
        })
    for name, interruption in (
        ("recovery-retained-toolchain-only", "toolchain-only"),
        ("recovery-archive-before-receipt", "archive-before-receipt"),
    ):
        scenarios.append({
            "name": name,
            "result": run_scenario(
                revisions=revisions,
                playground_root=playground_root,
                website_root=website_root,
                failure_stage="playground",
                delivery_interruption=interruption,
            ),
        })
    scenarios.append({
        "name": "recovery-release-attachment-interruption",
        "result": run_scenario(
            revisions=revisions,
            playground_root=playground_root,
            website_root=website_root,
            attachment_interruption=True,
        ),
    })
    for mutation in (
        "receipt", "archive", "actor", "attempt", "missing-browser-lane",
    ):
        scenarios.append({
            "name": f"tamper-{mutation}",
            "result": run_tamper_scenario(
                revisions=revisions,
                playground_root=playground_root,
                website_root=website_root,
                mutation=mutation,
            ),
        })
    transcript = {
        "schemaVersion": 1,
        "status": "PASS",
        "externalEffects": "simulated",
        "sourceAttribution": source_attribution or [],
        "workflowContracts": workflow_contracts or {},
        "scenarios": scenarios,
    }
    encoded = json_bytes(transcript)
    return {
        **transcript,
        "transcriptSha256": sha256(encoded),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--playground-root", type=Path, required=True)
    parser.add_argument("--website-root", type=Path, required=True)
    parser.add_argument("--revisions", type=Path, required=True)
    parser.add_argument("--core-revision", required=True)
    parser.add_argument("--verify-determinism", action="store_true")
    parser.add_argument("--allow-dirty-development", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    revisions = load_revisions(arguments.revisions, arguments.core_revision)
    source_attribution = validate_source_revisions(
        revisions,
        arguments.playground_root,
        arguments.website_root,
        allow_dirty_development=arguments.allow_dirty_development,
    )
    workflow_contracts = validate_workflow_contracts(
        arguments.playground_root, arguments.website_root
    )
    result = run_matrix(
        revisions=revisions,
        playground_root=arguments.playground_root,
        website_root=arguments.website_root,
        source_attribution=source_attribution,
        workflow_contracts=workflow_contracts,
    )
    if arguments.verify_determinism:
        repeated = run_matrix(
            revisions=revisions,
            playground_root=arguments.playground_root,
            website_root=arguments.website_root,
            source_attribution=source_attribution,
            workflow_contracts=workflow_contracts,
        )
        if json_bytes(result) != json_bytes(repeated):
            raise ValueError("Rehearsal transcript is not deterministic.")
    write_json(arguments.output, result)
    print(json.dumps({
        "status": result["status"],
        "scenarioCount": len(result["scenarios"]),
        "transcriptSha256": result["transcriptSha256"],
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

from __future__ import annotations

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile


ROOT = Path(__file__).parents[1]


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


MODULE = load("release_orchestrator", ROOT / "release-orchestrator.py")
PREPARATION = load("release_preparation_for_orchestrator", ROOT / "release-preparation.py")


class FakeStore:
    def __init__(self) -> None:
        self.token = "app-token"
        self.releases: dict[tuple[str, int], dict[str, object]] = {}
        self.asset_contents: dict[tuple[str, int, str], bytes] = {}
        self.tags: dict[tuple[str, str], str] = {}
        self.workflow_artifacts: list[dict[str, object]] = []
        self.archives: dict[int, bytes] = {}
        self.runs: list[dict[str, object]] = []
        self.run_by_id: dict[str, dict[str, object]] = {}
        self.run_attempts: dict[tuple[int, int], dict[str, object]] = {}
        self.jobs: list[dict[str, object]] = []
        self.deployments: dict[tuple[str, int], dict[str, object]] = {}
        self.deployment_status_values: dict[
            tuple[str, int], list[dict[str, object]]
        ] = {}
        self.publish_calls = 0
        self.upload_calls: list[tuple[str, int, str]] = []
        self.asset_queries: list[tuple[str, int]] = []
        self.workflow_run_queries: list[tuple[str, str]] = []

    def release(self, repository: str, release_id: int) -> dict[str, object]:
        return self.releases[(repository, release_id)]

    def asset_bytes(
        self, repository: str, release_id: int, name: str
    ) -> bytes | None:
        return self.asset_contents.get((repository, release_id, name))

    def upload_asset(
        self, repository: str, release_id: int, name: str, contents: bytes
    ) -> None:
        self.upload_calls.append((repository, release_id, name))
        self.asset_contents[(repository, release_id, name)] = contents

    def release_asset_bytes_bounded(
        self,
        repository: str,
        release_id: int,
        asset_name: str,
        maximum_bytes: int,
    ) -> bytes | None:
        contents = self.asset_contents.get((repository, release_id, asset_name))
        if contents is not None and len(contents) > maximum_bytes:
            raise ValueError("Download exceeds its byte limit.")
        return contents

    def assets(self, repository: str, release_id: int) -> list[dict[str, object]]:
        self.asset_queries.append((repository, release_id))
        return [
            {"id": index, "name": name}
            for index, (candidate_repository, candidate_release, name) in enumerate(
                self.asset_contents, 1
            )
            if candidate_repository == repository and candidate_release == release_id
        ]

    def tag_commit(self, repository: str, tag: str) -> str | None:
        return self.tags.get((repository, tag))

    def run_artifacts(
        self, repository: str, run_id: str
    ) -> list[dict[str, object]]:
        return self.workflow_artifacts

    def artifact_bytes(self, repository: str, artifact_id: int) -> bytes:
        return self.archives[artifact_id]

    def artifact_bytes_bounded(
        self, repository: str, artifact_id: int, maximum_bytes: int
    ) -> bytes:
        contents = self.archives[artifact_id]
        if len(contents) > maximum_bytes:
            raise ValueError("Workflow artifact exceeds its byte limit.")
        return contents

    def workflow_runs(
        self, repository: str, workflow: str, ref: str
    ) -> list[dict[str, object]]:
        return self.runs

    def workflow_run(self, repository: str, run_id: str) -> dict[str, object]:
        self.workflow_run_queries.append((repository, run_id))
        return self.run_by_id[run_id]

    def workflow_run_attempt(
        self, repository: str, run_id: int, run_attempt: int
    ) -> dict[str, object]:
        return self.run_attempts[(run_id, run_attempt)]

    def workflow_jobs(
        self, repository: str, run_id: int, run_attempt: int
    ) -> list[dict[str, object]]:
        return self.jobs

    def deployment(
        self, repository: str, deployment_id: int
    ) -> dict[str, object]:
        return self.deployments[(repository, deployment_id)]

    def deployment_statuses(
        self, repository: str, deployment_id: int
    ) -> list[dict[str, object]]:
        return self.deployment_status_values[(repository, deployment_id)]

    def publish_release(self, repository: str, release_id: int) -> dict[str, object]:
        self.publish_calls += 1
        release = self.releases[(repository, release_id)]
        release["draft"] = False
        return release


class StreamResponse(io.BytesIO):
    status = 200

    def __init__(self, contents: bytes, headers: dict[str, str] | None = None) -> None:
        super().__init__(contents)
        self.headers = headers or {}
        self.read_sizes: list[int] = []

    def read(self, size: int = -1) -> bytes:
        self.read_sizes.append(size)
        return super().read(size)


def preparation_document() -> dict[str, object]:
    repositories = {}
    for index, repository in enumerate(PREPARATION.REPOSITORIES, 1):
        commit = f"{index:x}" * 40
        repositories[repository] = {
            "sourceCommit": commit,
            "infrastructureCommit": commit,
            "workflowCommit": commit,
            "workflowRef": "main",
        }
    coordinates = {
        "schemaVersion": 1,
        "version": "7.8.9",
        "repositories": repositories,
    }
    release_ids = {
        name: index
        for index, name in enumerate(PREPARATION.PACKAGE_STAGE_NAMES, 101)
    }
    return PREPARATION.create_preparation(coordinates, release_ids)


class ReleaseOrchestratorTests(unittest.TestCase):
    def candidate_fixture(
        self,
    ) -> tuple[dict[str, object], str, FakeStore, bytes, bytes]:
        preparation = preparation_document()
        encoded = (json.dumps(preparation, indent=2) + "\n").encode()
        with tempfile.NamedTemporaryFile() as temporary:
            Path(temporary.name).write_bytes(encoded)
            _, digest = MODULE.TRAIN.read_preparation(Path(temporary.name))
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        archive_bytes = b"exact browser toolchain candidate"
        receipt = {
            "schemaVersion": 1,
            "kind": "playground-toolchain-candidate",
            "stage": "playground",
            "preparationSha256": digest,
            "stageIdentity": MODULE.TRAIN.release_stage_identity(
                digest, "playground"
            ),
            "repository": stage["repository"],
            "sourceCommit": stage["sourceCommit"],
            "infrastructureCommit": stage["infrastructureCommit"],
            "workflowCommit": stage["workflowCommit"],
            "workflowRef": stage["workflowRef"],
            "stateAnchor": {
                "repository": stage["repository"],
                "releaseId": stage["releaseId"],
            },
            "upstreamReceipts": [
                {"stage": name, "sha256": "6" * 64}
                for name in stage["upstreamStages"]
            ],
            "producer": {
                "runId": 700,
                "runAttempt": 2,
                "jobId": 900,
                "workflowPath": stage["workflow"],
                "actorId": 12345,
                "dispatchAttemptIdentity": "7" * 64,
            },
            "archive": {
                "fileName": "toolchain.tar.gz",
                "bytes": len(archive_bytes),
                "sha256": MODULE.hashlib.sha256(archive_bytes).hexdigest(),
            },
            "artifact": {
                "repository": stage["repository"],
                "runId": 700,
                "runAttempt": 2,
                "artifactId": 92,
                "artifactName": "playground-toolchain-payload-700-2",
            },
            "toolchain": {"id": "8" * 64, "manifestSha256": "9" * 64},
        }
        receipt_bytes = (json.dumps(receipt, sort_keys=True) + "\n").encode()
        store = FakeStore()
        store.workflow_artifacts = [
            {
                "id": 91,
                "name": MODULE.candidate_receipt_artifact_name(
                    "playground-toolchain-candidate", 700, 2
                ),
                "expired": False,
            },
            {
                "id": 92,
                "name": "playground-toolchain-payload-700-2",
                "expired": False,
            },
        ]
        receipt_bundle = io.BytesIO()
        with zipfile.ZipFile(receipt_bundle, "w") as bundle:
            bundle.writestr(MODULE.CANDIDATE_RECEIPT_FILE, receipt_bytes)
        payload_bundle = io.BytesIO()
        with zipfile.ZipFile(payload_bundle, "w") as bundle:
            bundle.writestr("toolchain.tar.gz", archive_bytes)
        store.archives[91] = receipt_bundle.getvalue()
        store.archives[92] = payload_bundle.getvalue()
        store.run_by_id["700"] = {
            "id": 700,
            "run_attempt": 2,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "status": "completed",
            "conclusion": "failure",
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": 12345},
        }
        store.run_attempts[(700, 2)] = dict(store.run_by_id["700"])
        store.jobs = [{
            "id": 900,
            "run_id": 700,
            "run_attempt": 2,
            "status": "completed",
            "conclusion": "success",
            "head_sha": stage["workflowCommit"],
        }]
        return preparation, digest, store, receipt_bytes, archive_bytes

    def collect_candidate(
        self,
        preparation: dict[str, object],
        digest: str,
        store: FakeStore,
    ) -> dict[str, object]:
        return MODULE.CandidateArtifactCollector(store).collect(
            preparation,
            digest,
            "playground-toolchain-candidate",
            700,
            2,
            "12345",
            "7" * 64,
        )

    def stage_fixture(self) -> tuple[dict[str, object], bytes, str, FakeStore]:
        preparation = preparation_document()
        contents = (json.dumps(preparation, indent=2) + "\n").encode()
        with tempfile.NamedTemporaryFile() as temporary:
            Path(temporary.name).write_bytes(contents)
            _, digest = MODULE.TRAIN.read_preparation(Path(temporary.name))
        stage = preparation["stages"][0]
        store = FakeStore()
        key = (str(stage["repository"]), int(stage["releaseId"]))
        store.releases[key] = {
            "id": stage["releaseId"],
            "tag_name": stage["ref"],
            "target_commitish": stage["sourceCommit"],
            "draft": False,
            "prerelease": True,
        }
        store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = contents
        store.tags[(str(stage["repository"]), str(stage["ref"]))] = str(
            stage["sourceCommit"]
        )
        run = {
            "id": 700,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "display_title": MODULE.expected_run_title(digest, str(stage["name"])),
            "status": "completed",
            "conclusion": "success",
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": 12345},
        }
        store.run_by_id["700"] = run
        store.workflow_artifacts = [{
            "id": 91,
            "name": "publication-receipt-core-preview-700-1",
            "expired": False,
        }]
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, "w") as bundle:
            bundle.writestr("publication-receipt.json", b"receipt")
        store.archives[91] = archive.getvalue()
        return preparation, contents, digest, store

    def seed_intent(
        self,
        preparation: dict[str, object],
        digest: str,
        store: FakeStore,
        identity: str = "a" * 64,
    ) -> str:
        stage = preparation["stages"][0]
        key = (str(stage["repository"]), int(stage["releaseId"]))
        name = f"dispatch-intent-core-preview-{identity}.json"
        store.asset_contents[(*key, name)] = MODULE.dispatch_intent(
            stage, digest, identity
        )
        return identity

    def test_start_requires_the_exact_published_core_preview(self) -> None:
        preparation = preparation_document()
        stage = preparation["stages"][0]
        contents = (json.dumps(preparation, indent=2) + "\n").encode()
        store = FakeStore()
        key = (str(stage["repository"]), int(stage["releaseId"]))
        store.releases[key] = {
            "id": stage["releaseId"],
            "tag_name": stage["ref"],
            "target_commitish": stage["sourceCommit"],
            "draft": False,
            "prerelease": True,
        }
        store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = contents
        store.tags[(str(stage["repository"]), str(stage["ref"]))] = str(
            stage["sourceCommit"]
        )
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "release-preparation.json"
            result = MODULE.prepare_start(store, int(stage["releaseId"]), output)
            self.assertEqual("7.8.9", result["version"])
            self.assertEqual(contents, output.read_bytes())

            store.releases[key]["draft"] = True
            with self.assertRaisesRegex(ValueError, "not published"):
                MODULE.prepare_start(store, int(stage["releaseId"]), output)

    def test_release_asset_reuse_requires_identical_bytes(self) -> None:
        store = FakeStore()
        MODULE.ensure_asset(store, "owner/repo", 1, "receipt.json", b"one")
        MODULE.ensure_asset(store, "owner/repo", 1, "receipt.json", b"one")
        with self.assertRaisesRegex(ValueError, "conflicts"):
            MODULE.ensure_asset(store, "owner/repo", 1, "receipt.json", b"two")

    def test_workflow_run_is_bound_to_the_app_and_prepared_workflow(self) -> None:
        stage = preparation_document()["stages"][2]
        run = {
            "id": 700,
            "run_attempt": 2,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "status": "completed",
            "conclusion": "success",
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": 12345},
        }
        MODULE.validate_run(run, stage, "700", "2", "12345", require_complete=True)
        run["actor"] = {"id": 54321}
        with self.assertRaisesRegex(ValueError, "identity"):
            MODULE.validate_run(
                run, stage, "700", "2", "12345", require_complete=True
            )

    def test_receipt_artifact_accepts_one_bounded_named_file(self) -> None:
        stage = preparation_document()["stages"][0]
        store = FakeStore()
        store.workflow_artifacts = [{
            "id": 91,
            "name": "publication-receipt-core-preview-700-2",
            "expired": False,
        }]
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, "w") as bundle:
            bundle.writestr("publication-receipt.json", b"receipt")
        store.archives[91] = archive.getvalue()
        self.assertEqual(
            b"receipt",
            MODULE.receipt_from_artifact(store, stage, "700", "2"),
        )

        archive = io.BytesIO()
        with zipfile.ZipFile(archive, "w") as bundle:
            bundle.writestr("../publication-receipt.json", b"receipt")
        store.archives[91] = archive.getvalue()
        with self.assertRaisesRegex(ValueError, "path"):
            MODULE.receipt_from_artifact(store, stage, "700", "2")

    def test_candidate_collector_recovers_original_receipt_and_payload(self) -> None:
        preparation, digest, store, receipt_bytes, archive_bytes = (
            self.candidate_fixture()
        )
        result = self.collect_candidate(preparation, digest, store)
        self.assertEqual(receipt_bytes, result["receiptBytes"])
        self.assertEqual(archive_bytes, result["archiveBytes"])
        self.assertEqual(91, result["receiptArtifactId"])
        self.assertEqual(92, result["payloadArtifactId"])

    def test_candidate_collector_fails_closed_on_expired_or_changed_evidence(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()

        store.workflow_artifacts[0]["expired"] = True
        with self.assertRaisesRegex(ValueError, "artifact is missing"):
            self.collect_candidate(preparation, digest, store)
        store.workflow_artifacts[0]["expired"] = False

        changed = io.BytesIO()
        with zipfile.ZipFile(changed, "w") as bundle:
            bundle.writestr("toolchain.tar.gz", b"different bytes")
        store.archives[92] = changed.getvalue()
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.collect_candidate(preparation, digest, store)

    def test_candidate_collector_rejects_ambiguous_or_unsafe_inventory(self) -> None:
        preparation, digest, store, _, archive_bytes = self.candidate_fixture()
        store.workflow_artifacts.append(dict(store.workflow_artifacts[1]))
        with self.assertRaisesRegex(ValueError, "artifact is missing"):
            self.collect_candidate(preparation, digest, store)
        store.workflow_artifacts.pop()

        unsafe = io.BytesIO()
        with zipfile.ZipFile(unsafe, "w") as bundle:
            bundle.writestr("../toolchain.tar.gz", archive_bytes)
        store.archives[92] = unsafe.getvalue()
        with self.assertRaisesRegex(ValueError, "member is invalid"):
            self.collect_candidate(preparation, digest, store)

    def test_candidate_collector_authenticates_failed_run_with_successful_producer(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        self.assertEqual(
            "failure",
            store.run_by_id["700"]["conclusion"],
        )
        self.collect_candidate(preparation, digest, store)

        baseline = json.loads(json.dumps(store.run_by_id["700"]))
        for field, value in (
            ("actor", {"id": 54321}),
            ("path", ".github/workflows/other.yml"),
            ("run_attempt", 3),
        ):
            with self.subTest(field=field):
                store.run_attempts[(700, 2)] = dict(baseline, **{field: value})
                with self.assertRaisesRegex(ValueError, "Workflow run identity"):
                    self.collect_candidate(preparation, digest, store)
        store.run_attempts[(700, 2)] = baseline

        store.jobs[0]["conclusion"] = "failure"
        with self.assertRaisesRegex(ValueError, "producer job did not succeed"):
            self.collect_candidate(preparation, digest, store)
        store.jobs.clear()
        with self.assertRaisesRegex(ValueError, "producer job is missing"):
            self.collect_candidate(preparation, digest, store)

    def test_candidate_collector_recovers_original_attempt_after_rerun(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        latest = dict(store.run_by_id["700"])
        latest["run_attempt"] = 3
        latest["status"] = "in_progress"
        latest["conclusion"] = None
        store.run_by_id["700"] = latest

        result = self.collect_candidate(preparation, digest, store)
        self.assertEqual(2, result["receipt"]["producer"]["runAttempt"])

    def test_candidate_collector_binds_dispatch_attempt(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        with self.assertRaisesRegex(ValueError, "producer does not match"):
            MODULE.CandidateArtifactCollector(store).collect(
                preparation,
                digest,
                "playground-toolchain-candidate",
                700,
                2,
                "12345",
                "a" * 64,
            )

    def test_delivery_completion_receipt_uses_exact_successful_run_and_job(self) -> None:
        preparation, digest, store, receipt_bytes, _ = self.candidate_fixture()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        repository = str(stage["repository"])
        upstream = {
            name: f"exact {name} receipt\n".encode()
            for name in stage["upstreamStages"]
        }
        toolchain_receipt = json.loads(receipt_bytes)
        toolchain_receipt["upstreamReceipts"] = [
            {
                "stage": name,
                "sha256": MODULE.hashlib.sha256(contents).hexdigest(),
            }
            for name, contents in upstream.items()
        ]
        toolchain_bytes = (
            json.dumps(toolchain_receipt, sort_keys=True) + "\n"
        ).encode()
        site_receipt = {
            **{
                field: toolchain_receipt[field]
                for field in (
                    "schemaVersion", "stage", "preparationSha256",
                    "stageIdentity", "repository", "sourceCommit",
                    "infrastructureCommit", "workflowCommit", "workflowRef",
                    "stateAnchor", "upstreamReceipts", "producer",
                )
            },
            "kind": "playground-site-candidate",
            "archive": {
                "fileName": "site.zip",
                "bytes": 4,
                "sha256": MODULE.hashlib.sha256(b"site").hexdigest(),
            },
            "artifact": {
                "repository": repository,
                "runId": 700,
                "runAttempt": 2,
                "artifactId": 93,
                "artifactName": "playground-site-payload-700-2",
            },
            "toolchainCandidateSha256": MODULE.hashlib.sha256(
                toolchain_bytes
            ).hexdigest(),
            "site": {
                "identitySha256": "4" * 64,
                "indexHtmlSha256": "a" * 64,
            },
            "toolchain": toolchain_receipt["toolchain"],
        }
        site_bytes = (json.dumps(site_receipt, sort_keys=True) + "\n").encode()
        producer = dict(toolchain_receipt["producer"])
        producer["jobId"] = 901
        completion = {
            **{
                field: toolchain_receipt[field]
                for field in (
                    "schemaVersion", "stage", "preparationSha256",
                    "stageIdentity", "repository", "sourceCommit",
                    "infrastructureCommit", "workflowCommit", "workflowRef",
                    "stateAnchor", "upstreamReceipts",
                )
            },
            "kind": "playground-completion",
            "producer": producer,
            "status": "PASS",
            "toolchainCandidateSha256": MODULE.hashlib.sha256(
                toolchain_bytes
            ).hexdigest(),
            "siteCandidateSha256": MODULE.hashlib.sha256(site_bytes).hexdigest(),
            "deployment": {
                "id": 1001,
                "environment": "github-pages",
                "url": "https://playground.netwasm.com/",
                "runId": 700,
                "runAttempt": 2,
                "jobId": 902,
            },
            "liveChecks": [
                {
                    "browser": browser,
                    "status": "PASS",
                    "jobId": 910 + index,
                    "siteIdentitySha256": "4" * 64,
                    "toolchainId": toolchain_receipt["toolchain"]["id"],
                    "toolchainManifestSha256": (
                        toolchain_receipt["toolchain"]["manifestSha256"]
                    ),
                    "evidence": {
                        "artifactId": 920 + index,
                        "artifactName": MODULE.TRAIN.delivery_evidence_artifact_name(
                            "playground-completion",
                            700,
                            2,
                            910 + index,
                            browser=browser,
                        ),
                        "fileName": "delivery-evidence.json",
                        "sha256": "7" * 64,
                    },
                }
                for index, browser in enumerate(("chromium", "firefox", "webkit"))
            ],
        }
        store.run_attempts[(700, 2)]["conclusion"] = "success"
        store.jobs.append({
            "id": 901,
            "run_id": 700,
            "run_attempt": 2,
            "status": "completed",
            "conclusion": "success",
            "head_sha": completion["workflowCommit"],
        })

        toolchain_candidate = {
            "receipt": toolchain_receipt,
            "receiptBytes": toolchain_bytes,
        }
        site_candidate = {
            "receipt": site_receipt,
            "receiptBytes": site_bytes,
        }
        for job_id in (902, 910, 911, 912):
            store.jobs.append({
                "id": job_id,
                "run_id": 700,
                "run_attempt": 2,
                "status": "completed",
                "conclusion": "success",
                "head_sha": stage["workflowCommit"],
            })
        store.deployments[(repository, 1001)] = {
            "id": 1001,
            "sha": stage["workflowCommit"],
            "ref": stage["workflowRef"],
            "task": "deploy",
            "environment": "github-pages",
            "creator": {"id": 12345},
        }
        store.deployment_status_values[(repository, 1001)] = [{
            "state": "success",
            "environment": "github-pages",
            "environment_url": "https://playground.netwasm.com/",
            "log_url": f"https://github.com/{repository}/actions/runs/700/job/902",
            "creator": {"id": 12345},
        }]
        for check in completion["liveChecks"]:
            evidence = check["evidence"]
            value = {
                "schemaVersion": 1,
                "status": "PASS",
                "stage": "playground",
                "repository": repository,
                "runId": 700,
                "runAttempt": 2,
                "jobId": check["jobId"],
                "dispatchAttemptIdentity": "7" * 64,
                "deployment": completion["deployment"],
                "browser": check["browser"],
                "siteIdentitySha256": check["siteIdentitySha256"],
                "toolchainId": check["toolchainId"],
                "toolchainManifestSha256": check["toolchainManifestSha256"],
            }
            evidence_bytes = (json.dumps(value, sort_keys=True) + "\n").encode()
            evidence["sha256"] = MODULE.hashlib.sha256(evidence_bytes).hexdigest()
            store.workflow_artifacts.append({
                "id": evidence["artifactId"],
                "name": evidence["artifactName"],
                "expired": False,
                "workflow_run": {
                    "id": 700,
                    "head_sha": stage["workflowCommit"],
                    "head_branch": stage["workflowRef"],
                },
            })
            bundle = io.BytesIO()
            with zipfile.ZipFile(bundle, "w") as archive:
                archive.writestr(evidence["fileName"], evidence_bytes)
            store.archives[evidence["artifactId"]] = bundle.getvalue()
        candidates = {
            "playground-toolchain-candidate": toolchain_candidate,
            "playground-site-candidate": site_candidate,
        }
        arguments = {
            "upstream_receipts": upstream,
            "approved_actor_id": "12345",
            "expected_dispatch_attempt_identity": "7" * 64,
        }
        MODULE.validate_delivery_completion(
            store, completion, preparation, digest, candidates, **arguments
        )

        changed_evidence = json.loads(json.dumps(completion))
        changed_evidence["liveChecks"][0]["toolchainId"] = "a" * 64
        with self.assertRaisesRegex(ValueError, "contradictory identities"):
            MODULE.validate_delivery_completion(
                store, changed_evidence, preparation, digest, candidates, **arguments
            )

        changed_dispatch = json.loads(json.dumps(completion))
        changed_dispatch["producer"]["dispatchAttemptIdentity"] = "b" * 64
        with self.assertRaisesRegex(ValueError, "dispatch identity"):
            MODULE.validate_delivery_completion(
                store, changed_dispatch, preparation, digest, candidates, **arguments
            )

        changed_candidate = dict(toolchain_candidate)
        changed_candidate["receiptBytes"] = b"not json"
        with self.assertRaisesRegex(ValueError, "invalid JSON"):
            MODULE.validate_delivery_completion(
                store,
                completion,
                preparation,
                digest,
                {**candidates, "playground-toolchain-candidate": changed_candidate},
                **arguments,
            )

        unrelated = json.loads(json.dumps(completion))
        unrelated["deployment"]["id"] = 1002
        store.deployments[(repository, 1002)] = {
            **store.deployments[(repository, 1001)],
            "id": 1002,
            "sha": "f" * 40,
        }
        store.deployment_status_values[(repository, 1002)] = (
            store.deployment_status_values[(repository, 1001)]
        )
        with self.assertRaisesRegex(ValueError, "deployment identity"):
            MODULE.validate_delivery_completion(
                store, unrelated, preparation, digest, candidates, **arguments
            )

        zero_lanes = json.loads(json.dumps(completion))
        zero_lanes["liveChecks"] = []
        with self.assertRaisesRegex(ValueError, "three live checks"):
            MODULE.validate_delivery_completion(
                store, zero_lanes, preparation, digest, candidates, **arguments
            )

        mismatched_object = {
            **toolchain_candidate,
            "receipt": {**toolchain_receipt, "kind": "website-site-candidate"},
        }
        with self.assertRaisesRegex(ValueError, "bytes do not match"):
            MODULE.validate_delivery_completion(
                store,
                completion,
                preparation,
                digest,
                {**candidates, "playground-toolchain-candidate": mismatched_object},
                **arguments,
            )

        first_evidence = completion["liveChecks"][0]["evidence"]
        original_archive = store.archives[first_evidence["artifactId"]]
        for field, value, message in (
            ("schemaVersion", True, "live evidence"),
            ("runAttempt", 1, "live evidence"),
        ):
            with self.subTest(replayed_field=field):
                with zipfile.ZipFile(io.BytesIO(original_archive)) as archive:
                    evidence_value = json.loads(
                        archive.read(first_evidence["fileName"])
                    )
                evidence_value[field] = value
                evidence_bytes = (
                    json.dumps(evidence_value, sort_keys=True) + "\n"
                ).encode()
                changed = json.loads(json.dumps(completion))
                changed["liveChecks"][0]["evidence"]["sha256"] = (
                    MODULE.hashlib.sha256(evidence_bytes).hexdigest()
                )
                bundle = io.BytesIO()
                with zipfile.ZipFile(bundle, "w") as archive:
                    archive.writestr(first_evidence["fileName"], evidence_bytes)
                store.archives[first_evidence["artifactId"]] = bundle.getvalue()
                with self.assertRaisesRegex(ValueError, message):
                    MODULE.validate_delivery_completion(
                        store,
                        changed,
                        preparation,
                        digest,
                        candidates,
                        **arguments,
                    )
                store.archives[first_evidence["artifactId"]] = original_archive

        next(job for job in store.jobs if job["id"] == 901)["conclusion"] = "failure"
        with self.assertRaisesRegex(ValueError, "producer job did not succeed"):
            MODULE.validate_delivery_completion(
                store, completion, preparation, digest, candidates, **arguments
            )

    def test_bounded_artifact_download_stops_without_relying_on_content_length(self) -> None:
        for headers in ({}, {"Content-Length": "4"}):
            with self.subTest(headers=headers):
                response = StreamResponse(b"0123456789", headers)
                store = MODULE.GitHubStore.__new__(MODULE.GitHubStore)
                store.token = "token"
                store.open = lambda *_args, **_kwargs: response
                with self.assertRaisesRegex(ValueError, "exceeds its byte limit"):
                    store.artifact_bytes_bounded("owner/repo", 1, 4)
                self.assertEqual([5], response.read_sizes)

        response = StreamResponse(b"unread", {"Content-Length": "100"})
        store = MODULE.GitHubStore.__new__(MODULE.GitHubStore)
        store.token = "token"
        store.open = lambda *_args, **_kwargs: response
        with self.assertRaisesRegex(ValueError, "exceeds its byte limit"):
            store.artifact_bytes_bounded("owner/repo", 1, 4)
        self.assertEqual([], response.read_sizes)

    def test_website_completion_binds_candidate_deployment_and_live_evidence(self) -> None:
        preparation = preparation_document()
        encoded = (json.dumps(preparation, indent=2) + "\n").encode()
        with tempfile.NamedTemporaryFile() as temporary:
            Path(temporary.name).write_bytes(encoded)
            _, digest = MODULE.TRAIN.read_preparation(Path(temporary.name))
        stage = MODULE.TRAIN.preparation_stage(preparation, "website")
        repository = str(stage["repository"])
        upstream = {"playground": b"exact Playground completion\n"}
        upstream_coordinates = [{
            "stage": "playground",
            "sha256": MODULE.hashlib.sha256(upstream["playground"]).hexdigest(),
        }]
        anchor_repository, anchor_release_id = MODULE.TRAIN.stage_state_anchor(
            preparation, "website"
        )
        producer = {
            "runId": 701,
            "runAttempt": 3,
            "jobId": 801,
            "workflowPath": stage["workflow"],
            "actorId": 12345,
            "dispatchAttemptIdentity": "c" * 64,
        }
        envelope = {
            "schemaVersion": 1,
            "stage": "website",
            "preparationSha256": digest,
            "stageIdentity": MODULE.TRAIN.release_stage_identity(digest, "website"),
            "repository": repository,
            "sourceCommit": stage["sourceCommit"],
            "infrastructureCommit": stage["infrastructureCommit"],
            "workflowCommit": stage["workflowCommit"],
            "workflowRef": stage["workflowRef"],
            "stateAnchor": {
                "repository": anchor_repository,
                "releaseId": anchor_release_id,
            },
            "upstreamReceipts": upstream_coordinates,
            "producer": producer,
        }
        site_identity = {
            "identitySha256": "d" * 64,
            "indexHtmlSha256": "e" * 64,
        }
        candidate_receipt = {
            **envelope,
            "kind": "website-site-candidate",
            "archive": {
                "fileName": "website.zip",
                "bytes": 7,
                "sha256": MODULE.hashlib.sha256(b"website").hexdigest(),
            },
            "artifact": {
                "repository": repository,
                "runId": 701,
                "runAttempt": 3,
                "artifactId": 810,
                "artifactName": "website-site-payload-701-3",
            },
            "site": site_identity,
        }
        candidate_bytes = (
            json.dumps(candidate_receipt, sort_keys=True) + "\n"
        ).encode()
        completion_producer = {**producer, "jobId": 811}
        completion = {
            **{**envelope, "producer": completion_producer},
            "kind": "website-completion",
            "status": "PASS",
            "siteCandidateSha256": MODULE.hashlib.sha256(
                candidate_bytes
            ).hexdigest(),
            "deployment": {
                "id": 2001,
                "environment": "github-pages",
                "url": "https://www.netwasm.com/",
                "runId": 701,
                "runAttempt": 3,
                "jobId": 812,
            },
            "liveCheck": {
                "status": "PASS",
                "jobId": 813,
                "siteIdentitySha256": site_identity["identitySha256"],
                "playgroundCompletionSha256": upstream_coordinates[0]["sha256"],
                "evidence": {
                    "artifactId": 814,
                    "artifactName": MODULE.TRAIN.delivery_evidence_artifact_name(
                        "website-completion", 701, 3, 813
                    ),
                    "fileName": "delivery-evidence.json",
                    "sha256": "0" * 64,
                },
            },
        }
        store = FakeStore()
        store.run_attempts[(701, 3)] = {
            "id": 701,
            "run_attempt": 3,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "status": "completed",
            "conclusion": "success",
            "repository": {"full_name": repository},
            "actor": {"id": 12345},
        }
        store.jobs = [
            {
                "id": job_id,
                "run_id": 701,
                "run_attempt": 3,
                "status": "completed",
                "conclusion": "success",
                "head_sha": stage["workflowCommit"],
            }
            for job_id in (811, 812, 813)
        ]
        store.deployments[(repository, 2001)] = {
            "id": 2001,
            "sha": stage["workflowCommit"],
            "ref": stage["workflowRef"],
            "task": "deploy",
            "environment": "github-pages",
            "creator": {"id": 12345},
        }
        store.deployment_status_values[(repository, 2001)] = [{
            "state": "success",
            "environment": "github-pages",
            "environment_url": "https://www.netwasm.com/",
            "log_url": f"https://github.com/{repository}/actions/runs/701/job/812",
            "creator": {"id": 12345},
        }]
        evidence = {
            "schemaVersion": 1,
            "status": "PASS",
            "stage": "website",
            "repository": repository,
            "runId": 701,
            "runAttempt": 3,
            "jobId": 813,
            "dispatchAttemptIdentity": "c" * 64,
            "deployment": completion["deployment"],
            "siteIdentitySha256": site_identity["identitySha256"],
            "playgroundCompletionSha256": upstream_coordinates[0]["sha256"],
        }
        evidence_bytes = (json.dumps(evidence, sort_keys=True) + "\n").encode()
        completion["liveCheck"]["evidence"]["sha256"] = (
            MODULE.hashlib.sha256(evidence_bytes).hexdigest()
        )
        store.workflow_artifacts = [{
            "id": 814,
            "name": completion["liveCheck"]["evidence"]["artifactName"],
            "expired": False,
            "workflow_run": {
                "id": 701,
                "head_sha": stage["workflowCommit"],
                "head_branch": stage["workflowRef"],
            },
        }]
        bundle = io.BytesIO()
        with zipfile.ZipFile(bundle, "w") as archive:
            archive.writestr("delivery-evidence.json", evidence_bytes)
        store.archives[814] = bundle.getvalue()
        candidates = {"website-site-candidate": {
            "receipt": candidate_receipt,
            "receiptBytes": candidate_bytes,
        }}
        arguments = {
            "upstream_receipts": upstream,
            "approved_actor_id": "12345",
            "expected_dispatch_attempt_identity": "c" * 64,
        }
        MODULE.validate_delivery_completion(
            store, completion, preparation, digest, candidates, **arguments
        )

        changed = json.loads(json.dumps(completion))
        changed["liveCheck"]["siteIdentitySha256"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "live evidence"):
            MODULE.validate_delivery_completion(
                store, changed, preparation, digest, candidates, **arguments
            )

    def test_website_dispatch_state_uses_core_anchor_and_website_run(self) -> None:
        preparation = preparation_document()
        encoded = (json.dumps(preparation, indent=2) + "\n").encode()
        with tempfile.NamedTemporaryFile() as temporary:
            Path(temporary.name).write_bytes(encoded)
            _, digest = MODULE.TRAIN.read_preparation(Path(temporary.name))
        stage = MODULE.TRAIN.preparation_stage(preparation, "website")
        anchor = MODULE.TRAIN.stage_state_anchor(preparation, "website")
        identity = "c" * 64
        run = {
            "id": 701,
            "run_attempt": 3,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "status": "in_progress",
            "conclusion": None,
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": 12345},
        }
        store = FakeStore()
        store.run_by_id["701"] = run
        store.asset_contents[(*anchor, f"dispatch-intent-website-{identity}.json")] = (
            MODULE.dispatch_intent(stage, digest, identity)
        )
        store.asset_contents[(*anchor, "dispatch-coordinate-website-701.json")] = (
            MODULE.dispatch_coordinates(stage, digest, {
                "workflowRunId": "701",
                "dispatchAttemptIdentity": identity,
            })
        )
        persisted, attempts = MODULE.persisted_recoverable_run(
            store, preparation, stage, digest, "12345"
        )
        intents = MODULE.persisted_intents(store, preparation, stage, digest)
        self.assertEqual((run, identity), persisted)
        self.assertEqual({identity}, attempts)
        self.assertEqual({identity}, intents)
        self.assertTrue(store.asset_queries)
        self.assertTrue(all(query == anchor for query in store.asset_queries))
        self.assertEqual([(stage["repository"], "701")], store.workflow_run_queries)

    def test_failed_delivery_run_is_retained_before_retry_dispatch(self) -> None:
        preparation, digest, store, receipt_bytes, _ = self.candidate_fixture()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        encoded = (json.dumps(preparation, indent=2) + "\n").encode()
        upstream_values = {
            name: f"exact {name}\n".encode()
            for name in stage["upstreamStages"]
        }
        receipt = json.loads(receipt_bytes)
        receipt["upstreamReceipts"] = [
            {
                "stage": name,
                "sha256": MODULE.hashlib.sha256(value).hexdigest(),
            }
            for name, value in upstream_values.items()
        ]
        receipt_bytes = (json.dumps(receipt, sort_keys=True) + "\n").encode()
        receipt_bundle = io.BytesIO()
        with zipfile.ZipFile(receipt_bundle, "w") as archive:
            archive.writestr(MODULE.CANDIDATE_RECEIPT_FILE, receipt_bytes)
        store.archives[91] = receipt_bundle.getvalue()
        key = (str(stage["repository"]), int(stage["releaseId"]))
        store.releases[key] = {
            "id": stage["releaseId"],
            "tag_name": stage["ref"],
            "target_commitish": stage["sourceCommit"],
            "draft": True,
            "prerelease": False,
        }
        store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = encoded
        identity = "7" * 64
        store.asset_contents[
            (*key, f"dispatch-intent-playground-{identity}.json")
        ] = MODULE.dispatch_intent(stage, digest, identity)
        store.asset_contents[
            (*key, "dispatch-coordinate-playground-700.json")
        ] = MODULE.dispatch_coordinates(stage, digest, {
            "workflowRunId": "700",
            "dispatchAttemptIdentity": identity,
        })
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            preparation_path = root / "preparation.json"
            preparation_path.write_bytes(encoded)
            upstream_paths = []
            for name, value in upstream_values.items():
                path = root / f"{name}.json"
                path.write_text(json.dumps({"stage": name}) + "\n")
                upstream_values[name] = path.read_bytes()
                upstream_paths.append(path)
            receipt["upstreamReceipts"] = [
                {
                    "stage": name,
                    "sha256": MODULE.hashlib.sha256(value).hexdigest(),
                }
                for name, value in upstream_values.items()
            ]
            receipt_bytes = (json.dumps(receipt, sort_keys=True) + "\n").encode()
            receipt_bundle = io.BytesIO()
            with zipfile.ZipFile(receipt_bundle, "w") as archive:
                archive.writestr(MODULE.CANDIDATE_RECEIPT_FILE, receipt_bytes)
            store.archives[91] = receipt_bundle.getvalue()
            with patch.object(
                MODULE, "upstream_receipts", return_value=upstream_paths
            ), self.assertRaisesRegex(RuntimeError, "Retained delivery candidate"):
                MODULE.run_delivery_stage(
                    store,
                    preparation_path,
                    "playground",
                    "12345",
                    root / "completion.json",
                )

            receipt_name = MODULE.candidate_receipt_asset_name(
                "playground-toolchain-candidate"
            )
            self.assertEqual(receipt_bytes, store.asset_contents[(*key, receipt_name)])

            site_identity = "8" * 64
            site_archive = b"exact production site candidate"
            site_receipt = {
                **{
                    field: receipt[field]
                    for field in (
                        "schemaVersion", "stage", "preparationSha256",
                        "stageIdentity", "repository", "sourceCommit",
                        "infrastructureCommit", "workflowCommit", "workflowRef",
                        "stateAnchor", "upstreamReceipts",
                    )
                },
                "kind": "playground-site-candidate",
                "producer": {
                    "runId": 701,
                    "runAttempt": 1,
                    "jobId": 930,
                    "workflowPath": stage["workflow"],
                    "actorId": 12345,
                    "dispatchAttemptIdentity": site_identity,
                },
                "archive": {
                    "fileName": "site.zip",
                    "bytes": len(site_archive),
                    "sha256": MODULE.hashlib.sha256(site_archive).hexdigest(),
                },
                "artifact": {
                    "repository": stage["repository"],
                    "runId": 701,
                    "runAttempt": 1,
                    "artifactId": 94,
                    "artifactName": MODULE.TRAIN.delivery_candidate_payload_artifact_name(
                        "playground-site-candidate", 701, 1
                    ),
                },
                "toolchainCandidateSha256": MODULE.hashlib.sha256(
                    receipt_bytes
                ).hexdigest(),
                "site": {
                    "identitySha256": "4" * 64,
                    "indexHtmlSha256": "a" * 64,
                },
                "toolchain": receipt["toolchain"],
            }
            site_receipt_bytes = (
                json.dumps(site_receipt, sort_keys=True) + "\n"
            ).encode()
            site_receipt_bundle = io.BytesIO()
            with zipfile.ZipFile(site_receipt_bundle, "w") as archive:
                archive.writestr(MODULE.CANDIDATE_RECEIPT_FILE, site_receipt_bytes)
            site_payload_bundle = io.BytesIO()
            with zipfile.ZipFile(site_payload_bundle, "w") as archive:
                archive.writestr("site.zip", site_archive)
            store.workflow_artifacts.extend([
                {
                    "id": 93,
                    "name": MODULE.candidate_receipt_artifact_name(
                        "playground-site-candidate", 701, 1
                    ),
                    "expired": False,
                },
                {
                    "id": 94,
                    "name": MODULE.TRAIN.delivery_candidate_payload_artifact_name(
                        "playground-site-candidate", 701, 1
                    ),
                    "expired": False,
                },
            ])
            store.archives[93] = site_receipt_bundle.getvalue()
            store.archives[94] = site_payload_bundle.getvalue()
            site_run = {
                "id": 701,
                "run_attempt": 1,
                "event": "workflow_dispatch",
                "path": stage["workflow"],
                "head_sha": stage["workflowCommit"],
                "head_branch": stage["workflowRef"],
                "status": "completed",
                "conclusion": "failure",
                "repository": {"full_name": stage["repository"]},
                "actor": {"id": 12345},
            }
            store.run_by_id["701"] = site_run
            store.run_attempts[(701, 1)] = dict(site_run)
            store.jobs.append({
                "id": 930,
                "run_id": 701,
                "run_attempt": 1,
                "status": "completed",
                "conclusion": "success",
                "head_sha": stage["workflowCommit"],
            })
            store.asset_contents[
                (*key, f"dispatch-intent-playground-{site_identity}.json")
            ] = MODULE.dispatch_intent(stage, digest, site_identity)
            store.asset_contents[
                (*key, "dispatch-coordinate-playground-701.json")
            ] = MODULE.dispatch_coordinates(stage, digest, {
                "workflowRunId": "701",
                "dispatchAttemptIdentity": site_identity,
            })
            with patch.object(
                MODULE, "upstream_receipts", return_value=upstream_paths
            ), patch.object(
                MODULE.COORDINATOR, "dispatch_workflow"
            ) as dispatch, self.assertRaisesRegex(
                RuntimeError, "Retained delivery candidate"
            ):
                MODULE.run_delivery_stage(
                    store,
                    preparation_path,
                    "playground",
                    "12345",
                    root / "completion.json",
                )
            dispatch.assert_not_called()
            site_receipt_name = MODULE.candidate_receipt_asset_name(
                "playground-site-candidate"
            )
            self.assertEqual(
                site_receipt_bytes,
                store.asset_contents[(*key, site_receipt_name)],
            )

            failed_run = store.run_by_id["700"]
            failed_run["display_title"] = MODULE.expected_run_title(
                digest, "playground"
            )
            site_run["display_title"] = MODULE.expected_run_title(
                digest, "playground"
            )
            store.runs = [failed_run, site_run]
            captured = {}

            def lose_dispatch(request, *_args, **_kwargs):
                captured.update(request)
                raise TimeoutError("response lost")

            with patch.object(
                MODULE, "upstream_receipts", return_value=upstream_paths
            ), patch.dict(
                "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "2"}
            ), patch.object(
                MODULE.COORDINATOR, "dispatch_workflow", side_effect=lose_dispatch
            ), self.assertRaisesRegex(RuntimeError, "response was lost"):
                MODULE.run_delivery_stage(
                    store,
                    preparation_path,
                    "playground",
                    "12345",
                    root / "completion.json",
                )
            self.assertEqual(
                [{
                    "kind": "playground-toolchain-candidate",
                    "receiptSha256": MODULE.hashlib.sha256(receipt_bytes).hexdigest(),
                }, {
                    "kind": "playground-site-candidate",
                    "receiptSha256": MODULE.hashlib.sha256(
                        site_receipt_bytes
                    ).hexdigest(),
                }],
                json.loads(captured["inputs"]["retained_candidates"]),
            )

    def test_failed_candidate_payload_without_receipt_stops_recovery(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        store.workflow_artifacts = [store.workflow_artifacts[1]]
        with self.assertRaisesRegex(ValueError, "no approved receipt"):
            MODULE.harvest_delivery_candidates(
                store,
                preparation,
                digest,
                stage,
                store.run_by_id["700"],
                "7" * 64,
                "12345",
                {},
                {},
            )

    def test_delivery_lost_dispatch_can_recover_a_failed_visible_run(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        run = store.run_by_id["700"]
        run["display_title"] = MODULE.expected_run_title(digest, "playground")
        store.runs = [run]
        self.assertIs(
            run,
            MODULE.recoverable_delivery_run(
                store, stage, digest, "12345"
            ),
        )

    def test_successful_delivery_attaches_completion_before_publication(self) -> None:
        preparation = preparation_document()
        encoded = (json.dumps(preparation, indent=2) + "\n").encode()
        with tempfile.NamedTemporaryFile() as temporary:
            Path(temporary.name).write_bytes(encoded)
            _, digest = MODULE.TRAIN.read_preparation(Path(temporary.name))
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        key = (str(stage["repository"]), int(stage["releaseId"]))
        store = FakeStore()
        store.releases[key] = {
            "id": stage["releaseId"],
            "tag_name": stage["ref"],
            "target_commitish": stage["sourceCommit"],
            "draft": True,
            "prerelease": False,
        }
        store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = encoded
        store.tags[(str(stage["repository"]), str(stage["ref"]))] = str(
            stage["sourceCommit"]
        )
        store.run_by_id["701"] = {
            "id": 701,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": stage["workflow"],
            "head_sha": stage["workflowCommit"],
            "head_branch": stage["workflowRef"],
            "status": "completed",
            "conclusion": "success",
            "repository": {"full_name": stage["repository"]},
            "actor": {"id": 12345},
        }
        candidates = {
            kind: {"receipt": {"kind": kind}, "receiptBytes": kind.encode()}
            for kind in MODULE.delivery_candidate_kinds("playground")
        }
        completion = {
            "producer": {"runId": 701, "runAttempt": 1},
        }
        completion_bytes = b"exact validated completion\n"

        def dispatch(request, *_args, **_kwargs):
            inputs = request["inputs"]
            return {
                "workflowRunId": "701",
                "runUrl": "https://api.github.com/runs/701",
                "htmlUrl": "https://github.com/runs/701",
                "stageIdentity": inputs["stage_identity"],
                "dispatchAttemptIdentity": inputs["dispatch_attempt_identity"],
            }

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            preparation_path = root / "preparation.json"
            preparation_path.write_bytes(encoded)
            upstream_paths = []
            for name in stage["upstreamStages"]:
                path = root / f"{name}.json"
                path.write_text(json.dumps({"stage": name}) + "\n")
                upstream_paths.append(path)
            with patch.object(
                MODULE, "upstream_receipts", return_value=upstream_paths
            ), patch.dict(
                "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
            ), patch.object(
                MODULE.COORDINATOR, "dispatch_workflow", side_effect=dispatch
            ), patch.object(
                MODULE, "load_delivery_candidates", side_effect=[{}, candidates]
            ), patch.object(
                MODULE, "harvest_delivery_candidates",
                return_value=list(candidates),
            ), patch.object(
                MODULE, "completion_receipt_from_artifact",
                return_value=completion_bytes,
            ), patch.object(
                MODULE, "validate_receipt_bytes", return_value=completion
            ), patch.object(
                MODULE, "validate_delivery_completion"
            ) as validate_completion:
                result = MODULE.run_delivery_stage(
                    store,
                    preparation_path,
                    "playground",
                    "12345",
                    root / "completion.json",
                )
            self.assertEqual("PASS", result["status"])
            self.assertEqual(1, store.publish_calls)
            self.assertFalse(store.releases[key]["draft"])
            self.assertEqual(
                completion_bytes,
                store.asset_contents[(*key, MODULE.receipt_asset_name("playground"))],
            )
            self.assertEqual(completion_bytes, (root / "completion.json").read_bytes())
            validate_completion.assert_called_once()

    def test_playground_publication_revalidates_immutable_state_first(self) -> None:
        preparation = preparation_document()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        preparation_bytes = (json.dumps(preparation, indent=2) + "\n").encode()
        completion_bytes = b"approved completion\n"
        key = (str(stage["repository"]), int(stage["releaseId"]))

        def configured_store() -> FakeStore:
            store = FakeStore()
            store.releases[key] = {
                "id": stage["releaseId"],
                "tag_name": stage["ref"],
                "target_commitish": stage["sourceCommit"],
                "draft": True,
                "prerelease": False,
            }
            store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = (
                preparation_bytes
            )
            store.asset_contents[
                (*key, MODULE.receipt_asset_name("playground"))
            ] = completion_bytes
            return store

        mutations = {
            "target": lambda store: store.releases[key].update(
                {"target_commitish": "f" * 40}
            ),
            "tag": lambda store: store.tags.__setitem__(
                (str(stage["repository"]), str(stage["ref"])), "f" * 40
            ),
            "preparation": lambda store: store.asset_contents.__setitem__(
                (*key, PREPARATION.PREPARATION_ASSET), b"changed"
            ),
        }
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                store = configured_store()
                mutate(store)
                with self.assertRaisesRegex(ValueError, "identity changed"):
                    MODULE.publish_completed_playground_release(
                        store, stage, preparation_bytes, completion_bytes
                    )
                self.assertEqual(0, store.publish_calls)

    def test_candidate_attachment_is_archive_first_receipt_last_and_resumable(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        collected = self.collect_candidate(preparation, digest, store)
        upstream = {
            "core-stable": b"core",
            "tunit-stable": b"tunit",
            "libraries-stable": b"libraries",
        }
        receipt = collected["receipt"]
        receipt["upstreamReceipts"] = [
            {
                "stage": name,
                "sha256": MODULE.hashlib.sha256(contents).hexdigest(),
            }
            for name, contents in upstream.items()
        ]
        collected["receiptBytes"] = (
            json.dumps(receipt, sort_keys=True) + "\n"
        ).encode()
        MODULE.attach_candidate(
            store,
            preparation,
            digest,
            "playground-toolchain-candidate",
            collected,
            upstream_receipts=upstream,
        )
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        archive_name = receipt["archive"]["fileName"]
        receipt_name = MODULE.candidate_receipt_asset_name(
            "playground-toolchain-candidate"
        )
        self.assertEqual(
            [
                (stage["repository"], stage["releaseId"], archive_name),
                (stage["repository"], stage["releaseId"], receipt_name),
            ],
            store.upload_calls,
        )
        loaded = MODULE.load_attached_candidate(
            store,
            preparation,
            digest,
            "playground-toolchain-candidate",
            upstream_receipts=upstream,
        )
        self.assertEqual(collected["archiveBytes"], loaded["archiveBytes"])
        uploads = list(store.upload_calls)
        MODULE.attach_candidate(
            store,
            preparation,
            digest,
            "playground-toolchain-candidate",
            collected,
            upstream_receipts=upstream,
        )
        self.assertEqual(uploads, store.upload_calls)

    def test_candidate_attachment_rejects_conflict_before_receipt(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        collected = self.collect_candidate(preparation, digest, store)
        upstream = {
            name: contents
            for name, contents in (
                ("core-stable", b"core"),
                ("tunit-stable", b"tunit"),
                ("libraries-stable", b"libraries"),
            )
        }
        receipt = collected["receipt"]
        receipt["upstreamReceipts"] = [
            {"stage": name, "sha256": MODULE.hashlib.sha256(value).hexdigest()}
            for name, value in upstream.items()
        ]
        collected["receiptBytes"] = json.dumps(receipt).encode()
        stage = MODULE.TRAIN.preparation_stage(preparation, "playground")
        archive_name = receipt["archive"]["fileName"]
        store.asset_contents[(
            str(stage["repository"]), int(stage["releaseId"]), str(archive_name)
        )] = b"conflict"
        with self.assertRaisesRegex(ValueError, "byte limit|conflicts"):
            MODULE.attach_candidate(
                store,
                preparation,
                digest,
                "playground-toolchain-candidate",
                collected,
                upstream_receipts=upstream,
            )
        self.assertNotIn(
            (
                str(stage["repository"]),
                int(stage["releaseId"]),
                MODULE.candidate_receipt_asset_name(
                    "playground-toolchain-candidate"
                ),
            ),
            store.asset_contents,
        )

    def test_candidate_attachment_validates_the_exact_receipt_bytes(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        collected = self.collect_candidate(preparation, digest, store)
        upstream = {
            "core-stable": b"core",
            "tunit-stable": b"tunit",
            "libraries-stable": b"libraries",
        }
        collected["receipt"]["upstreamReceipts"] = [
            {"stage": name, "sha256": MODULE.hashlib.sha256(value).hexdigest()}
            for name, value in upstream.items()
        ]
        collected["receiptBytes"] = b"not JSON and not the validated object"
        with self.assertRaisesRegex(ValueError, "receipt is invalid JSON"):
            MODULE.attach_candidate(
                store,
                preparation,
                digest,
                "playground-toolchain-candidate",
                collected,
                upstream_receipts=upstream,
            )
        self.assertEqual([], store.upload_calls)

    def test_site_candidate_requires_exact_toolchain_candidate_receipt(self) -> None:
        preparation, digest, store, _, _ = self.candidate_fixture()
        collected = self.collect_candidate(preparation, digest, store)
        toolchain = collected["receipt"]
        upstream = {
            "core-stable": b"core",
            "tunit-stable": b"tunit",
            "libraries-stable": b"libraries",
        }
        toolchain["upstreamReceipts"] = [
            {"stage": name, "sha256": MODULE.hashlib.sha256(value).hexdigest()}
            for name, value in upstream.items()
        ]
        toolchain_receipt = (json.dumps(toolchain, sort_keys=True) + "\n").encode()
        receipt = {
            "kind": "playground-site-candidate",
            "upstreamReceipts": toolchain["upstreamReceipts"],
            "toolchainCandidateSha256": MODULE.hashlib.sha256(
                toolchain_receipt
            ).hexdigest(),
            "toolchain": toolchain["toolchain"],
        }
        MODULE.validate_candidate_dependencies(
            receipt,
            preparation,
            digest,
            upstream_receipts=upstream,
            toolchain_receipt_bytes=toolchain_receipt,
        )
        with self.assertRaisesRegex(ValueError, "invalid JSON"):
            MODULE.validate_candidate_dependencies(
                receipt,
                preparation,
                digest,
                upstream_receipts=upstream,
                toolchain_receipt_bytes=b"different",
            )

        changed = json.loads(json.dumps(receipt))
        changed["toolchain"]["id"] = "a" * 64
        with self.assertRaisesRegex(ValueError, "toolchain candidate"):
            MODULE.validate_candidate_dependencies(
                changed,
                preparation,
                digest,
                upstream_receipts=upstream,
                toolchain_receipt_bytes=toolchain_receipt,
            )

    def test_draft_source_is_rejected_before_publication_or_asset_mutation(self) -> None:
        preparation, contents, _, store = self.stage_fixture()
        stage = preparation["stages"][1]
        key = (str(stage["repository"]), int(stage["releaseId"]))
        store.releases[key] = {
            "id": stage["releaseId"],
            "tag_name": stage["ref"],
            "target_commitish": "f" * 40,
            "draft": True,
            "prerelease": False,
        }
        store.asset_contents[(*key, PREPARATION.PREPARATION_ASSET)] = contents
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "upstream.json"
            receipt.write_text("{}\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "target changed"):
                MODULE.publish_prepared_release(store, stage, [receipt], contents)
        self.assertEqual(0, store.publish_calls)
        self.assertNotIn((*key, "upstream.json"), store.asset_contents)

        store.releases[key]["target_commitish"] = stage["sourceCommit"]
        store.tags[(str(stage["repository"]), str(stage["ref"]))] = "e" * 40
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "upstream.json"
            receipt.write_text("{}\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "tag changed"):
                MODULE.publish_prepared_release(store, stage, [receipt], contents)
        self.assertEqual(0, store.publish_calls)

    def test_run_stage_recovers_an_active_run_without_dispatch(self) -> None:
        preparation, contents, digest, store = self.stage_fixture()
        self.seed_intent(preparation, digest, store)
        active = dict(store.run_by_id["700"])
        active["status"] = "in_progress"
        active["conclusion"] = None
        store.runs = [active]
        receipt = {"publication": {"runId": "700", "runAttempt": "1"}}
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ), patch.object(
            MODULE, "validate_receipt_bytes", return_value=receipt
        ), patch.object(
            MODULE.COORDINATOR, "dispatch_workflow"
        ) as dispatch:
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            result = MODULE.run_stage(
                store, preparation_path, "core-preview", "12345",
                Path(temporary) / "receipt.json",
            )
        self.assertEqual("recovered", result["mode"])
        dispatch.assert_not_called()

    def test_run_stage_recovers_a_successful_run_before_receipt_attachment(self) -> None:
        preparation, contents, digest, store = self.stage_fixture()
        self.seed_intent(preparation, digest, store)
        store.runs = [store.run_by_id["700"]]
        receipt = {"publication": {"runId": "700", "runAttempt": "1"}}
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ), patch.object(
            MODULE, "validate_receipt_bytes", return_value=receipt
        ), patch.object(
            MODULE.COORDINATOR, "dispatch_workflow"
        ) as dispatch:
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            result = MODULE.run_stage(
                store, preparation_path, "core-preview", "12345",
                Path(temporary) / "receipt.json",
            )
        self.assertEqual("recovered", result["mode"])
        dispatch.assert_not_called()

    def test_run_stage_uses_persisted_coordinates_when_listing_omits_run(self) -> None:
        preparation, contents, digest, store = self.stage_fixture()
        stage = preparation["stages"][0]
        key = (str(stage["repository"]), int(stage["releaseId"]))
        attempt = self.seed_intent(preparation, digest, store)
        coordinate = MODULE.dispatch_coordinates(
            stage, digest, {
                "workflowRunId": "700", "dispatchAttemptIdentity": attempt,
            }
        )
        store.asset_contents[(*key, "dispatch-coordinate-core-preview-700.json")] = coordinate
        receipt = {"publication": {"runId": "700", "runAttempt": "1"}}
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ), patch.object(
            MODULE, "validate_receipt_bytes", return_value=receipt
        ), patch.object(
            MODULE.COORDINATOR, "dispatch_workflow"
        ) as dispatch:
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            result = MODULE.run_stage(
                store, preparation_path, "core-preview", "12345",
                Path(temporary) / "receipt.json",
            )
        self.assertEqual("recovered", result["mode"])
        dispatch.assert_not_called()

    def test_missing_persisted_run_fails_closed_without_dispatch(self) -> None:
        preparation, contents, digest, store = self.stage_fixture()
        stage = preparation["stages"][0]
        key = (str(stage["repository"]), int(stage["releaseId"]))
        attempt = self.seed_intent(preparation, digest, store)
        store.asset_contents[(*key, "dispatch-coordinate-core-preview-700.json")] = (
            MODULE.dispatch_coordinates(stage, digest, {
                "workflowRunId": "700", "dispatchAttemptIdentity": attempt,
            })
        )
        del store.run_by_id["700"]
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ), patch.object(
            MODULE.COORDINATOR, "dispatch_workflow"
        ) as dispatch:
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            with self.assertRaises(KeyError):
                MODULE.run_stage(
                    store, preparation_path, "core-preview", "12345",
                    Path(temporary) / "receipt.json",
                )
        dispatch.assert_not_called()

    def test_run_stage_recovers_after_a_lost_dispatch_response(self) -> None:
        _, contents, _, store = self.stage_fixture()
        receipt = {"publication": {"runId": "700", "runAttempt": "1"}}

        def lose_response(*_args, **_kwargs):
            store.runs = [store.run_by_id["700"]]
            raise TimeoutError("response lost")

        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ), patch.object(
            MODULE, "validate_receipt_bytes", return_value=receipt
        ), patch.object(
            MODULE.COORDINATOR, "dispatch_workflow", side_effect=lose_response
        ):
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            result = MODULE.run_stage(
                store, preparation_path, "core-preview", "12345",
                Path(temporary) / "receipt.json",
            )
        self.assertEqual("recovered", result["mode"])

    def test_lost_response_with_delayed_listing_blocks_a_second_dispatch(self) -> None:
        _, contents, _, store = self.stage_fixture()
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "1"}
        ):
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            with patch.object(
                MODULE.COORDINATOR,
                "dispatch_workflow",
                side_effect=TimeoutError("response lost"),
            ) as first_dispatch:
                with self.assertRaisesRegex(RuntimeError, "response was lost"):
                    MODULE.run_stage(
                        store, preparation_path, "core-preview", "12345",
                        Path(temporary) / "first-receipt.json",
                    )
            first_dispatch.assert_called_once()
            with patch.object(
                MODULE.COORDINATOR, "dispatch_workflow"
            ) as second_dispatch:
                with self.assertRaisesRegex(RuntimeError, "no visible workflow run"):
                    MODULE.run_stage(
                        store, preparation_path, "core-preview", "12345",
                        Path(temporary) / "second-receipt.json",
                    )
            second_dispatch.assert_not_called()

    def test_failed_run_does_not_mask_a_later_unresolved_dispatch(self) -> None:
        preparation, contents, digest, store = self.stage_fixture()
        stage = preparation["stages"][0]
        key = (str(stage["repository"]), int(stage["releaseId"]))
        old_attempt = self.seed_intent(preparation, digest, store, "b" * 64)
        store.asset_contents[(*key, "dispatch-coordinate-core-preview-700.json")] = (
            MODULE.dispatch_coordinates(stage, digest, {
                "workflowRunId": "700",
                "dispatchAttemptIdentity": old_attempt,
            })
        )
        store.run_by_id["700"]["status"] = "completed"
        store.run_by_id["700"]["conclusion"] = "failure"
        with tempfile.TemporaryDirectory() as temporary, patch.dict(
            "os.environ", {"GITHUB_RUN_ID": "800", "GITHUB_RUN_ATTEMPT": "2"}
        ):
            preparation_path = Path(temporary) / "release-preparation.json"
            preparation_path.write_bytes(contents)
            with patch.object(
                MODULE.COORDINATOR,
                "dispatch_workflow",
                side_effect=TimeoutError("response lost"),
            ) as first_dispatch:
                with self.assertRaisesRegex(RuntimeError, "response was lost"):
                    MODULE.run_stage(
                        store, preparation_path, "core-preview", "12345",
                        Path(temporary) / "first-receipt.json",
                    )
            first_dispatch.assert_called_once()
            with patch.object(
                MODULE.COORDINATOR, "dispatch_workflow"
            ) as second_dispatch:
                with self.assertRaisesRegex(RuntimeError, "no visible workflow run"):
                    MODULE.run_stage(
                        store, preparation_path, "core-preview", "12345",
                        Path(temporary) / "second-receipt.json",
                    )
            second_dispatch.assert_not_called()

    def test_receipt_must_name_the_awaited_run_and_attempt(self) -> None:
        preparation, _, _, store = self.stage_fixture()
        stage = preparation["stages"][0]
        receipt = {"publication": {"runId": "700", "runAttempt": "1"}}
        with self.assertRaisesRegex(ValueError, "completed workflow run"):
            MODULE.validate_receipt_run(
                store, receipt, stage, "12345", "701", "1"
            )


if __name__ == "__main__":
    unittest.main()

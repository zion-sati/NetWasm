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
        self.publish_calls = 0

    def release(self, repository: str, release_id: int) -> dict[str, object]:
        return self.releases[(repository, release_id)]

    def asset_bytes(
        self, repository: str, release_id: int, name: str
    ) -> bytes | None:
        return self.asset_contents.get((repository, release_id, name))

    def upload_asset(
        self, repository: str, release_id: int, name: str, contents: bytes
    ) -> None:
        self.asset_contents[(repository, release_id, name)] = contents

    def assets(self, repository: str, release_id: int) -> list[dict[str, object]]:
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

    def workflow_runs(
        self, repository: str, workflow: str, ref: str
    ) -> list[dict[str, object]]:
        return self.runs

    def workflow_run(self, repository: str, run_id: str) -> dict[str, object]:
        return self.run_by_id[run_id]

    def publish_release(self, repository: str, release_id: int) -> dict[str, object]:
        self.publish_calls += 1
        release = self.releases[(repository, release_id)]
        release["draft"] = False
        return release


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

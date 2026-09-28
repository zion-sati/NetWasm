from __future__ import annotations

import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).parents[1]


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


MODULE = load("release_receiver", ROOT / "release-receiver.py")
PREPARATION = load("release_preparation_for_receiver", ROOT / "release-preparation.py")
COORDINATOR = load("release_coordinator_for_receiver", ROOT / "release-coordinator.py")


class ReleaseReceiverTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
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
        self.preparation = PREPARATION.create_preparation(coordinates, release_ids)
        self.preparation_path = self.root / "release-preparation.json"
        self.preparation_path.write_text(json.dumps(self.preparation, indent=2) + "\n")
        request = COORDINATOR.create_dispatch(
            preparation_path=self.preparation_path,
            stage_name="core-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={},
        )
        self.inputs_path = self.root / "inputs.json"
        self.inputs_path.write_text(json.dumps(request["inputs"]))
        self.stage = self.preparation["stages"][0]
        self.release_path = self.root / "release.json"
        self.release_path.write_text(json.dumps({
            "id": self.stage["releaseId"],
            "tag_name": self.stage["ref"],
            "draft": False,
            "prerelease": True,
        }))
        self.upstream = self.root / "upstream"
        self.upstream.mkdir()
        self.upstream_runs = self.root / "upstream-runs"
        self.upstream_runs.mkdir()

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def verify(self, **changes):
        values = {
            "inputs_path": self.inputs_path,
            "preparation_path": self.preparation_path,
            "release_path": self.release_path,
            "upstream_directory": self.upstream,
            "upstream_run_directory": self.upstream_runs,
            "repository": "zion-sati/NetWasm",
            "event_name": "workflow_dispatch",
            "workflow_identity": (
                "zion-sati/NetWasm/.github/workflows/release.yml@refs/heads/main"
            ),
            "workflow_sha": self.stage["workflowCommit"],
            "workflow_ref": self.stage["workflowRef"],
            "actor_id": "12345",
            "approved_actor_id": "12345",
            "tag_commit": self.stage["sourceCommit"],
        }
        values.update(changes)
        return MODULE.verify_receiver(**values)

    def test_receiver_accepts_only_exact_prepared_release_dispatch(self) -> None:
        result = self.verify()
        self.assertEqual("PASS", result["status"])
        self.assertEqual("7.8.9-preview.1", result["version"])

        mutations = {
            "repository": "zion-sati/Other",
            "event_name": "release",
            "workflow_identity": (
                "zion-sati/NetWasm/.github/workflows/other.yml@refs/heads/main"
            ),
            "workflow_sha": "9" * 40,
            "workflow_ref": "other",
            "actor_id": "54321",
            "tag_commit": "8" * 40,
        }
        for field, value in mutations.items():
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.verify(**{field: value})

        changed = json.loads(self.inputs_path.read_text())
        changed["source_commit"] = "7" * 40
        bad_inputs = self.root / "bad-inputs.json"
        bad_inputs.write_text(json.dumps(changed))
        with self.assertRaisesRegex(ValueError, "do not match preparation"):
            self.verify(inputs_path=bad_inputs)

        self.assertEqual(
            hashlib.sha256(self.preparation_path.read_bytes()).hexdigest(),
            result["preparationSha256"],
        )

    def test_upstream_receipt_requires_exact_successful_workflow_run(self) -> None:
        stage = self.preparation["stages"][0]
        receipt = {
            "stage": "core-preview",
            "publication": {"runId": "900", "runAttempt": "2"},
        }
        run_path = self.upstream_runs / "core-preview.json"
        run = {
            "id": 900,
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
        run_path.write_text(json.dumps(run))
        MODULE.validate_upstream_runs(
            [(self.root / "receipt.json", receipt)],
            self.upstream_runs,
            self.preparation,
            "12345",
        )

        for field, value in (
            ("status", "in_progress"),
            ("conclusion", "failure"),
            ("id", 901),
            ("run_attempt", 3),
        ):
            with self.subTest(field=field):
                changed = dict(run, **{field: value})
                run_path.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError, "did not complete"):
                    MODULE.validate_upstream_runs(
                        [(self.root / "receipt.json", receipt)],
                        self.upstream_runs,
                        self.preparation,
                        "12345",
                    )


if __name__ == "__main__":
    unittest.main()

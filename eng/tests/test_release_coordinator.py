from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "release-coordinator.py"
SPEC = importlib.util.spec_from_file_location("release_coordinator", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class Response(io.BytesIO):
    status = 200

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


class ReleaseCoordinatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        commits = {
            "zion-sati/NetWasm": "a" * 40,
            "zion-sati/TUnit-NetWasm": "b" * 40,
            "zion-sati/NetWasm.Libraries": "c" * 40,
            "zion-sati/NetWasm.Playground": "d" * 40,
            "zion-sati/netwasm.com": "e" * 40,
        }
        stages = []
        release_id = 100
        for name, repository, workflow, upstream, _ in MODULE.TRAIN.PREPARATION_STAGES:
            release_id += 1
            website = name == "website"
            workflow_commit = "f" * 40 if website else commits[repository]
            stages.append({
                "name": name,
                "repository": repository,
                "workflow": workflow,
                "sourceCommit": commits[repository],
                "infrastructureCommit": commits[repository],
                "workflowCommit": workflow_commit,
                "workflowRef": "main" if website else (
                    MODULE.TRAIN.expected_stage_ref(name, "7.8.9")
                ),
                "ref": commits[repository] if website else (
                    MODULE.TRAIN.expected_stage_ref(name, "7.8.9")
                ),
                "releaseId": None if website else release_id,
                "prerelease": name.endswith("-preview"),
                "upstreamStages": list(upstream),
            })
        self.preparation = {
            "schemaVersion": 1,
            "version": "7.8.9",
            "stages": stages,
            "policy": {
                "publicationReceiptSchemaVersion": 1,
                "completionStage": "website",
            },
        }
        self.path = self.root / "preparation.json"
        self.path.write_text(json.dumps(self.preparation, indent=2) + "\n")

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def receipt(self, stage: str) -> Path:
        path = self.root / f"{stage}.json"
        path.write_text(json.dumps({"stage": stage}) + "\n")
        return path

    def test_dispatch_binds_stable_stage_and_unique_attempt(self) -> None:
        receipt = self.receipt("core-stable")
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="tunit-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="2",
            upstream_receipt_paths={"core-stable": receipt},
        )
        digest = hashlib.sha256(self.path.read_bytes()).hexdigest()

        self.assertEqual("zion-sati/TUnit-NetWasm", request["repository"])
        self.assertEqual("netwasm-v7.8.9-preview.1", request["ref"])
        self.assertEqual(digest, request["inputs"]["preparation_sha256"])
        self.assertEqual(
            MODULE.stage_identity(digest, "tunit-preview"),
            request["inputs"]["stage_identity"],
        )
        self.assertEqual(
            [{
                "fileName": "core-stable.json",
                "sha256": MODULE.TRAIN.sha256(receipt),
                "stage": "core-stable",
            }],
            json.loads(request["inputs"]["upstream_receipts"]),
        )
        retried = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="tunit-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="701",
            coordinator_run_attempt="1",
            upstream_receipt_paths={"core-stable": receipt},
        )
        self.assertEqual(
            request["inputs"]["stage_identity"],
            retried["inputs"]["stage_identity"],
        )
        self.assertNotEqual(
            request["inputs"]["dispatch_attempt_identity"],
            retried["inputs"]["dispatch_attempt_identity"],
        )

    def test_dispatch_rejects_missing_or_extra_upstream_receipts(self) -> None:
        receipt = self.receipt("core-stable")
        for receipts in ({}, {"core-stable": receipt, "extra": receipt}):
            with self.subTest(receipts=receipts), self.assertRaisesRegex(
                ValueError, "receipt set"
            ):
                MODULE.create_dispatch(
                    preparation_path=self.path,
                    stage_name="tunit-preview",
                    coordinator_repository="zion-sati/NetWasm",
                    coordinator_run_id="700",
                    coordinator_run_attempt="1",
                    upstream_receipt_paths=receipts,
                )

    def test_dispatch_rejects_bad_upstream_coordinates_for_every_stage(self) -> None:
        cases = (
            ("tunit-preview", ("core-stable",)),
            (
                "playground",
                ("core-stable", "tunit-stable", "libraries-stable"),
            ),
        )
        for stage_name, upstream_names in cases:
            receipt_paths = {
                name: self.receipt(name) for name in upstream_names
            }
            request = MODULE.create_dispatch(
                preparation_path=self.path,
                stage_name=stage_name,
                coordinator_repository="zion-sati/NetWasm",
                coordinator_run_id="700",
                coordinator_run_attempt="1",
                upstream_receipt_paths=receipt_paths,
            )
            for field, value in (
                ("sha256", "bad"),
                ("fileName", "../outside.json"),
            ):
                with self.subTest(stage=stage_name, field=field):
                    changed = json.loads(json.dumps(request))
                    upstream = json.loads(changed["inputs"]["upstream_receipts"])
                    upstream[0][field] = value
                    changed["inputs"]["upstream_receipts"] = json.dumps(upstream)
                    with self.assertRaisesRegex(ValueError, "coordinates"):
                        MODULE.validate_dispatch_request(changed, self.path)

    def test_workflow_dispatch_requests_and_returns_exact_run(self) -> None:
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="core-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={},
        )
        observed = {}

        def send(api_request, timeout):
            observed["url"] = api_request.full_url
            observed["method"] = api_request.method
            observed["headers"] = dict(api_request.headers)
            observed["body"] = json.loads(api_request.data)
            return Response(json.dumps({
                "workflow_run_id": 900,
                "run_url": "https://api.github.com/repos/zion-sati/NetWasm/actions/runs/900",
                "html_url": "https://github.com/zion-sati/NetWasm/actions/runs/900",
            }).encode())

        dispatch = MODULE.dispatch_workflow(
            request, "installation-token", self.path, open_request=send
        )

        self.assertEqual("POST", observed["method"])
        self.assertEqual(
            "https://api.github.com/repos/zion-sati/NetWasm/actions/"
            "workflows/release.yml/dispatches",
            observed["url"],
        )
        self.assertTrue(observed["body"]["return_run_details"])
        self.assertEqual(request["ref"], observed["body"]["ref"])
        self.assertEqual("900", dispatch["workflowRunId"])
        self.assertEqual(
            request["inputs"]["stage_identity"], dispatch["stageIdentity"]
        )

    def test_exact_run_must_match_workflow_event_source_and_success(self) -> None:
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="core-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={},
        )
        dispatch = {"workflowRunId": "900"}
        run = {
            "id": 900,
            "event": "workflow_dispatch",
            "head_sha": "a" * 40,
            "head_branch": "v7.8.9-preview.1",
            "path": ".github/workflows/release.yml",
            "status": "completed",
            "conclusion": "success",
        }
        MODULE.validate_workflow_run(run, request, dispatch, require_complete=True)

        mutations = {
            "id": 901,
            "event": "release",
            "head_sha": "9" * 40,
            "head_branch": "v7.8.8-preview.1",
            "path": ".github/workflows/other.yml",
            "status": "in_progress",
            "conclusion": None,
        }
        for field, value in mutations.items():
            with self.subTest(field=field):
                changed = dict(run, **{field: value})
                with self.assertRaises(ValueError):
                    MODULE.validate_workflow_run(
                        changed, request, dispatch, require_complete=True
                    )

    def test_verify_run_command_writes_result(self) -> None:
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="core-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={},
        )
        paths = {
            "request": self.root / "request.json",
            "dispatch": self.root / "dispatch.json",
            "run": self.root / "run.json",
        }
        paths["request"].write_text(json.dumps(request))
        paths["dispatch"].write_text(json.dumps({"workflowRunId": "900"}))
        paths["run"].write_text(json.dumps({
            "id": 900,
            "event": "workflow_dispatch",
            "head_sha": "a" * 40,
            "head_branch": "v7.8.9-preview.1",
            "path": ".github/workflows/release.yml",
            "status": "completed",
            "conclusion": "success",
        }))
        output = self.root / "verified.json"

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "verify-run",
                "--request", str(paths["request"]),
                "--dispatch", str(paths["dispatch"]),
                "--run", str(paths["run"]),
                "--require-complete",
                "--output", str(output),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("PASS", json.loads(output.read_text())["status"])

    def test_website_uses_distinct_workflow_ref_and_content_identity(self) -> None:
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="website",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={"playground": self.receipt("playground")},
        )
        self.assertEqual("main", request["ref"])
        self.assertEqual("f" * 40, request["expectedWorkflowSha"])
        self.assertEqual("e" * 40, request["inputs"]["source_commit"])
        self.assertEqual("", request["inputs"]["release_id"])
        self.assertEqual("101", request["inputs"]["preparation_release_id"])
        self.assertEqual([], json.loads(request["inputs"]["retained_candidates"]))

        changed = json.loads(json.dumps(request))
        changed["inputs"]["preparation_release_id"] = "999"
        with self.assertRaisesRegex(ValueError, "identity"):
            MODULE.validate_dispatch_request(changed, self.path)

    def test_delivery_dispatch_binds_ordered_retained_candidates(self) -> None:
        receipts = {
            name: self.receipt(name)
            for name in ("core-stable", "tunit-stable", "libraries-stable")
        }
        retained = [
            {
                "kind": "playground-toolchain-candidate",
                "receiptSha256": "1" * 64,
            },
            {
                "kind": "playground-site-candidate",
                "receiptSha256": "2" * 64,
            },
        ]
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="playground",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths=receipts,
            retained_candidates=retained,
        )
        self.assertEqual(
            retained, json.loads(request["inputs"]["retained_candidates"])
        )
        MODULE.validate_dispatch_request(request, self.path)

        for invalid in (
            [retained[1]],
            [retained[0], retained[0]],
            [{"kind": "website-site-candidate", "receiptSha256": "3" * 64}],
        ):
            with self.subTest(invalid=invalid), self.assertRaisesRegex(
                ValueError, "candidate"
            ):
                MODULE.create_dispatch(
                    preparation_path=self.path,
                    stage_name="playground",
                    coordinator_repository="zion-sati/NetWasm",
                    coordinator_run_id="700",
                    coordinator_run_attempt="1",
                    upstream_receipt_paths=receipts,
                    retained_candidates=invalid,
                )

    def test_dispatch_rejects_tampered_target_or_identity(self) -> None:
        request = MODULE.create_dispatch(
            preparation_path=self.path,
            stage_name="core-preview",
            coordinator_repository="zion-sati/NetWasm",
            coordinator_run_id="700",
            coordinator_run_attempt="1",
            upstream_receipt_paths={},
        )
        mutations = (
            ("repository", "zion-sati/Other"),
            ("workflow", ".github/workflows/other.yml"),
            ("ref", "main?unexpected=true"),
        )
        for field, value in mutations:
            with self.subTest(field=field):
                changed = json.loads(json.dumps(request))
                changed[field] = value
                with self.assertRaisesRegex(ValueError, "target"):
                    MODULE.validate_dispatch_request(changed, self.path)

        changed = json.loads(json.dumps(request))
        changed["inputs"]["dispatch_attempt_identity"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "attempt identity"):
            MODULE.validate_dispatch_request(changed, self.path)


if __name__ == "__main__":
    unittest.main()

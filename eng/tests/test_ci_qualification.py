import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile


SCRIPT = Path(__file__).resolve().parents[1] / "ci-qualification.py"
SPEC = importlib.util.spec_from_file_location("ci_qualification", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
QUALIFICATION = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = QUALIFICATION
SPEC.loader.exec_module(QUALIFICATION)


def archive(receipt):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as bundle:
        bundle.writestr(QUALIFICATION.RECEIPT_NAME, json.dumps(receipt))
    return output.getvalue()


class CiQualificationTests(unittest.TestCase):
    def test_validates_exact_repository_head_tree_workflow_and_run(self):
        receipt = {
            "schemaVersion": 1,
            "repository": "zion-sati/NetWasm",
            "eventName": "pull_request",
            "pullRequestHead": "head",
            "checkedOutTree": "tree",
            "workflowSha256": "workflow",
            "workflowRunId": "42",
        }
        QUALIFICATION.validate_receipt(
            receipt,
            repository="zion-sati/NetWasm",
            pull_request_head="head",
            tree="tree",
            workflow_sha256="workflow",
            run_id=42,
        )
        for field in receipt:
            changed = dict(receipt)
            changed[field] = "wrong"
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "does not match"):
                QUALIFICATION.validate_receipt(
                    changed,
                    repository="zion-sati/NetWasm",
                    pull_request_head="head",
                    tree="tree",
                    workflow_sha256="workflow",
                    run_id=42,
                )

    def test_archive_rejects_extra_entries_and_large_receipts(self):
        with tempfile.TemporaryFile() as file:
            with zipfile.ZipFile(file, "w") as bundle:
                bundle.writestr(QUALIFICATION.RECEIPT_NAME, "{}")
                bundle.writestr("extra", "bad")
            file.seek(0)
            with self.assertRaisesRegex(ValueError, "unexpected contents"):
                QUALIFICATION.receipt_from_archive(file.read())
        with self.assertRaisesRegex(ValueError, "size limit"):
            QUALIFICATION.receipt_from_archive(archive({
                "padding": "x" * QUALIFICATION.MAX_RECEIPT_BYTES,
            }))

    def test_finds_matching_successful_run_artifact_for_two_parent_merge(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = Path(".github/workflows/ci.yml")
            (root / workflow).parent.mkdir(parents=True)
            (root / workflow).write_text("name: CI\n")
            receipt = {
                "schemaVersion": 1,
                "repository": "zion-sati/NetWasm",
                "eventName": "pull_request",
                "pullRequestHead": "head",
                "checkedOutTree": "tree",
                "workflowSha256": hashlib.sha256((root / workflow).read_bytes()).hexdigest(),
                "workflowRunId": "42",
            }
            responses = iter([
                {"workflow_runs": [{"id": 42, "head_sha": "head"}]},
                {"artifacts": [{
                    "name": QUALIFICATION.ARTIFACT_NAME,
                    "expired": False,
                    "archive_download_url": "download",
                }]},
            ])
            original_git = QUALIFICATION.git
            QUALIFICATION.git = lambda _root, *args: {
                ("rev-parse", "HEAD"): "merge",
                ("rev-list", "--parents", "-n", "1", "merge"): "merge base head",
                ("rev-parse", "merge^{tree}"): "tree",
                ("rev-parse", "head^{tree}"): "tree",
            }[args]
            try:
                run_id = QUALIFICATION.find_qualification(
                    root,
                    "zion-sati/NetWasm",
                    workflow,
                    lambda _url: next(responses),
                    lambda _url: archive(receipt),
                )
            finally:
                QUALIFICATION.git = original_git
        self.assertEqual(42, run_id)


if __name__ == "__main__":
    unittest.main()

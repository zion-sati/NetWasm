from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).parents[1] / "verify-release-ci.py"
SPEC = importlib.util.spec_from_file_location("verify_release_ci", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class VerifyReleaseCiTests(unittest.TestCase):
    def test_accepts_only_exact_successful_public_main_push(self) -> None:
        commit = "a" * 40
        seen = []

        def fetch(url: str):
            seen.append(url)
            return {"workflow_runs": [
                {"id": 41, "head_sha": commit, "head_branch": "feature",
                 "event": "pull_request", "status": "completed", "conclusion": "success"},
                {"id": 42, "head_sha": commit, "head_branch": "main",
                 "event": "push", "status": "completed", "conclusion": "success"},
            ]}

        run_id = MODULE.find_successful_run(
            "zion-sati/NetWasm", ".github/workflows/ci.yml", commit, "main", fetch
        )

        self.assertEqual(42, run_id)
        self.assertIn("head_sha=" + commit, seen[0])
        self.assertIn("event=push", seen[0])
        self.assertIn("status=success", seen[0])

    def test_rejects_missing_exact_success_and_invalid_commit(self) -> None:
        commit = "b" * 40
        with self.assertRaisesRegex(ValueError, "No successful"):
            MODULE.find_successful_run(
                "zion-sati/NetWasm", "ci.yml", commit, "main",
                lambda _url: {"workflow_runs": [{
                    "id": 1, "head_sha": "c" * 40, "head_branch": "main",
                    "event": "push", "status": "completed", "conclusion": "success",
                }]},
            )
        with self.assertRaisesRegex(ValueError, "full lowercase"):
            MODULE.find_successful_run(
                "zion-sati/NetWasm", "ci.yml", "short", "main",
                lambda _url: {"workflow_runs": []},
            )


if __name__ == "__main__":
    unittest.main()

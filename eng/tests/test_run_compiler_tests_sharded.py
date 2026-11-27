import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


REPOSITORY = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / "eng" / "run-compiler-tests-sharded.py"
SPEC = importlib.util.spec_from_file_location("compiler_test_runner", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class CompilerTestRunnerCoverageTests(unittest.TestCase):
    @staticmethod
    def report(hits):
        return {"Compiler.dll": {"File.cs": {"Class": {
            "Method": {"Lines": {"1": hits}, "Branches": []},
        }}}}

    def execute(self, root, *, failed=False, missing=False, coverage=True):
        output = root / "coverage.json"
        output.write_text(json.dumps(self.report(3)), encoding="utf-8")
        outcomes = []
        for index in range(2):
            report = root / f"shard-{index}.json"
            if not (missing and index == 1):
                report.write_text(json.dumps(self.report(index + 1)), encoding="utf-8")
            log = root / f"shard-{index}.log"
            log.write_text("test outcome\n", encoding="utf-8")
            outcomes.append({
                "index": index,
                "seconds": 0.1,
                "exitCode": 1 if failed and index == 1 else 0,
                "coverage": str(report),
                "log": str(log),
            })
        arguments = [
            str(SCRIPT), "--source", str(REPOSITORY),
            "--results-directory", str(root / "results"), "--shard-count", "2",
        ]
        if coverage:
            arguments.extend(["--coverage-output", str(output)])
        listing = subprocess.CompletedProcess([], 0,
            "The following Tests are available:\n"
            "    NetWasm.Compiler.Tests.FirstTests.Run\n"
            "    NetWasm.Compiler.Tests.SecondTests.Run\n", "")
        shard_module = RUNNER.load_shard_module(REPOSITORY)
        weights = {
            "NetWasm.Compiler.Tests.FirstTests.Run": 1.0,
            "NetWasm.Compiler.Tests.SecondTests.Run": 1.0,
        }
        with (
            patch.object(sys, "argv", arguments),
            patch.object(RUNNER.subprocess, "run", return_value=listing),
            patch.object(RUNNER, "find_test_output", return_value=root),
            patch.object(RUNNER, "load_shard_module", return_value=shard_module),
            patch.object(shard_module, "load_timing_weights", return_value=(weights, 2)),
            patch.object(RUNNER, "run_shard", side_effect=lambda **kwargs: outcomes[kwargs["index"]]),
            patch.object(sys, "stdout", io.StringIO()),
            patch.object(sys, "stderr", io.StringIO()),
        ):
            result = RUNNER.main()
        return result, json.loads(output.read_text(encoding="utf-8"))

    def test_failed_shard_still_merges_all_coverage_and_retains_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, report = self.execute(Path(temporary), failed=True)
        self.assertEqual(1, result)
        self.assertEqual(self.report(6), report)

    def test_successful_shards_merge_existing_and_new_coverage(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, report = self.execute(Path(temporary))
        self.assertEqual(0, result)
        self.assertEqual(self.report(6), report)

    def test_missing_report_fails_without_publishing_partial_coverage(self):
        for failed in (False, True):
            with self.subTest(failed=failed), tempfile.TemporaryDirectory() as temporary:
                result, report = self.execute(Path(temporary), failed=failed, missing=True)
            self.assertEqual(1, result)
            self.assertEqual(self.report(3), report)

    def test_failure_without_coverage_keeps_the_failure_verdict(self):
        with tempfile.TemporaryDirectory() as temporary:
            result, report = self.execute(Path(temporary), failed=True, coverage=False)
        self.assertEqual(1, result)
        self.assertEqual(self.report(3), report)


if __name__ == "__main__":
    unittest.main()

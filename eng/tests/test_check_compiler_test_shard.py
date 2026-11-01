import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check-compiler-test-shard.py"
SPEC = importlib.util.spec_from_file_location("check_compiler_test_shard", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
CHECK = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = CHECK
SPEC.loader.exec_module(CHECK)

FACT = "NetWasm.Compiler.Tests.AlphaTests.Fact"
THEORY = "NetWasm.Compiler.Tests.AlphaTests.Theory"
SPLIT = "NetWasm.Compiler.Tests.GuardTests.Rejects"


def trx(*names):
    results = "".join(
        f'<UnitTestResult testName="{name.replace(chr(34), "&quot;")}" outcome="Passed" />'
        for name in names
    )
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f"<Results>{results}</Results></TestRun>"
    )


class CheckCompilerTestShardTests(unittest.TestCase):
    def run_check(self, plan, *results):
        with tempfile.TemporaryDirectory() as directory:
            plan_path = Path(directory) / "plan.json"
            plan_path.write_text(json.dumps({"items": plan}), encoding="utf-8")
            result_paths = []
            for index, names in enumerate(results):
                path = Path(directory) / f"results-{index}.trx"
                path.write_text(trx(*names), encoding="utf-8")
                result_paths.append(str(path))
            with mock.patch.object(sys, "argv", ["check", str(plan_path), *result_paths]):
                with mock.patch.object(sys, "stderr") as stderr:
                    code = CHECK.main()
            return code, "".join(call.args[0] for call in stderr.write.call_args_list)

    def test_a_shard_that_ran_its_plan_passes(self):
        case = f'{SPLIT}(actor: "NetWasm.Compiler.Analysis.AddressTakenValues.Local"···, missing: 0)'
        plan = [
            {"method": FACT, "displayName": None},
            {"method": THEORY, "displayName": None},
            {"method": SPLIT, "displayName": case},
        ]

        code, _ = self.run_check(plan, [FACT], [f"{THEORY}(value: 1)", case])

        self.assertEqual(0, code)

    def test_a_shard_that_skipped_planned_tests_fails_and_names_them(self):
        case = f'{SPLIT}(actor: "NetWasm.Compiler.Analysis.Constants.DeadJumpRewriter", missing: 1)'
        plan = [
            {"method": FACT, "displayName": None},
            {"method": THEORY, "displayName": None},
            {"method": SPLIT, "displayName": case},
        ]

        code, output = self.run_check(plan, [f"{FACT}Other", f"{THEORY}Other(value: 1)"])

        self.assertEqual(1, code)
        self.assertIn("did not run 3 of the tests", output)
        self.assertIn(case, output)
        self.assertIn(FACT, output)

    def test_long_lists_of_missing_tests_are_shortened(self):
        plan = [{"method": f"{FACT}{index}", "displayName": None} for index in range(25)]

        code, output = self.run_check(plan, [])

        self.assertEqual(1, code)
        self.assertIn("and 5 more", output)

    def test_invalid_plans_and_results_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "plan.json"
            for content, message in [
                ("not json", "Cannot read the shard plan"),
                ('{"items": []}', "selects nothing"),
                ('[]', "selects nothing"),
                ('{"items": [{"method": 1}]}', "Invalid shard plan item"),
                ('{"items": [{"method": "A", "displayName": 2}]}', "Invalid shard plan item"),
                ('{"items": ["A"]}', "Invalid shard plan item"),
            ]:
                path.write_text(content, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    CHECK.load_plan(path)
            with self.assertRaisesRegex(ValueError, "Cannot read the shard plan"):
                CHECK.load_plan(Path(directory) / "missing.json")
            results = Path(directory) / "results.trx"
            results.write_text("<TestRun>", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Cannot read test results"):
                CHECK.load_results([results])

    def test_an_unreadable_plan_is_a_usage_error(self):
        with tempfile.TemporaryDirectory() as directory:
            with mock.patch.object(sys, "argv", ["check", str(Path(directory) / "missing.json"), "x.trx"]):
                with mock.patch.object(sys, "stderr"):
                    with self.assertRaises(SystemExit) as exit:
                        CHECK.main()
        self.assertEqual(2, exit.exception.code)


if __name__ == "__main__":
    unittest.main()

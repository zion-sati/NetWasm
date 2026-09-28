import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "merge-compiler-test-timings.py"
SPEC = importlib.util.spec_from_file_location("merge_compiler_test_timings", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
TIMINGS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = TIMINGS
SPEC.loader.exec_module(TIMINGS)


TRX = '''<?xml version="1.0" encoding="UTF-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="NetWasm.Compiler.Tests.AlphaTests.Fact" duration="00:00:01.2500000" />
    <UnitTestResult testName="NetWasm.Compiler.Tests.AlphaTests.Theory(value: 1)" duration="00:00:00.7500000" />
    <UnitTestResult testName="NetWasm.Compiler.Tests.Correctness.BetaTests.Run" duration="00:01:01.5000000" />
    <UnitTestResult testName="Unrelated.Tests.Other.Run" duration="00:00:20.0000000" />
  </Results>
</TestRun>
'''


class CompilerTestTimingTests(unittest.TestCase):
    def test_merges_class_case_counts_and_durations(self):
        with tempfile.TemporaryDirectory() as temporary:
            first = Path(temporary, "first.trx")
            second = Path(temporary, "second.trx")
            first.write_text(TRX)
            second.write_text(TRX.replace("AlphaTests", "GammaTests"))

            result = TIMINGS.merge([second, first])

        self.assertEqual(1, result["schemaVersion"])
        self.assertEqual(
            {"cases": 2, "durationSeconds": 2.0},
            result["classes"]["NetWasm.Compiler.Tests.AlphaTests"],
        )
        self.assertEqual(
            {"cases": 2, "durationSeconds": 123.0},
            result["classes"]["NetWasm.Compiler.Tests.Correctness.BetaTests"],
        )

    def test_rejects_invalid_duration_and_empty_inputs(self):
        with self.assertRaisesRegex(ValueError, "invalid TRX duration"):
            TIMINGS.parse_duration("bad")
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary, "empty.trx")
            path.write_text("<TestRun><Results /></TestRun>")
            with self.assertRaisesRegex(ValueError, "no NetWasm compiler test results"):
                TIMINGS.merge([path])


if __name__ == "__main__":
    unittest.main()

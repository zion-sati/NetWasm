import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "merge-coverlet-reports.py"
SPEC = importlib.util.spec_from_file_location("coverlet_reports", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
REPORTS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = REPORTS
SPEC.loader.exec_module(REPORTS)


def report(line_hits: int, branch_hits: int, ordinal: int = 0) -> dict:
    return {"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {
        "Lines": {"10": line_hits},
        "Branches": [{"Line": 10, "Offset": 4, "EndOffset": 8, "Path": 0,
                      "Ordinal": ordinal, "Hits": branch_hits}],
    }}}}}


class MergeCoverletReportsTests(unittest.TestCase):
    def merge(self, *reports: dict) -> dict:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = []
            for index, value in enumerate(reports):
                path = root / f"{index}.json"
                path.write_text(json.dumps(value), encoding="utf-8")
                paths.append(path)
            output = root / "merged" / "coverage.json"
            REPORTS.merge_coverlet_reports(paths, output)
            return json.loads(output.read_text(encoding="utf-8"))

    def test_sums_line_hits_and_the_hits_of_the_same_branch(self):
        merged = self.merge(report(2, 1), report(3, 0))
        method = merged["Compiler.dll"]["Compiler.cs"]["Compiler"]["Run"]
        self.assertEqual({"10": 5}, method["Lines"])
        self.assertEqual(1, len(method["Branches"]))
        self.assertEqual(1, method["Branches"][0]["Hits"])

    # A line or branch run more than 2^31 times in one report wraps Coverlet's
    # 32-bit counter negative; it was still run.
    def test_reads_a_wrapped_hit_count_as_run(self):
        merged = self.merge(report(-2066561544, -1), report(3, 0))
        method = merged["Compiler.dll"]["Compiler.cs"]["Compiler"]["Run"]
        self.assertEqual({"10": 2**32 - 2066561544 + 3}, method["Lines"])
        self.assertEqual(2**32 - 1, method["Branches"][0]["Hits"])

    def test_keeps_distinct_branches_and_methods_apart(self):
        other = report(1, 4, ordinal=1)
        other["Compiler.dll"]["Compiler.cs"]["Compiler"]["Stop"] = {"Lines": {"20": 0}, "Branches": []}
        merged = self.merge(report(0, 0), other)
        methods = merged["Compiler.dll"]["Compiler.cs"]["Compiler"]
        self.assertEqual([0, 4], sorted(branch["Hits"] for branch in methods["Run"]["Branches"]))
        self.assertEqual({"20": 0}, methods["Stop"]["Lines"])

    def test_rejects_malformed_reports(self):
        cases = [
            ([], "not a JSON object"),
            ({"Compiler.dll": []}, "Invalid file map"),
            ({"Compiler.dll": {"Compiler.cs": []}}, "Invalid class map"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": []}}}, "Invalid method map"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": []}}}}, "Invalid coverage"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {"Lines": [], "Branches": []}}}}},
             "Invalid Coverlet method shape"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {"Lines": {"1": -(2**31) - 1}, "Branches": []}}}}},
             "Invalid line hit count"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {"Lines": {"1": "1"}, "Branches": []}}}}},
             "Invalid line hit count"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {"Lines": {}, "Branches": [{"Line": 1}]}}}}},
             "Incomplete branch coverage"),
            ({"Compiler.dll": {"Compiler.cs": {"Compiler": {"Run": {"Lines": {}, "Branches": [
                {"Line": 1, "Offset": 0, "EndOffset": 1, "Path": 0, "Ordinal": 0, "Hits": True}]}}}}},
             "Invalid branch hit count"),
        ]
        for value, message in cases:
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                self.merge(value)

    def test_rejects_an_unreadable_report(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "broken.json"
            path.write_text("{", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Cannot read Coverlet report"):
                REPORTS.merge_coverlet_reports([path], Path(directory) / "out.json")

    def test_command_line_merges_into_the_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first, second, output = root / "a.json", root / "b.json", root / "merged.json"
            first.write_text(json.dumps(report(1, 1)), encoding="utf-8")
            second.write_text(json.dumps(report(1, 1)), encoding="utf-8")
            self.assertEqual(0, REPORTS.main(["--output", str(output), str(first), str(second)]))
            merged = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(2, merged["Compiler.dll"]["Compiler.cs"]["Compiler"]["Run"]["Lines"]["10"])
            first.write_text("[]", encoding="utf-8")
            with self.assertRaises(SystemExit):
                REPORTS.main(["--output", str(output), str(first)])


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest


HARNESS = Path(__file__).parents[2] / "compiler-qualification" / "micro-benchmarks"


def load(name: str):
    spec = importlib.util.spec_from_file_location(name, HARNESS / f"{name}.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


TABLE = load("table")
RUN = load("run")


class FamilyTests(unittest.TestCase):
    def test_names_each_case_by_its_namespace(self) -> None:
        self.assertEqual("NetWasm workloads", TABLE.family("NetWasm.Workloads.TrialDivision.LargestPrimeRemainder()"))
        self.assertEqual("Math", TABLE.family("System.MathBenchmarks.Double.Sqrt()"))
        self.assertEqual("`System.Memory` spans", TABLE.family("System.Memory.Span<Int32>.Reverse(Size: 512)"))
        self.assertEqual("`System.Numerics` vectors", TABLE.family("System.Numerics.Tests.Perf_Vector2.AddOperator()"))
        self.assertEqual("`System.Runtime.Intrinsics`",
                         TABLE.family("System.Runtime.Intrinsics.Tests.Perf_Vector128Of<Byte>.AddBenchmark()"))
        self.assertEqual("Runtime", TABLE.family("Benchstone.BenchI.IniArray.Test()"))
        self.assertEqual("Runtime", TABLE.family("System.Tests.Perf_Int32.ToString(value: 12345)"))


class TableTests(unittest.TestCase):
    def test_leaves_cases_mono_removed_out_of_the_mono_means_only(self) -> None:
        rows = [("Benchstone.A.Test()", 10.0, 20.0, 5.0), ("Benchstone.B.Test()", 10.0, 0.01, 10.0)]

        text = TABLE.render(rows, {"Benchstone.B.Test()"})

        self.assertIn("| Runtime | 2 | 0.50 | 1.41 |", text)
        self.assertIn("| All | 2 | 0.50 | 1.41 |", text)
        self.assertIn("| `Benchstone.B.Test()` † | 10.0 | 0.0 | 10.0 | 1000.00 | 1.00 |", text)

    def test_reads_only_cases_every_runtime_timed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory) / "results.tsv"
            results.write_text("case\tdesktop_ns\tmono_ns\tnetwasm_ns\n"
                               "A()\t1\t2\t3\nB()\t1\t\t3\nC()\t1\tERROR x\t3\n")

            self.assertEqual([("A()", 3.0, 2.0, 1.0)], TABLE.read_rows(results))

    def test_reads_the_removed_cases_without_comments(self) -> None:
        removed = TABLE.read_cases(HARNESS / "mono-removed.txt")

        self.assertEqual(5, len(removed))
        self.assertIn("Benchstone.BenchF.NewtE.Test()", removed)


class ExclusionTests(unittest.TestCase):
    def test_removes_a_file_or_every_file_under_a_directory(self) -> None:
        props = RUN.exclusion_props("# reason\nruntime/Roslyn/\nruntime/A.cs # why\n\n")

        self.assertIn('<Compile Remove="$(DotnetPerformanceSource)/benchmarks/micro/runtime/Roslyn/**" />', props)
        self.assertIn('<Compile Remove="$(DotnetPerformanceSource)/benchmarks/micro/runtime/A.cs" />', props)
        self.assertEqual(2, props.count("<Compile Remove="))

    def test_every_exclusion_names_a_dotnet_performance_source(self) -> None:
        entries = [line.split("#")[0].strip() for line in (HARNESS / "exclusions.txt").read_text().splitlines()]

        for entry in filter(None, entries):
            self.assertRegex(entry, r"^(runtime|libraries)/[\w./-]+$")


if __name__ == "__main__":
    unittest.main()

import importlib.util
from pathlib import Path
import unittest


SCRIPT_PATH = (
    Path(__file__).resolve().parents[1] / "create-compiler-test-shard-filter.py"
)
SPEC = importlib.util.spec_from_file_location("compiler_test_shards", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
SHARDS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SHARDS)


class CompilerTestShardFilterTests(unittest.TestCase):
    def test_parses_fact_and_theory_rows_by_class(self):
        counts = SHARDS.parse_test_class_counts(
            [
                "Test run for something.dll",
                "The following Tests are available:",
                "    NetWasm.Compiler.Tests.AlphaTests.Fact",
                "    NetWasm.Compiler.Tests.AlphaTests.Theory(value: 1)",
                "    NetWasm.Compiler.Tests.Correctness.BetaTests.Run(cell: \\\"x\\\")",
                "unrelated output",
            ]
        )

        self.assertEqual(2, counts["NetWasm.Compiler.Tests.AlphaTests"])
        self.assertEqual(
            1,
            counts["NetWasm.Compiler.Tests.Correctness.BetaTests"],
        )

    def test_greedy_partition_is_deterministic_balanced_and_complete(self):
        counts = SHARDS.Counter(
            {
                "NetWasm.Compiler.Tests.AlphaTests": 8,
                "NetWasm.Compiler.Tests.BetaTests": 7,
                "NetWasm.Compiler.Tests.GammaTests": 4,
                "NetWasm.Compiler.Tests.DeltaTests": 3,
                "NetWasm.Compiler.Tests.EpsilonTests": 2,
            }
        )

        first = SHARDS.create_shards(counts, 3)
        second = SHARDS.create_shards(counts, 3)

        self.assertEqual(first, second)
        flattened = [name for shard in first for name in shard]
        self.assertCountEqual(counts, flattened)
        self.assertEqual(len(flattened), len(set(flattened)))
        loads = [sum(counts[name] for name in shard) for shard in first]
        self.assertLessEqual(max(loads) - min(loads), 2)

    def test_filter_selects_every_class_in_the_shard(self):
        value = SHARDS.create_vstest_filter(
            [
                "NetWasm.Compiler.Tests.BetaTests",
                "NetWasm.Compiler.Tests.AlphaTests",
            ]
        )

        self.assertEqual(
            "FullyQualifiedName~NetWasm.Compiler.Tests.AlphaTests."
            "|FullyQualifiedName~NetWasm.Compiler.Tests.BetaTests.",
            value,
        )

    def test_rejects_empty_discovery_and_empty_shards(self):
        with self.assertRaisesRegex(ValueError, "no NetWasm compiler tests"):
            SHARDS.parse_test_class_counts(["The following Tests are available:"])

        with self.assertRaisesRegex(ValueError, "contains no test classes"):
            SHARDS.create_vstest_filter([])


if __name__ == "__main__":
    unittest.main()

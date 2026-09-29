import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT_PATH = (
    Path(__file__).resolve().parents[1] / "create-compiler-test-shard-filter.py"
)
SPEC = importlib.util.spec_from_file_location("compiler_test_shards", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
SHARDS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = SHARDS
SPEC.loader.exec_module(SHARDS)


class CompilerTestShardFilterTests(unittest.TestCase):
    def test_checked_in_timing_history_is_valid(self):
        path = SCRIPT_PATH.parent / "compiler-test-durations.json"
        history = json.loads(path.read_text(encoding="utf-8"))
        counts = SHARDS.Counter({name: 1 for name in history["methods"]})

        weights, fallback = SHARDS.load_timing_weights(path, counts)

        self.assertEqual(0, fallback)
        self.assertEqual(set(counts), set(weights))

    def test_parses_fact_and_theory_rows_by_method(self):
        listing = [
            "Test run for something.dll",
            "The following Tests are available:",
            "    NetWasm.Compiler.Tests.AlphaTests.Fact",
            "    NetWasm.Compiler.Tests.AlphaTests.Theory(value: 1)",
            "    NetWasm.Compiler.Tests.Correctness.BetaTests.Run(cell: \\\"x\\\")",
            "unrelated output",
        ]
        cases = SHARDS.parse_test_cases(listing)
        counts = SHARDS.parse_test_method_counts(listing)

        self.assertEqual(1, counts["NetWasm.Compiler.Tests.AlphaTests.Fact"])
        self.assertEqual(1, counts["NetWasm.Compiler.Tests.AlphaTests.Theory"])
        self.assertEqual(
            1,
            counts["NetWasm.Compiler.Tests.Correctness.BetaTests.Run"],
        )
        self.assertEqual(
            ["NetWasm.Compiler.Tests.AlphaTests.Theory(value: 1)"],
            cases["NetWasm.Compiler.Tests.AlphaTests.Theory"],
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
            "FullyQualifiedName=NetWasm.Compiler.Tests.AlphaTests"
            "|FullyQualifiedName=NetWasm.Compiler.Tests.BetaTests",
            value,
        )

    def test_timing_history_drives_balancing_with_case_count_fallback(self):
        counts = SHARDS.Counter({
            "NetWasm.Compiler.Tests.SlowTests": 2,
            "NetWasm.Compiler.Tests.FastTests": 10,
            "NetWasm.Compiler.Tests.NewTests": 4,
        })
        history = {
            "schemaVersion": 2,
            "methods": {
                "NetWasm.Compiler.Tests.SlowTests": {
                    "cases": 2,
                    "durationSeconds": 20.0,
                },
                "NetWasm.Compiler.Tests.FastTests": {
                    "cases": 10,
                    "durationSeconds": 5.0,
                },
                "NetWasm.Compiler.Tests.RemovedTests": {
                    "cases": 1,
                    "durationSeconds": 100.0,
                },
            },
        }
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary, "timings.json")
            path.write_text(json.dumps(history), encoding="utf-8")
            weights, fallback = SHARDS.load_timing_weights(path, counts)

        self.assertEqual(1, fallback)
        self.assertEqual(20.0, weights["NetWasm.Compiler.Tests.SlowTests"])
        self.assertEqual(5.0, weights["NetWasm.Compiler.Tests.FastTests"])
        self.assertAlmostEqual(25.0 / 12.0 * 4, weights["NetWasm.Compiler.Tests.NewTests"])
        shards = SHARDS.create_shards(counts, 2, weights)
        self.assertEqual(
            ["NetWasm.Compiler.Tests.SlowTests"],
            shards[0],
        )
        self.assertCountEqual(
            ["NetWasm.Compiler.Tests.FastTests", "NetWasm.Compiler.Tests.NewTests"],
            shards[1],
        )

    def test_oversized_theory_is_split_into_exact_case_filters(self):
        method = "NetWasm.Compiler.Tests.DecimalTests.Arithmetic"
        first = f'{method}(caseId: "decimal", cell: "Debug-Wasm32-Direct")'
        second = f'{method}(caseId: "decimal", cell: "Release-Wasm64-Linked")'
        items = SHARDS.create_execution_items(
            {method: [first, second]},
            {method: 900.0},
            {first: 300.0, second: 600.0},
        )

        self.assertEqual(2, len(items))
        self.assertEqual([300.0, 600.0], [item.weight for item in items])
        shards = SHARDS.create_execution_shards(items, 2)
        self.assertEqual(
            [600.0, 300.0],
            [sum(item.weight for item in shard) for shard in shards],
        )
        filter_value = SHARDS.create_vstest_filter([items[0]])
        self.assertIn(f"FullyQualifiedName={method}", filter_value)
        self.assertIn("DisplayName~caseId%3A%20%22decimal%22", filter_value)

    def test_normal_and_unsafe_theories_remain_atomic(self):
        normal = "NetWasm.Compiler.Tests.NormalTests.Theory"
        unsafe = "NetWasm.Compiler.Tests.UnsafeTests.Theory"
        items = SHARDS.create_execution_items(
            {
                normal: [f"{normal}(value: 1)", f"{normal}(value: 2)"],
                unsafe: [f"{unsafe}(value: (1))", f"{unsafe}(value: (2))"],
            },
            {normal: 299.0, unsafe: 900.0},
        )

        self.assertEqual(2, len(items))
        self.assertTrue(all(item.display_name is None for item in items))
        self.assertEqual([2, 2], [item.cases for item in items])

    def test_schema_three_case_timings_are_loaded(self):
        history = {
            "schemaVersion": 3,
            "methods": {},
            "cases": {
                "NetWasm.Compiler.Tests.SlowTests.Theory(value: 1)": 12.5,
            },
        }
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary, "timings.json")
            path.write_text(json.dumps(history), encoding="utf-8")
            weights = SHARDS.load_case_timing_weights(path)

        self.assertEqual(
            {"NetWasm.Compiler.Tests.SlowTests.Theory(value: 1)": 12.5},
            weights,
        )

    def test_timing_history_rejects_invalid_or_unrelated_data(self):
        counts = SHARDS.Counter({"NetWasm.Compiler.Tests.CurrentTests": 1})
        for history, message in (
            ({"schemaVersion": 1, "methods": {}}, "unsupported schema"),
            ({"schemaVersion": 2, "methods": {
                "NetWasm.Compiler.Tests.OldTests": {
                    "cases": 1,
                    "durationSeconds": 1.0,
                },
            }}, "matches no discovered"),
            ({"schemaVersion": 2, "methods": {
                "NetWasm.Compiler.Tests.CurrentTests": {
                    "cases": 0,
                    "durationSeconds": 1.0,
                },
            }}, "Invalid timing history"),
            ({"schemaVersion": 2, "methods": {
                "NetWasm.Compiler.Tests.CurrentTests": {
                    "cases": 1,
                    "durationSeconds": float("nan"),
                },
            }}, "Invalid timing history"),
        ):
            with self.subTest(message=message), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary, "timings.json")
                path.write_text(json.dumps(history), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    SHARDS.load_timing_weights(path, counts)

    def test_rejects_empty_discovery_and_empty_shards(self):
        with self.assertRaisesRegex(ValueError, "no NetWasm compiler tests"):
            SHARDS.parse_test_method_counts(["The following Tests are available:"])

        with self.assertRaisesRegex(ValueError, "contains no test methods"):
            SHARDS.create_vstest_filter([])
        with self.assertRaisesRegex(ValueError, "cover every discovered"):
            SHARDS.create_shards(
                SHARDS.Counter({"NetWasm.Compiler.Tests.AlphaTests": 1}),
                1,
                {},
            )


if __name__ == "__main__":
    unittest.main()

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

        self.assertEqual(["NetWasm.Compiler.Tests.AlphaTests.Fact"], cases["NetWasm.Compiler.Tests.AlphaTests.Fact"])
        self.assertEqual(
            ['NetWasm.Compiler.Tests.Correctness.BetaTests.Run(cell: \\\"x\\\")'],
            cases["NetWasm.Compiler.Tests.Correctness.BetaTests.Run"],
        )
        self.assertEqual(
            ["NetWasm.Compiler.Tests.AlphaTests.Theory(value: 1)"],
            cases["NetWasm.Compiler.Tests.AlphaTests.Theory"],
        )

    def test_greedy_partition_is_deterministic_balanced_and_complete(self):
        items = [
            SHARDS.ShardItem(f"NetWasm.Compiler.Tests.{name}Tests.Run", None, 1, weight)
            for name, weight in (
                ("Alpha", 8.0), ("Beta", 7.0), ("Gamma", 4.0), ("Delta", 3.0), ("Epsilon", 2.0),
            )
        ]

        first = SHARDS.create_execution_shards(items, 3)
        second = SHARDS.create_execution_shards(list(reversed(items)), 3)

        self.assertEqual(first, second)
        flattened = [item for shard in first for item in shard]
        self.assertCountEqual(items, flattened)
        loads = [sum(item.weight for item in shard) for shard in first]
        self.assertLessEqual(max(loads) - min(loads), 2)

    def test_a_class_s_cases_spread_because_they_run_in_turn(self):
        # xUnit runs one class's tests one after another, so two of the beta
        # cases in one shard would take 180 seconds where the shard's load,
        # spread over its lanes, takes 45.
        alpha = "NetWasm.Compiler.Tests.AlphaTests.Run"
        beta = "NetWasm.Compiler.Tests.BetaTests.Run"
        items = [
            SHARDS.ShardItem(alpha, f"{alpha}(value: {index})", 1, 100.0) for index in range(2)
        ] + [
            SHARDS.ShardItem(beta, f"{beta}(value: {index})", 1, 90.0) for index in range(3)
        ]

        shards = SHARDS.create_execution_shards(items, 3)

        for shard in shards:
            self.assertLessEqual(
                sum(1 for item in shard if item.method_name == beta), 1, shard)
            self.assertLessEqual(
                sum(1 for item in shard if item.method_name == alpha), 1, shard)

    def test_filter_selects_every_class_in_the_shard(self):
        value = SHARDS.create_vstest_filter(
            [
                "NetWasm.Compiler.Tests.BetaTests",
                "NetWasm.Compiler.Tests.AlphaTests",
            ]
        )
        self.assertEqual(
            "(FullyQualifiedName=NetWasm.Compiler.Tests.AlphaTests"
            "|FullyQualifiedName=NetWasm.Compiler.Tests.BetaTests)"
            "&Category!=Performance",
            value,
        )

    def test_timing_history_drives_balancing_with_case_count_fallback(self):
        counts = SHARDS.Counter({
            "NetWasm.Compiler.Tests.SlowTests": 2,
            "NetWasm.Compiler.Tests.FastTests": 10,
            "NetWasm.Compiler.Tests.NewTests": 4,
        })
        history = {
            "schemaVersion": 3,
            "cases": {},
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
        items = [SHARDS.ShardItem(name, None, counts[name], weight) for name, weight in weights.items()]
        shards = SHARDS.create_execution_shards(items, 2)
        self.assertEqual(
            ["NetWasm.Compiler.Tests.SlowTests"],
            [item.method_name for item in shards[0]],
        )
        self.assertCountEqual(
            ["NetWasm.Compiler.Tests.FastTests", "NetWasm.Compiler.Tests.NewTests"],
            [item.method_name for item in shards[1]],
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

    def test_a_shortened_argument_keeps_its_ellipsis_unescaped(self):
        # xUnit shortens a long string argument and ends it with '···'. VSTest
        # decodes %XX one character at a time, so an escaped UTF-8 sequence
        # would select nothing.
        method = "NetWasm.Compiler.Tests.GuardTests.Rejects"
        case = f'{method}(actor: "NetWasm.Compiler.Analysis.AddressTakenValues.Local"···, missing: 0)'
        item = SHARDS.ShardItem(method, case, 1, 1.0)

        filter_value = SHARDS.create_vstest_filter([item])

        self.assertIn(
            'DisplayName~actor%3A%20%22NetWasm.Compiler.Analysis.AddressTakenValues.Local%22···%2C%20missing%3A%200)',
            filter_value,
        )
        self.assertNotIn("%C2%B7", filter_value)

    def test_plan_records_whole_methods_and_split_cases(self):
        method = "NetWasm.Compiler.Tests.AlphaTests.Theory"
        items = [
            SHARDS.ShardItem("NetWasm.Compiler.Tests.AlphaTests.Fact", None, 1, 1.0),
            SHARDS.ShardItem(method, f"{method}(value: 1)", 1, 1.0),
        ]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "plan.json"
            SHARDS.write_plan(path, items)
            plan = json.loads(path.read_text(encoding="utf-8"))

        self.assertEqual(
            [
                {"method": "NetWasm.Compiler.Tests.AlphaTests.Fact", "displayName": None},
                {"method": method, "displayName": f"{method}(value: 1)"},
            ],
            plan["items"],
        )

    def test_run_settings_carry_the_filter_without_a_length_limit(self):
        items = [
            SHARDS.ShardItem(f"NetWasm.Compiler.Tests.AlphaTests.Case{index}", None, 1, 1.0)
            for index in range(4000)
        ]
        method = "NetWasm.Compiler.Tests.BetaTests.Theory"
        items.append(SHARDS.ShardItem(method, f'{method}(caseId: "a<b>", cell: "Debug-Wasm32")', 1, 1.0))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "shard.runsettings"
            SHARDS.write_run_settings(path, items)
            root = SHARDS.ElementTree.parse(path).getroot()

        written = root.findtext("RunConfiguration/TestCaseFilter")
        self.assertEqual("RunSettings", root.tag)
        self.assertGreater(len(written.encode()), 131072)
        # VSTest reads run settings as written: the case's arguments are not percent-encoded,
        # as the command line's filter is for MSBuild.
        self.assertIn(f'(FullyQualifiedName={method}&DisplayName~caseId: "a<b>", cell: "Debug-Wasm32")', written)
        self.assertEqual(
            SHARDS.create_vstest_filter(items, lambda arguments: arguments), written)
        self.assertNotEqual(SHARDS.create_vstest_filter(items), written)

    def test_normal_and_unsafe_theories_remain_atomic(self):
        normal = "NetWasm.Compiler.Tests.NormalTests.Theory"
        unsafe = "NetWasm.Compiler.Tests.UnsafeTests.Theory"
        # xUnit writes a NUL as \0, which VSTest's filter rejects as an
        # unknown escape sequence.
        escaped = "NetWasm.Compiler.Tests.EscapedTests.Theory"
        items = SHARDS.create_execution_items(
            {
                normal: [f"{normal}(value: 1)", f"{normal}(value: 2)"],
                unsafe: [f"{unsafe}(value: (1))", f"{unsafe}(value: (2))"],
                escaped: [f'{escaped}(line: "a\\0b")', f'{escaped}(line: "a")'],
            },
            {normal: 299.0, unsafe: 900.0, escaped: 900.0},
        )

        self.assertEqual(3, len(items))
        self.assertTrue(all(item.display_name is None for item in items))
        self.assertEqual([2, 2, 2], [item.cases for item in items])

    def test_a_theory_without_case_history_is_split(self):
        # Its weight is the average case's, which says nothing about how long
        # its cases take, so they are placed one by one.
        method = "NetWasm.Compiler.Tests.NewTests.Theory"
        cases = [f"{method}(chunk: {index})" for index in range(3)]

        items = SHARDS.create_execution_items({method: cases}, {method: 20.0}, {})

        self.assertEqual(cases, [item.display_name for item in items])
        self.assertEqual([20.0 / 3] * 3, [item.weight for item in items])
        # Without any history every weight is a case count, and the theory
        # stays whole.
        whole = SHARDS.create_execution_items({method: cases}, {method: 3.0})
        self.assertEqual([None], [item.display_name for item in whole])

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
            ({"schemaVersion": 2, "methods": {}, "cases": {}}, "unsupported schema"),
            ({"schemaVersion": 3, "methods": {
                "NetWasm.Compiler.Tests.OldTests": {
                    "cases": 1,
                    "durationSeconds": 1.0,
                },
            }}, "matches no discovered"),
            ({"schemaVersion": 3, "methods": {
                "NetWasm.Compiler.Tests.CurrentTests": {
                    "cases": 0,
                    "durationSeconds": 1.0,
                },
            }}, "Invalid timing history"),
            ({"schemaVersion": 3, "methods": {
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
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary, "timings.json")
            path.write_text(json.dumps({"schemaVersion": 2, "methods": {}}), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "unsupported schema"):
                SHARDS.load_case_timing_weights(path)

    def test_parses_tests_under_another_namespace(self):
        listing = [
            "    NetWasm.Runtime.Pack.Tests.MsBuild.PackTests.Packs",
            "    NetWasm.Compiler.Tests.AlphaTests.Fact",
        ]
        cases = SHARDS.parse_test_cases(listing, "NetWasm.Runtime.Pack.Tests.")

        self.assertEqual(["NetWasm.Runtime.Pack.Tests.MsBuild.PackTests.Packs"], list(cases))
        with self.assertRaisesRegex(ValueError, "no NetWasm.Sdk.Tests tests"):
            SHARDS.parse_test_cases(listing, "NetWasm.Sdk.Tests.")

    def test_class_list_keeps_listed_classes_and_rejects_stale_entries(self):
        listing = [
            "    NetWasm.Compiler.Tests.AlphaTests.Fact",
            "    NetWasm.Compiler.Tests.AlphaTests+Nested.Fact",
            "    NetWasm.Compiler.Tests.AlphaTestsExtra.Fact",
            "    NetWasm.Compiler.Tests.Correctness.BetaTests.Run(cell: 1)",
        ]
        cases = SHARDS.parse_test_cases(listing)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "classes.txt"
            path.write_text(
                "# Pull request tier\nNetWasm.Compiler.Tests.AlphaTests\n\n"
                "NetWasm.Compiler.Tests.Correctness.BetaTests\n",
                encoding="utf-8")
            selected = SHARDS.select_classes(cases, SHARDS.load_class_list(path))

            # A class does not select another whose name it prefixes.
            self.assertEqual(
                [
                    "NetWasm.Compiler.Tests.AlphaTests+Nested.Fact",
                    "NetWasm.Compiler.Tests.AlphaTests.Fact",
                    "NetWasm.Compiler.Tests.Correctness.BetaTests.Run",
                ],
                sorted(selected))

            path.write_text("NetWasm.Compiler.Tests.GoneTests\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "no discovered tests: NetWasm.Compiler.Tests.GoneTests"):
                SHARDS.select_classes(cases, SHARDS.load_class_list(path))
            path.write_text("# nothing\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "names no classes"):
                SHARDS.load_class_list(path)
            path.write_text("A\nA\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "more than once"):
                SHARDS.load_class_list(path)
            with self.assertRaisesRegex(ValueError, "Cannot read"):
                SHARDS.load_class_list(Path(directory) / "missing.txt")

    def test_rejects_empty_discovery_and_empty_shards(self):
        with self.assertRaisesRegex(ValueError, "no NetWasm.Compiler.Tests tests"):
            SHARDS.parse_test_cases(["The following Tests are available:"])

        with self.assertRaisesRegex(ValueError, "contains no test methods"):
            SHARDS.create_vstest_filter([])
        with self.assertRaisesRegex(ValueError, "cover every discovered"):
            SHARDS.create_execution_items(
                {"NetWasm.Compiler.Tests.AlphaTests.Run": ["NetWasm.Compiler.Tests.AlphaTests.Run"]},
                {},
            )
        item = SHARDS.ShardItem("NetWasm.Compiler.Tests.AlphaTests.Run", None, 1, 1.0)
        with self.assertRaisesRegex(ValueError, "no tests"):
            SHARDS.create_execution_shards([], 1)
        with self.assertRaisesRegex(ValueError, "Parallel lanes"):
            SHARDS.create_execution_shards([item], 1, 0)


if __name__ == "__main__":
    unittest.main()

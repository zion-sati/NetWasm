# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "run-compiler-tests-sharded.py"
SPEC = importlib.util.spec_from_file_location("run_compiler_tests_sharded", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class ShardedCompilerTestRunnerTests(unittest.TestCase):
    def test_shard_count_defaults_to_one_per_logical_cpu_and_allows_override(self):
        cpus = os.cpu_count() or 1
        expected = min(cpus, 100)

        # As many as the CPUs, but no more than the memory holds at MEMORY_PER_SHARD each.
        self.assertEqual(expected, RUNNER.resolve_shard_count(100, memory=1024 * RUNNER.MEMORY_PER_SHARD))
        self.assertEqual(min(cpus, 2), RUNNER.resolve_shard_count(100, memory=2 * RUNNER.MEMORY_PER_SHARD))
        self.assertEqual(1, RUNNER.resolve_shard_count(100, memory=RUNNER.MEMORY_PER_SHARD // 2))
        self.assertEqual(3, RUNNER.resolve_shard_count(100, "3"))
        self.assertEqual(2, RUNNER.resolve_shard_count(2, "20"))
        with self.assertRaisesRegex(ValueError, "positive integer"):
            RUNNER.resolve_shard_count(100, "0")
        with self.assertRaisesRegex(ValueError, "positive integer"):
            RUNNER.resolve_shard_count(100, "many")

    def test_runs_every_class_once_with_isolated_process_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.create_source(root)
            dotnet = self.create_fake_dotnet(root)
            results = root / "results"

            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--source",
                    str(source),
                    "--results-directory",
                    str(results),
                    "--dotnet",
                    str(dotnet),
                    "--shard-count",
                    "2",
                ],
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            summary = json.loads((results / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(2, summary["shardCount"])
            self.assertEqual(5, summary["discoveredMethods"])
            self.assertEqual(5, summary["discoveredCases"])
            invocations = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in sorted(results.glob("shard-*/test-results/invocation.json"))
            ]
            self.assertEqual(2, len(invocations))
            for invocation in invocations:
                self.assertEqual(["xUnit.MaxParallelThreads=1"], invocation["runSettings"])
            selected = "|".join(item["filter"] for item in invocations)
            for name in (
                "AlphaTests.First",
                "AlphaTests.Second",
                "BetaTests.Run",
                "GammaTests.Run",
                "DeltaTests.Run",
            ):
                self.assertEqual(1, selected.count(name))
            self.assertEqual(2, len({item["tmpdir"] for item in invocations}))
            self.assertEqual(2, len({item["receipts"] for item in invocations}))
            self.assertEqual(
                {"Release"},
                {item["buildConfiguration"] for item in invocations},
            )
            self.assertEqual(
                {str(source.resolve())},
                {item["repositoryRoot"] for item in invocations},
            )
            self.assertEqual(
                {str((results / "native-dependencies").resolve())},
                {item["dependencyRoot"] for item in invocations},
            )

    # A filter selects the tests the shards run, as dotnet test's --filter would; each shard is one test process by
    # design, so it tells the project's one-process guard (NWTEST001) so.
    def test_runs_only_the_filters_tests_each_shard_as_one_process(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.create_source(root)
            dotnet = self.create_fake_dotnet(root)
            results = root / "results"

            completed = subprocess.run(
                [
                    sys.executable, str(SCRIPT),
                    "--source", str(source),
                    "--results-directory", str(results),
                    "--dotnet", str(dotnet),
                    "--shard-count", "2",
                    "--filter", "FullyQualifiedName~.AlphaTests.",
                ],
                capture_output=True, text=True, check=False,
            )

            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            # Without --no-build it builds the test project first.
            self.assertTrue((source / "built").is_file())
            listing = json.loads((source / "listing.json").read_text(encoding="utf-8"))
            self.assertEqual("FullyQualifiedName~.AlphaTests.", listing["filter"])
            self.assertTrue(listing["oneProcess"])
            summary = json.loads((results / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(2, summary["discoveredMethods"])
            invocations = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in sorted(results.glob("shard-*/test-results/invocation.json"))
            ]
            self.assertTrue(invocations)
            self.assertTrue(all(item["oneProcess"] for item in invocations))
            selected = "|".join(item["filter"] for item in invocations)
            for name in ("BetaTests", "GammaTests", "DeltaTests"):
                self.assertNotIn(name, selected)

    def test_runs_only_the_listed_classes_and_rejects_a_stale_list(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.create_source(root)
            dotnet = self.create_fake_dotnet(root)
            classes = root / "pr-tier.txt"
            classes.write_text(
                "# The pull request tier.\n"
                "NetWasm.Compiler.Tests.AlphaTests\n"
                "NetWasm.Compiler.Tests.GammaTests\n",
                encoding="utf-8",
            )

            def run(results: Path) -> subprocess.CompletedProcess[str]:
                return subprocess.run(
                    [
                        sys.executable, str(SCRIPT),
                        "--source", str(source),
                        "--results-directory", str(results),
                        "--dotnet", str(dotnet),
                        "--shard-count", "2",
                        "--classes", str(classes),
                    ],
                    capture_output=True, text=True, check=False,
                )

            completed = run(root / "results")
            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            summary = json.loads((root / "results" / "summary.json").read_text(encoding="utf-8"))
            # AlphaTests.First, AlphaTests.Second and GammaTests.Run, one case each.
            self.assertEqual(3, summary["discoveredMethods"])
            self.assertEqual(3, summary["discoveredCases"])
            selected = "|".join(
                json.loads(path.read_text(encoding="utf-8"))["filter"]
                for path in sorted((root / "results").glob("shard-*/test-results/invocation.json"))
            )
            for name in ("AlphaTests.First", "AlphaTests.Second", "GammaTests.Run"):
                self.assertEqual(1, selected.count(name))
            for name in ("BetaTests", "DeltaTests"):
                self.assertNotIn(name, selected)

            classes.write_text("NetWasm.Compiler.Tests.RenamedTests\n", encoding="utf-8")
            stale = run(root / "stale")
            self.assertNotEqual(0, stale.returncode)
            self.assertIn("names classes with no discovered tests", stale.stderr)

    def test_waits_for_all_shards_and_reports_a_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.create_source(root)
            dotnet = self.create_fake_dotnet(root)
            results = root / "results"
            environment = os.environ.copy()
            environment["FAIL_CLASS"] = "BetaTests"

            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--source",
                    str(source),
                    "--results-directory",
                    str(results),
                    "--dotnet",
                    str(dotnet),
                    "--shard-count",
                    "2",
                ],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(1, completed.returncode)
            summary = json.loads((results / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(2, len(summary["shards"]))
            self.assertEqual(1, sum(item["exitCode"] != 0 for item in summary["shards"]))
            self.assertIn("simulated compiler-test failure", completed.stderr)

    def test_collects_isolated_coverage_and_merges_it_with_existing_coverage(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.create_source(root)
            dotnet = self.create_fake_dotnet(root)
            results = root / "results"
            coverage = root / "coverage.json"
            coverage.write_text(
                json.dumps(self.coverage_report(5, 3)),
                encoding="utf-8",
            )

            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "--source",
                    str(source),
                    "--results-directory",
                    str(results),
                    "--coverage-output",
                    str(coverage),
                    "--dotnet",
                    str(dotnet),
                    "--shard-count",
                    "2",
                ],
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
            merged = json.loads(coverage.read_text(encoding="utf-8"))
            method = merged["Compiler.dll"]["Compiler.cs"]["Compiler"]["Run"]
            self.assertEqual(7, method["Lines"]["10"])
            self.assertEqual(5, method["Branches"][0]["Hits"])
            invocations = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in sorted(results.glob("shard-*/test-results/invocation.json"))
            ]
            self.assertEqual(2, len(invocations))
            self.assertTrue(all(item["coverage"] for item in invocations))
            self.assertEqual(2, len({item["outputPath"] for item in invocations}))
            self.assertTrue(
                all(not Path(item["outputPath"]).exists() for item in invocations)
            )

    @staticmethod
    def create_source(root: Path) -> Path:
        source = root / "source"
        eng = source / "eng"
        eng.mkdir(parents=True)
        for name in ("create-compiler-test-shard-filter.py", "merge-coverlet-reports.py"):
            shutil.copy2(SCRIPT.parent / name, eng / name)
        methods = {
            f"NetWasm.Compiler.Tests.{class_name}Tests.{method_name}": {
                "cases": count,
                "durationSeconds": float(duration),
            }
            for class_name, method_name, count, duration in (
                ("Alpha", "First", 1, 6),
                ("Alpha", "Second", 1, 4),
                ("Beta", "Run", 1, 6),
                ("Gamma", "Run", 1, 2),
                ("Delta", "Run", 1, 1),
            )
        }
        (eng / "compiler-test-durations.json").write_text(
            json.dumps({"schemaVersion": 3, "methods": methods, "cases": {}}),
            encoding="utf-8",
        )
        output = (
            source
            / "tests"
            / "NetWasm.Compiler.Tests"
            / "bin"
            / "Release"
            / "net10.0"
        )
        output.mkdir(parents=True)
        (output / "NetWasm.Compiler.Tests.dll").write_bytes(b"test assembly")
        (output / "NetWasm.Compiler.Tests.runtimeconfig.json").write_text(
            "{}",
            encoding="utf-8",
        )
        return source

    @staticmethod
    def coverage_report(line_hits: int, branch_hits: int) -> dict[str, object]:
        return {
            "Compiler.dll": {
                "Compiler.cs": {
                    "Compiler": {
                        "Run": {
                            "Lines": {"10": line_hits},
                            "Branches": [
                                {
                                    "Line": 10,
                                    "Offset": 1,
                                    "EndOffset": 2,
                                    "Path": 0,
                                    "Ordinal": 0,
                                    "Hits": branch_hits,
                                }
                            ],
                        }
                    }
                }
            }
        }

    @staticmethod
    def create_fake_dotnet(root: Path) -> Path:
        script = root / "fake-dotnet.py"
        script.write_text(
            "#!" + sys.executable + "\n" + textwrap.dedent(
                """
                import json
                import os
                from pathlib import Path
                import sys
                import time

                arguments = sys.argv[1:]
                if arguments[0] == "build":
                    Path("built").write_text("Release", encoding="utf-8")
                    raise SystemExit(0)
                if "--list-tests" in arguments:
                    listed = [
                        "NetWasm.Compiler.Tests.AlphaTests.First",
                        "NetWasm.Compiler.Tests.AlphaTests.Second(value: 1)",
                        "NetWasm.Compiler.Tests.BetaTests.Run",
                        "NetWasm.Compiler.Tests.GammaTests.Run",
                        "NetWasm.Compiler.Tests.DeltaTests.Run",
                    ]
                    # A filter of the form FullyQualifiedName~.Name. keeps the tests that contain it.
                    selected = (arguments[arguments.index("--filter") + 1]
                                if "--filter" in arguments else None)
                    Path("listing.json").write_text(json.dumps({
                        "filter": selected,
                        "oneProcess": "-p:NetWasmTestsInOneProcess=true" in arguments,
                    }), encoding="utf-8")
                    print("The following Tests are available:")
                    for name in listed:
                        if selected is None or selected.split("~", 1)[1] in name:
                            print("    " + name)
                    raise SystemExit(0)

                import xml.etree.ElementTree as ElementTree
                settings = ElementTree.parse(arguments[arguments.index("--settings") + 1])
                selected = settings.getroot().findtext("RunConfiguration/TestCaseFilter")
                results = Path(arguments[arguments.index("--results-directory") + 1])
                results.mkdir(parents=True, exist_ok=True)
                output_argument = next(
                    (value for value in arguments if value.startswith("-p:OutputPath=")),
                    None,
                )
                output_path = output_argument.split("=", 1)[1] if output_argument else None
                (results / "invocation.json").write_text(json.dumps({
                    "filter": selected,
                    "tmpdir": os.environ["TMPDIR"],
                    "receipts": os.environ["NETWASM_CORPUS_RECEIPTS"],
                    "buildConfiguration": os.environ[
                        "NETWASM_TEST_BUILD_CONFIGURATION"
                    ],
                    "repositoryRoot": os.environ["NETWASM_TEST_REPOSITORY_ROOT"],
                    "dependencyRoot": os.environ["NETWASM_DEPENDENCY_ROOT"],
                    "coverage": "--collect" in arguments,
                    "runSettings": arguments[arguments.index("--") + 1:],
                    "oneProcess": "-p:NetWasmTestsInOneProcess=true" in arguments,
                    "outputPath": output_path,
                }), encoding="utf-8")
                if "--collect" in arguments:
                    if output_path is None or not Path(output_path).is_dir():
                        print("isolated output is missing")
                        raise SystemExit(8)
                    report = {
                        "Compiler.dll": {
                            "Compiler.cs": {
                                "Compiler": {
                                    "Run": {
                                        "Lines": {"10": 1},
                                        "Branches": [{
                                            "Line": 10,
                                            "Offset": 1,
                                            "EndOffset": 2,
                                            "Path": 0,
                                            "Ordinal": 0,
                                            "Hits": 1,
                                        }],
                                    }
                                }
                            }
                        }
                    }
                    session = results / "fake-session"
                    session.mkdir()
                    (session / "coverage.json").write_text(
                        json.dumps(report),
                        encoding="utf-8",
                    )
                time.sleep(0.05)
                failed_class = os.environ.get("FAIL_CLASS")
                if failed_class and failed_class in selected:
                    print("simulated compiler-test failure")
                    raise SystemExit(7)
                print("Passed!")
                """
            ),
            encoding="utf-8",
        )
        script.chmod(0o755)
        return script


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT_PATH = Path(__file__).parents[1] / "qualify-optimization-modes.py"
SPEC = importlib.util.spec_from_file_location("qualify_optimization_modes", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class OptimizationTraceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.trace = Path(self.temporary_directory.name) / "trace.jsonl"

    def write(self, *invocations: list[str]) -> None:
        self.trace.write_text(
            "".join(json.dumps(invocation) + "\n" for invocation in invocations),
            encoding="utf-8",
        )

    def test_rejects_invocations_that_could_hide_or_duplicate_a_build_phase(self) -> None:
        cases = {
            "unflagged None invocation": (
                None,
                [["input.wasm", "--post-emscripten", "-o", "output.wasm"]],
            ),
            "unknown optimization flag": (
                "-O3",
                [
                    ["input.wasm", "-O4", "--post-emscripten", "-o", "runtime.wasm"],
                    ["input.wasm", "-O3", "--converge", "--remove-unused-module-elements"],
                ],
            ),
            "duplicate runtime": (
                "-O2",
                [
                    ["input.wasm", "-O2", "--post-emscripten"],
                    ["input.wasm", "-O2", "--post-emscripten"],
                ],
            ),
            "duplicate final": (
                "-Oz",
                [
                    ["input.wasm", "-Oz", "--converge", "--remove-unused-module-elements"],
                    ["input.wasm", "-Oz", "--converge", "--remove-unused-module-elements"],
                ],
            ),
            "nonexact version probe": (None, [["--version", "extra"]]),
        }
        for name, (flag, invocations) in cases.items():
            with self.subTest(name=name):
                self.write(*invocations)
                with self.assertRaises(RuntimeError):
                    MODULE.parse_trace(self.trace, flag)

    def test_ignores_only_the_exact_version_probe(self) -> None:
        self.write(["--version"])

        invocations, phases = MODULE.parse_trace(self.trace, None)

        self.assertEqual([], invocations)
        self.assertEqual([], phases)


if __name__ == "__main__":
    unittest.main()

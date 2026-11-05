#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT_PATH = Path(__file__).parents[1] / "qualify-doomsharp.py"
SPEC = importlib.util.spec_from_file_location("qualify_doomsharp", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class CellTests(unittest.TestCase):
    def test_every_preset_runs_with_each_collector(self) -> None:
        self.assertEqual(
            [(preset, collector) for preset in ("O0", "O1", "O2", "O3", "Os", "Oz")
             for collector in ("NetWasm", "Compact")],
            MODULE.cells())

    def test_the_pin_names_a_commit_and_the_desktop_checksum(self) -> None:
        pin = json.loads((Path(__file__).parents[1] / "doomsharp.json").read_text())
        self.assertEqual("zion-sati/doomsharp", pin["repository"])
        self.assertRegex(pin["commit"], r"^[0-9a-f]{40}$")
        self.assertRegex(pin["checksum"], r"^-?\d+$")


class ConfigureTests(unittest.TestCase):
    def test_selects_the_candidate_and_stops_the_directory_build_search(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            bench = Path(directory)
            MODULE.configure(bench, Path("/packages"), "1.2.3-ci.4")
            self.assertEqual({"NetWasm.Sdk": "1.2.3-ci.4"},
                             json.loads((bench / "global.json").read_text())["msbuild-sdks"])
            config = (bench / "NuGet.Config").read_text()
            self.assertIn("<clear />", config)
            self.assertIn('value="/packages"', config)
            for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
                self.assertEqual("<Project />\n", (bench / name).read_text())


class ChecksumTests(unittest.TestCase):
    def test_reads_each_batch_in_order(self) -> None:
        output = ("batch 0 map E1M1 frames 120 ms 2210.4 checksum 1326079720\n"
                  "batch 1 map E1M1 frames 120 ms 1620.1 checksum -5\n")
        self.assertEqual(["1326079720", "-5"], MODULE.checksums(output))

    def test_rejects_batches_out_of_order(self) -> None:
        output = ("batch 1 map E1M1 frames 120 ms 1.0 checksum 1\n"
                  "batch 0 map E1M1 frames 120 ms 1.0 checksum 1\n")
        with self.assertRaisesRegex(ValueError, "out of order"):
            MODULE.checksums(output)

    def test_rejects_a_batch_of_other_frames(self) -> None:
        with self.assertRaisesRegex(ValueError, "other than 120 frames"):
            MODULE.checksums("batch 0 map E1M1 frames 60 ms 1.0 checksum 1\n")

    def test_finds_no_batch_in_other_output(self) -> None:
        self.assertEqual([], MODULE.checksums("Unhandled exception.\n"))


if __name__ == "__main__":
    unittest.main()

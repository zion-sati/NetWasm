# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

"""Maintainer native-profile validation, before toolchain or dependency work."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "build-netwasm-runtime.sh"


class NativeRuntimeProfileTests(unittest.TestCase):
    def test_rejects_unknown_profile_before_creating_output(self) -> None:
        self.assert_rejected("release", "fast", "native profile must be")

    def test_rejects_speed_in_debug_before_creating_output(self) -> None:
        self.assert_rejected("debug", "speed", "speed native profile requires release")

    def assert_rejected(self, configuration: str, profile: str, expected: str) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            layout = root / "layout.json"
            layout.write_text("{}", encoding="utf-8")
            output = root / "runtime.a"
            result = subprocess.run(
                ["bash", str(SCRIPT), "--runtime-layout", str(layout), "--output", str(output),
                 "--configuration", configuration, "--native-profile", profile],
                check=False, capture_output=True, text=True,
            )
            self.assertEqual(2, result.returncode)
            self.assertIn(expected, result.stderr)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()

# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).parents[1] / "qualify-sdk-consumers.sh"


@unittest.skipUnless(shutil.which("bash"), "SDK qualification requires Bash")
class SdkConsumerArtifactTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="netwasm-sdk-harness-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        self.packages = self.root / "packages"
        self.packages.mkdir()
        self.tools = self.root / "tools"
        self.tools.mkdir()
        dotnet = self.tools / "dotnet"
        dotnet.write_text("#!/usr/bin/env bash\necho synthetic-template-failure >&2\nexit 19\n")
        dotnet.chmod(0o755)
        self.environment = dict(os.environ)
        self.environment.pop("NETWASM_QUALIFY_DOTNET_ROOT", None)
        self.environment["NETWASM_QUALIFY_SDK_VERSION"] = "11.0.100"
        self.environment["PATH"] = str(self.tools) + os.pathsep + os.environ["PATH"]
        self.environment["RUNNER_TEMP"] = str(self.root)

    def invoke(self, *arguments):
        return subprocess.run(
            ["bash", str(SCRIPT), str(self.packages), "0.4.1-test", *arguments],
            env=self.environment, capture_output=True, text=True, check=False,
        )

    def test_rejects_a_missing_sdk_version_before_creating_output(self):
        # Without it no consumer pins an SDK and the package size canary cannot run.
        del self.environment["NETWASM_QUALIFY_SDK_VERSION"]
        output = self.root / "output"
        result = self.invoke("--artifact-directory", str(output))
        self.assertEqual(2, result.returncode)
        self.assertIn("NETWASM_QUALIFY_SDK_VERSION", result.stderr)
        self.assertFalse(output.exists())

    def test_rejects_missing_artifact_path(self):
        result = self.invoke("--artifact-directory")
        self.assertEqual(2, result.returncode)
        self.assertIn("requires a path", result.stderr)

    def test_rejects_empty_artifact_path(self):
        self.assertEqual(2, self.invoke("--artifact-directory", "").returncode)

    def test_rejects_relative_artifact_path(self):
        result = self.invoke("--artifact-directory", "relative")
        self.assertEqual(2, result.returncode)
        self.assertIn("must be absolute", result.stderr)

    def test_rejects_duplicate_artifact_option_before_creating_output(self):
        output = self.root / "output"
        result = self.invoke("--artifact-directory", str(output), "--artifact-directory", str(output))
        self.assertEqual(2, result.returncode)
        self.assertFalse(output.exists())

    def test_rejects_existing_directory_without_touching_contents(self):
        marker = self.packages / "marker"
        marker.write_text("preserve")
        self.assertEqual(2, self.invoke("--artifact-directory", str(self.packages)).returncode)
        self.assertEqual("preserve", marker.read_text())

    def test_rejects_dangling_symlink(self):
        output = self.root / "output"
        output.symlink_to(self.root / "missing", target_is_directory=True)
        self.assertEqual(2, self.invoke("--artifact-directory", str(output)).returncode)
        self.assertTrue(output.is_symlink())

    def test_failed_explicit_run_retains_original_log_and_package_root(self):
        output = self.root / "retained with spaces"
        result = self.invoke("--artifact-directory", str(output))
        self.assertEqual(1, result.returncode)
        self.assertIn(str(output), result.stderr)
        self.assertEqual("synthetic-template-failure\n", (output / "template-install.log").read_text())
        self.assertTrue((output / "consumer with spaces/packages").is_dir())
        self.assertTrue((output / "NuGet.Config").is_file())

    def test_failed_default_run_retains_evidence(self):
        result = self.invoke()
        self.assertEqual(1, result.returncode)
        outputs = list(self.root.glob("netwasm-runtime-host.*"))
        self.assertEqual(1, len(outputs))
        self.assertTrue((outputs[0] / "template-install.log").is_file())
        self.assertIn(str(outputs[0]), result.stderr)

    def test_success_cleanup_policy_preserves_explicit_output_only(self):
        source = SCRIPT.read_text()
        function = source.split("finish_qualification() {", 1)[1].split("trap finish_qualification EXIT", 1)[0]
        for explicit in (False, True):
            with self.subTest(explicit=explicit):
                output = self.root / ("explicit" if explicit else "temporary")
                output.mkdir()
                command = 'work_root="$1"; artifact_directory="$2"; finish_qualification() {' + function
                command += '\ntrap finish_qualification EXIT\nexit 0\n'
                result = subprocess.run(
                    ["bash", "-c", command, "cleanup-contract", str(output), str(output) if explicit else ""],
                    capture_output=True, text=True, check=False,
                )
                self.assertEqual(0, result.returncode)
                self.assertEqual(explicit, output.exists())

    def test_qualifies_literal_first_run_before_scripted_restore(self):
        script = SCRIPT.read_text(encoding="utf-8")
        first_run = script.index('assert_run_42 "$work_root/app-first-run.log"')
        restore = script.index('run_log "$work_root/app-restore-debug.log"')

        self.assertLess(first_run, restore)
        self.assertIn('--project "$app_project" --disable-build-servers',
                      script[first_run:restore])

    def test_new_project_fixtures_follow_the_template_target_framework(self):
        # A fixture that stands for a new project targets what the template creates; only the TUnit consumer pins
        # a framework (it uses the released TUnit-NetWasm pinned in tests/libraries, which ships for netwasm0.1).
        script = SCRIPT.read_text(encoding="utf-8")
        pinned = re.findall(r"<TargetFramework>([^<$%]+)</TargetFramework>", script)
        self.assertEqual(["netwasm0.1"], pinned)
        self.assertNotIn("/netwasm0.1/", script)


if __name__ == "__main__":
    unittest.main()

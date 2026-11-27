# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

from pathlib import Path
import subprocess
import tempfile
import unittest


REPOSITORY = Path(__file__).resolve().parents[2]
GUARD = REPOSITORY / "tests" / "NetWasm.Compiler.Tests" / "OneProcessGuard.targets"


# dotnet test refuses to run NetWasm.Compiler.Tests unfiltered in one test process (NWTEST001), and lets a filtered
# run, a listing and an explicit one-process run (the sharded runner's processes) go ahead. A project that imports the
# guard stands in for the test project, with a VSTest target that only reports that it ran.
class OneProcessGuardTests(unittest.TestCase):
    def run_vstest(self, *properties: str) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "guarded.proj"
            project.write_text(
                "<Project>\n"
                f'  <Import Project="{GUARD}" />\n'
                '  <Target Name="VSTest"><Message Importance="High" Text="VSTEST RAN" /></Target>\n'
                "</Project>\n",
                encoding="utf-8",
            )
            return subprocess.run(
                ["dotnet", "msbuild", str(project), "-nologo", "-t:VSTest", *properties],
                capture_output=True, text=True, check=False,
            )

    def test_an_unfiltered_run_is_refused(self):
        completed = self.run_vstest()
        self.assertNotEqual(0, completed.returncode)
        self.assertIn("NWTEST001", completed.stdout)
        self.assertIn("eng/run-compiler-tests-sharded.py", completed.stdout)
        self.assertNotIn("VSTEST RAN", completed.stdout)

    def test_a_filtered_run_a_listing_and_an_explicit_one_process_run_go_ahead(self):
        for properties in (
            ("-p:VSTestTestCaseFilter=FullyQualifiedName~.AlphaTests.",),
            ("-p:VSTestListTests=true",),
            ("-p:NetWasmTestsInOneProcess=true",),
        ):
            with self.subTest(properties=properties):
                completed = self.run_vstest(*properties)
                self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
                self.assertIn("VSTEST RAN", completed.stdout)


if __name__ == "__main__":
    unittest.main()

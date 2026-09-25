import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "prepare-wit-bindings-test-tool.sh"


class PrepareWitBindingsTestToolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.calls = self.root / "calls.jsonl"
        fake_bin = self.root / "fake-bin"
        fake_bin.mkdir()
        dotnet = fake_bin / "dotnet"
        dotnet.write_text("""#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ['WIT_TEST_CALLS'], 'a') as output:
    output.write(json.dumps(args) + '\\n')
if args[0] == 'msbuild':
    print(os.environ.get('WIT_TEST_VERSION', '1.2.3'))
elif args[0] == 'pack':
    sys.exit(int(os.environ.get('WIT_TEST_PACK_EXIT', '0')))
elif args[:2] == ['tool', 'install']:
    if os.environ.get('WIT_TEST_INSTALL_EXIT'):
        sys.exit(int(os.environ['WIT_TEST_INSTALL_EXIT']))
    directory = pathlib.Path(args[args.index('--tool-path') + 1])
    directory.mkdir(parents=True)
    tool = directory / 'netwasm-wit-bindgen'
    tool.write_text('#!/bin/sh\\nexit 0\\n')
    tool.chmod(0o755)
else:
    sys.exit(99)
""")
        dotnet.chmod(0o755)
        self.assets = self.root / "assets"
        self.assets.mkdir()
        for name in ["wasm-tools.wasm", "LICENSE-APACHE", "LICENSE-Apache-2.0_WITH_LLVM-exception",
                     "LICENSE-MIT", "README.md"]:
            (self.assets / name).write_text("synthetic asset")
        self.env = dict(os.environ, PATH=f"{fake_bin}{os.pathsep}{os.environ['PATH']}",
                        WIT_TEST_CALLS=str(self.calls), NETWASM_WIT_TOOL_ASSET_ROOT=str(self.assets))

    def run_script(self, *args, **env):
        return subprocess.run(["bash", str(SCRIPT), *map(str, args)],
                              env=dict(self.env, **env), capture_output=True)

    def read_calls(self):
        return [json.loads(line) for line in self.calls.read_text().splitlines()]

    def test_rejects_missing_relative_existing_and_dangling_paths_before_tools(self):
        dangling = self.root / "dangling"
        dangling.symlink_to(self.root / "absent")
        for args in [(), ("relative",), (self.root,), (dangling,),
                     (self.root / "new", "extra")]:
            with self.subTest(args=args):
                self.assertEqual(2, self.run_script(*args).returncode)
                self.assertFalse(self.calls.exists())

    def test_installs_exact_locally_packed_version_into_owned_directory(self):
        target = self.root / "tool"
        self.assertEqual(0, self.run_script(target).returncode)
        calls = self.read_calls()
        self.assertEqual(3, len(calls))
        self.assertIn("-getProperty:Version", calls[0])
        self.assertIn("--no-build", calls[1])
        self.assertIn("--no-restore", calls[1])
        packed = next(arg.removeprefix("-p:PackageVersion=") for arg in calls[1]
                      if arg.startswith("-p:PackageVersion="))
        self.assertRegex(packed, r"^1\.2\.3-testharness\.\d{14}\.\d+$")
        self.assertEqual(packed, calls[2][calls[2].index("--version") + 1])
        self.assertEqual(str(target / "feed"), calls[2][calls[2].index("--add-source") + 1])
        self.assertTrue((target / "bin/netwasm-wit-bindgen").is_file())
        self.assertTrue((target / "pack.stdout").is_file())
        self.assertTrue((target / "install.stderr").is_file())
        self.assertEqual(5, len(list((target / "platform-assets").iterdir())))
        self.assertIn(f"-p:NetWasmToolchainWasmToolsAssetRoot={target / 'platform-assets'}", calls[1])

    def test_rejects_invalid_project_version_without_pack(self):
        self.assertEqual(1, self.run_script(self.root / "tool", WIT_TEST_VERSION="invalid").returncode)
        self.assertEqual(1, len(self.read_calls()))

    def test_rejects_relative_or_missing_assets_before_tools(self):
        for assets in ["relative", str(self.root / "missing")]:
            with self.subTest(assets=assets):
                self.assertEqual(2, self.run_script(self.root / "tool",
                                 NETWASM_WIT_TOOL_ASSET_ROOT=assets).returncode)
                self.assertFalse(self.calls.exists())

    def test_pack_failure_stops_before_install_and_retains_logs(self):
        target = self.root / "tool"
        self.assertEqual(7, self.run_script(target, WIT_TEST_PACK_EXIT="7").returncode)
        self.assertEqual(2, len(self.read_calls()))
        self.assertTrue((target / "pack.stderr").is_file())

    def test_install_failure_is_not_reported_as_success(self):
        target = self.root / "tool"
        self.assertEqual(8, self.run_script(target, WIT_TEST_INSTALL_EXIT="8").returncode)
        self.assertEqual(3, len(self.read_calls()))
        self.assertTrue((target / "install.stderr").is_file())


if __name__ == "__main__":
    unittest.main()

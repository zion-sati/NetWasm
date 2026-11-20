# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

from __future__ import annotations

import importlib.util
import json
import re
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).parents[2]
PROJECT = REPOSITORY_ROOT / "src/NetWasm.Runtime.Pack/NetWasm.Runtime.Pack.csproj"
REGENERATE = REPOSITORY_ROOT / "src/NetWasm.Runtime.Pack/tools/regenerate-runtime-pack.sh"
VERIFIER = REPOSITORY_ROOT / "eng/verify-runtime-pack-package.py"
SPEC = importlib.util.spec_from_file_location("verify_runtime_pack_package", VERIFIER)
assert SPEC is not None and SPEC.loader is not None
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)

COLLECTOR_FILES = ("libnetwasm-runtime.a", "libgc.a", "libnetwasm-runtime-wasi.a", "libnetwasm-runtime-nowasi.a",
                   "layout.json")


def runtime_tree_files() -> list[str]:
    """Every file a regenerated runtime pack holds, relative to its runtime directory."""
    files = ["LICENSE.txt", "runtime-pack.json", "runtime-policy.json", "runtime-exports.txt"]
    for target in VERIFY.TARGETS:
        files += [f"{target}/allowed-undefined-symbols.txt", f"{target}/libnetwasm-emscripten-support.a",
                  f"{target}/libnetwasm-emscripten-stand-ins.a", f"{target}/system-libraries/libc.a"]
        for collector in VERIFY.GARBAGE_COLLECTORS:
            for profile in ("", "speed/"):
                files += [f"{target}/{collector.lower()}/{profile}{name}" for name in COLLECTOR_FILES]
    return files


def packed_paths(runtime_root: Path) -> set[str]:
    result = subprocess.run(
        ["dotnet", "msbuild", str(PROJECT), "-p:ExcludeRestorePackageImports=true",
         f"-p:NetWasmRuntimePackAssetRoot={runtime_root}", "-getItem:None"],
        capture_output=True, text=True, check=True, cwd=REPOSITORY_ROOT,
    )
    paths = set()
    for item in json.loads(result.stdout)["Items"]["None"]:
        if item.get("Pack") != "true":
            continue
        package_path = item["PackagePath"]
        # NuGet's pack puts a file whose PackagePath is a folder under that folder, at the directories a
        # wildcard's ** matched (RecursiveDir) and by its own name.
        paths.add(package_path + item.get("RecursiveDir", "") + item["Filename"] + item["Extension"]
                  if package_path.endswith("/") else package_path)
    return paths


class RuntimePackProjectTests(unittest.TestCase):
    def test_packs_every_collector_the_package_verifier_requires(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            runtime_root = Path(temporary)
            for name in [*runtime_tree_files(), ".DS_Store", "wasm32/.DS_Store"]:
                (runtime_root / name).parent.mkdir(parents=True, exist_ok=True)
                (runtime_root / name).write_bytes(b"")

            packed = packed_paths(runtime_root)

        expected = {"README.md", "LICENSE.txt", "buildTransitive/NetWasm.Runtime.Pack.props",
                    "buildTransitive/NetWasm.Runtime.Pack.targets",
                    *(f"runtime/{name}" for name in runtime_tree_files() if name != "LICENSE.txt")}
        self.assertEqual(sorted(expected - packed), [], "files the package verifier requires are not packed")
        self.assertEqual(sorted(packed - expected), [], "files packed that the runtime pack does not own")

    def test_regeneration_builds_every_collector_the_package_verifier_requires(self) -> None:
        loop = re.search(r"for collector_directory in ([a-z ]+); do", REGENERATE.read_text(encoding="utf-8"))
        self.assertIsNotNone(loop)
        self.assertEqual(sorted(loop.group(1).split()),
                         sorted(collector.lower() for collector in VERIFY.GARBAGE_COLLECTORS))


if __name__ == "__main__":
    unittest.main()

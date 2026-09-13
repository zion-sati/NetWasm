#!/usr/bin/env python3

"""Command-contract tests for the public and qualification toolchain checks."""

from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VERIFIER = REPOSITORY_ROOT / "eng" / "verify-toolchain.sh"
SYSTEM_PATH = "/usr/bin:/bin"


class VerifyToolchainTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.emsdk = self.root / "emsdk"
        (self.emsdk / "upstream" / "emscripten").mkdir(parents=True)
        (self.emsdk / "upstream" / "bin").mkdir(parents=True)
        self.modules = self.root / "node_modules" / ".bin"
        self.modules.mkdir(parents=True)
        self.real_node = shutil.which("node")
        if self.real_node is None:
            self.skipTest("Node.js is required to exercise the verifier")
        self._write_source_tools(node_version="24.19.0", lld_major=24)

    def tearDown(self) -> None:
        self._temporary.cleanup()

    def test_public_source_contract_accepts_emsdk_node_24_without_qualification_tools(self) -> None:
        result = self._run()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("NetWasm toolchain PASS", result.stdout)

    def test_public_source_contract_rejects_node_below_24(self) -> None:
        self._write_node("23.11.1")

        result = self._run()

        self.assertEqual(1, result.returncode)
        self.assertIn("required major 24 or newer", result.stderr)

    def test_public_source_contract_rejects_lld_below_24(self) -> None:
        self._write_executable(
            self.emsdk / "upstream" / "bin" / "wasm-ld",
            "printf 'LLD 23.0.0\\n'",
        )

        result = self._run()

        self.assertEqual(1, result.returncode)
        self.assertIn("required LLD major 24 or newer", result.stderr)

    def test_qualification_contract_requires_its_additional_tools(self) -> None:
        result = self._run("--qualification")

        self.assertEqual(1, result.returncode)
        self.assertIn("missing qualification tool", result.stderr)

    def test_qualification_contract_accepts_exact_pinned_versions(self) -> None:
        self._write_node("26.7.0")
        self._write_executable(self.bin / "wasm-validate", "printf '1.0.41\\n'")
        self._write_executable(self.bin / "wasm-tools", "printf 'wasm-tools 1.256.0\\n'")
        self._write_executable(self.bin / "wasmtime", "printf 'wasmtime 47.0.3 (test)\\n'")

        result = self._run("--qualification")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("NetWasm toolchain PASS", result.stdout)

    def test_unknown_option_is_rejected_before_tool_discovery(self) -> None:
        result = self._run("--unknown")

        self.assertEqual(2, result.returncode)
        self.assertIn("Usage:", result.stderr)

    def _run(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        environment = os.environ.copy()
        environment.update({
            "PATH": f"{self.bin}:{SYSTEM_PATH}",
            "NETWASM_EMSDK_ROOT": str(self.emsdk),
            "NETWASM_NODE_PATH": str(self.bin / "node"),
            "NETWASM_NODE_MODULES_ROOT": str(self.root / "node_modules"),
        })
        return subprocess.run(
            ["bash", str(VERIFIER), *arguments],
            cwd=REPOSITORY_ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )

    def _write_source_tools(self, *, node_version: str, lld_major: int) -> None:
        self._write_node(node_version)
        self._write_executable(self.bin / "npm", ":")
        self._write_executable(
            self.bin / "dotnet",
            """
if [[ "${1:-}" == "--version" ]]; then
    printf '10.0.302\\n'
else
    printf '5.6.0-2.26329.109\\n'
fi
""",
        )
        csc = self.bin / "sdk" / "10.0.302" / "Roslyn" / "bincore" / "csc.dll"
        csc.parent.mkdir(parents=True)
        csc.touch()
        self._write_executable(
            self.emsdk / "upstream" / "emscripten" / "emcc",
            "printf 'emcc (Emscripten gcc/clang-like replacement) 6.0.7 (test)\\n'",
        )
        for tool in ("wasm-as", "wasm-dis", "wasm-merge"):
            self._write_executable(self.emsdk / "upstream" / "bin" / tool, ":")
        self._write_executable(
            self.emsdk / "upstream" / "bin" / "wasm-opt",
            "printf 'wasm-opt version 132\\n'",
        )
        self._write_executable(
            self.emsdk / "upstream" / "bin" / "wasm-ld",
            f"printf 'LLD {lld_major}.0.0\\n'",
        )
        self._write_executable(
            self.modules / "playwright",
            "printf 'Version 1.62.1\\n'",
        )
        self._write_executable(self.modules / "jco", "printf '1.28.1\\n'")

    def _write_node(self, version: str) -> None:
        self._write_executable(
            self.bin / "node",
            f"""
if [[ "${{1:-}}" == "--version" ]]; then
    printf 'v{version}\\n'
else
    exec {self.real_node!r} "$@"
fi
""",
        )

    @staticmethod
    def _write_executable(path: Path, body: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(f"#!/usr/bin/env bash\nset -euo pipefail\n{body}\n", encoding="utf-8")
        path.chmod(0o755)


if __name__ == "__main__":
    unittest.main()

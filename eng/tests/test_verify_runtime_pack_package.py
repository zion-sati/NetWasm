from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "verify-runtime-pack-package.py"
SPEC = importlib.util.spec_from_file_location("verify_runtime_pack_package", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class RuntimePackPackageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.version = "0.1.0-test.1"
        (self.root / "eng").mkdir()
        (self.root / "eng/toolchain.json").write_text(json.dumps({"emscripten": "6.0.7"}))
        policy_root = self.root / "src/NetWasm.Runtime.Pack/runtime"
        policy_root.mkdir(parents=True)
        policy = {"defaultGarbageCollector": "Boehm", "targets": {
            target: {"systemLibraries": ["libc.a"]} for target in ("wasm32", "wasm64")}}
        (policy_root / "runtime-policy.json").write_text(json.dumps(policy))
        self.entries = {}
        targets = []
        for target in ("wasm32", "wasm64"):
            for collector in ("Compact", "Boehm"):
                prefix = f"{target}/{collector.lower()}"
                self.entries[f"runtime/{prefix}/layout.json"] = json.dumps({
                    "schemaVersion": 1, "target": target, "runtimeFootprintBytes": 70_000,
                }).encode()
                targets.append({
                    "target": target,
                    "garbageCollector": collector,
                    "runtimeFootprintBytes": 70_000,
                    "runtimeArchive": self.asset(f"{prefix}/libnetwasm-runtime.a", b"runtime"),
                    "collectorArchive": self.asset(f"{prefix}/libgc.a", b"collector"),
                    "allowedUndefinedSymbols": self.asset(f"{target}/allowed-undefined-symbols.txt",
                                                          b"emscripten_notify_memory_growth\n"),
                    "systemLibraries": {"names": ["libc.a"], "assets": [
                        self.asset(f"{target}/system-libraries/libc.a", b"system")]},
                })
        self.manifest = {"schemaVersion": 5, "emscriptenVersion": "6.0.7",
                         "defaultGarbageCollector": "Boehm", "targets": targets}
        self.entries["LICENSE.txt"] = "\n".join([
            "=== NetWasm code: MIT ===", "=== Emscripten notice ===",
            "=== musl libc notice ===", "=== LLVM compiler-rt notice ===",
            "=== Compact collector LICENSE ===", "=== Compact collector NOTICE ===",
            "=== Compact collector UPSTREAM-NOTICE ===",
        ]).encode()
        self.entries["NetWasm.Runtime.Pack.nuspec"] = b'<package><metadata><license type="file">LICENSE.txt</license></metadata></package>'

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def asset(self, path: str, data: bytes) -> dict[str, str]:
        self.entries["runtime/" + path] = data
        return {"path": path, "sha256": hashlib.sha256(data).hexdigest()}

    def package(self, manifest: dict | None = None) -> Path:
        path = self.root / f"NetWasm.Runtime.Pack.{self.version}.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            for name, data in self.entries.items():
                archive.writestr(name, data)
            archive.writestr("runtime/runtime-pack.json", json.dumps(manifest or self.manifest))
        return path

    def test_accepts_four_pairs_with_shared_closure(self) -> None:
        MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_old_schema(self) -> None:
        manifest = self.manifest | {"schemaVersion": 4}
        with self.assertRaisesRegex(ValueError, "source version"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_missing_duplicate_or_noncanonical_pair(self) -> None:
        for mutation in ("missing", "duplicate", "invalid"):
            with self.subTest(mutation=mutation):
                manifest = copy.deepcopy(self.manifest)
                if mutation == "missing":
                    manifest["targets"].pop()
                elif mutation == "duplicate":
                    manifest["targets"][0] = manifest["targets"][1]
                else:
                    manifest["targets"][0]["garbageCollector"] = "compact"
                with self.assertRaisesRegex(ValueError, "inventory"):
                    MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_policy_default_mismatch(self) -> None:
        manifest = self.manifest | {"defaultGarbageCollector": "Compact"}
        with self.assertRaisesRegex(ValueError, "default collector"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_cross_collector_archive(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["targets"][0]["collectorArchive"] = manifest["targets"][1]["collectorArchive"]
        with self.assertRaisesRegex(ValueError, "archive paths"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_missing_compact_notices(self) -> None:
        self.entries["LICENSE.txt"] = self.entries["LICENSE.txt"].replace(
            b"=== Compact collector NOTICE ===", b"")
        with self.assertRaisesRegex(ValueError, "upstream notice"):
            MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_mismatched_collector_layout(self) -> None:
        self.entries["runtime/wasm32/compact/layout.json"] = json.dumps({
            "schemaVersion": 1, "target": "wasm32", "runtimeFootprintBytes": 70_001,
        }).encode()
        with self.assertRaisesRegex(ValueError, "collector layout"):
            MODULE.inspect(self.package(), self.root, self.version)


if __name__ == "__main__":
    unittest.main()

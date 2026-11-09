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
        policy = {"defaultGarbageCollector": "Compact", "targets": {
            target: {"systemLibraries": ["libc.a"]} for target in ("wasm32", "wasm64")}}
        (policy_root / "runtime-policy.json").write_text(json.dumps(policy))
        self.entries = {}
        targets = []
        for target in ("wasm32", "wasm64"):
            for collector in ("Compact", "NetWasm", "Bump"):
                prefix = f"{target}/{collector.lower()}"
                self.entries[f"runtime/{prefix}/layout.json"] = json.dumps({
                    "schemaVersion": 1, "target": target, "runtimeFootprintBytes": 70_000,
                }).encode()
                self.entries[f"runtime/{prefix}/speed/layout.json"] = json.dumps({
                    "schemaVersion": 1, "target": target, "runtimeFootprintBytes": 74_096,
                }).encode()
                targets.append({
                    "target": target,
                    "garbageCollector": collector,
                    "runtimeFootprintBytes": 70_000,
                    "nativeStackSizeBytes": 65_536,
                    "runtimeArchive": self.asset(f"{prefix}/libnetwasm-runtime.a", b"runtime"),
                    "collectorArchive": self.asset(f"{prefix}/libgc.a", b"collector"),
                    "platformArchives": {
                        "wasi": self.asset(f"{prefix}/libnetwasm-runtime-wasi.a", b"wasi"),
                        "noWasi": self.asset(f"{prefix}/libnetwasm-runtime-nowasi.a", b"nowasi"),
                    },
                    "speed": {
                        "runtimeFootprintBytes": 74_096,
                        "runtimeArchive": self.asset(f"{prefix}/speed/libnetwasm-runtime.a", b"speed-runtime"),
                        "collectorArchive": self.asset(f"{prefix}/speed/libgc.a", b"speed-collector"),
                        "platformArchives": {
                            "wasi": self.asset(f"{prefix}/speed/libnetwasm-runtime-wasi.a", b"speed-wasi"),
                            "noWasi": self.asset(f"{prefix}/speed/libnetwasm-runtime-nowasi.a", b"speed-nowasi"),
                        },
                    },
                    "allowedUndefinedSymbols": self.asset(f"{target}/allowed-undefined-symbols.txt",
                                                          b"emscripten_notify_memory_growth\n"),
                    "systemLibraries": {"names": ["libc.a"], "assets": [
                        self.asset(f"{target}/system-libraries/libc.a", b"system")]},
                })
        self.manifest = {"schemaVersion": 6, "emscriptenVersion": "6.0.7",
                         "defaultGarbageCollector": "Compact", "exports": ["allocate"],
                         "targets": targets}
        self.entries["runtime/runtime-exports.txt"] = b"allocate\n"
        self.entries["LICENSE.txt"] = "\n".join([
            "=== NetWasm runtime support: MIT, except separately identified collectors ===", "=== Emscripten notice ===",
            "=== musl libc notice ===", "=== LLVM compiler-rt notice ===",
            "=== NetWasmGC LICENSE ===", "=== NetWasmGC NOTICE ===",
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

    def test_accepts_six_pairs_with_shared_closure(self) -> None:
        MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_old_schema(self) -> None:
        manifest = self.manifest | {"schemaVersion": 5}
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

    def test_rejects_missing_or_invalid_speed_contract(self) -> None:
        for speed in (None, {}, {"runtimeFootprintBytes": 65_535}, {"runtimeFootprintBytes": "74096"}):
            with self.subTest(speed=speed):
                manifest = copy.deepcopy(self.manifest)
                manifest["targets"][0]["speed"] = speed
                with self.assertRaisesRegex(ValueError, "speed profile"):
                    MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_speed_layout_mismatch(self) -> None:
        self.entries["runtime/wasm32/compact/speed/layout.json"] = json.dumps({
            "schemaVersion": 1, "target": "wasm32", "runtimeFootprintBytes": 74_097,
        }).encode()
        with self.assertRaisesRegex(ValueError, "speed layout"):
            MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_cross_profile_archive(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["targets"][0]["speed"]["collectorArchive"] = manifest["targets"][0]["collectorArchive"]
        with self.assertRaisesRegex(ValueError, "archive paths"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_speed_archive_digest_mismatch(self) -> None:
        self.entries["runtime/wasm64/bump/speed/libgc.a"] = b"changed-speed-collector"
        with self.assertRaisesRegex(ValueError, "digest mismatch"):
            MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_policy_default_mismatch(self) -> None:
        manifest = self.manifest | {"defaultGarbageCollector": "NetWasm"}
        with self.assertRaisesRegex(ValueError, "default collector"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_test_only_export(self) -> None:
        manifest = self.manifest | {"exports": ["allocate", "test_allocation_count"]}
        with self.assertRaisesRegex(ValueError, "test-only export"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_export_file_mismatch(self) -> None:
        self.entries["runtime/runtime-exports.txt"] = b"allocate\ncollect\n"
        with self.assertRaisesRegex(ValueError, "does not match"):
            MODULE.inspect(self.package(), self.root, self.version)

    def test_rejects_cross_collector_archive(self) -> None:
        manifest = copy.deepcopy(self.manifest)
        manifest["targets"][0]["collectorArchive"] = manifest["targets"][1]["collectorArchive"]
        with self.assertRaisesRegex(ValueError, "archive paths"):
            MODULE.inspect(self.package(manifest), self.root, self.version)

    def test_rejects_missing_netwasmgc_license_or_notice(self) -> None:
        complete = self.entries["LICENSE.txt"]
        for section in (b"=== NetWasmGC LICENSE ===", b"=== NetWasmGC NOTICE ==="):
            with self.subTest(section=section):
                self.entries["LICENSE.txt"] = complete.replace(section, b"")
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

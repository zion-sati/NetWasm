"""Synthetic package gates for the restore-owned native host tools."""

from __future__ import annotations

import copy
import hashlib
import io
import importlib.util
import json
import stat
import sys
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]


def load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / "eng" / file)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


stage = load("netwasm_stage_host_tools", "stage-host-tools.py")
verify = load("netwasm_verify_host_tools", "verify-host-tools-package.py")


class HostToolsPackageTests(unittest.TestCase):
    RID = "osx-arm64"
    VERSION = "0.1.0"
    PACKAGE_ID = "NetWasm.HostTools.osx-arm64"

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="netwasm-host-tools-test-")
        self.addCleanup(self.temporary.cleanup)
        self.package = Path(self.temporary.name) / f"{self.PACKAGE_ID}.{self.VERSION}.nupkg"
        self.pins = json.loads((ROOT / "eng/toolchain.json").read_text())

    def make_package(self, *, defect: str | None = None) -> None:
        executable = b"\xcf\xfa\xed\xfe\x0c\x00\x00\x01" + b"test"
        payload = {
            "LICENSE.txt": b"combined",
            "README.md": b"readme",
            "licenses/Node-LICENSE": b"node notice",
            "licenses/LLVM-LICENSE.txt": b"llvm notice",
            "licenses/LLD-LICENSE.txt": b"lld notice",
            "licenses/LLVM-BLAKE3-LICENSE": b"blake3 notice",
            "licenses/LLVM-ThirdParty-NOTICES.txt": b"embedded notices",
            "licenses/Binaryen-LICENSE": b"binaryen notice",
            **{f"tools/bin/{role}": executable for role in verify.ROLES},
        }
        roles = {role: f"tools/bin/{role}" for role in verify.ROLES}
        manifest = {
            "schemaVersion": 1,
            "packageId": self.PACKAGE_ID,
            "packageVersion": self.VERSION,
            "hostRid": "win-x64" if defect == "wrong-host" else self.RID,
            "sourceCommit": "a" * 40,
            "upstream": {
                "emscripten": self.pins["emscripten"],
                "emsdkReleaseCommit": self.pins["hostArtifacts"]["emsdkReleaseCommit"],
                "lldOrigin": "emsdk-release",
                "llvmLld": self.pins["llvmLld"],
                "binaryen": self.pins["binaryen"],
                "node": self.pins["node"],
                "archives": [
                    {"role": role, **self.pins["hostArtifacts"]["hosts"][self.RID][role]}
                    for role in ("emsdk", "node")
                ],
            },
            "roles": roles,
            "files": [
                {
                    "path": name,
                    "size": len(data),
                    "sha256": hashlib.sha256(data).hexdigest(),
                }
                for name, data in sorted(payload.items())
            ],
        }
        if defect == "wrong-architecture":
            payload["tools/bin/node"] = b"\x7fELFwrong"
            data = payload["tools/bin/node"]
            file = next(item for item in manifest["files"] if item["path"] == "tools/bin/node")
            file["size"] = len(data)
            file["sha256"] = hashlib.sha256(data).hexdigest()
        if defect == "wrong-hash":
            payload["tools/bin/node"] = b"changed"
        if defect == "missing-notice":
            del payload["licenses/Node-LICENSE"]
        if defect == "extra-binary":
            payload["tools/bin/unexpected"] = executable
        with zipfile.ZipFile(self.package, "w") as archive:
            metadata = {
                "[Content_Types].xml": b"",
                "_rels/.rels": b"",
                "package/services/metadata/core-properties/core-properties.psmdcp": b"",
                f"{self.PACKAGE_ID}.nuspec": (
                    f'<package><metadata><id>{self.PACKAGE_ID}</id>'
                    f'<version>{self.VERSION}</version>'
                    '<repository type="git" url="https://github.com/zion-sati/netwasm" '
                    f'commit="{"a" * 40}"/></metadata></package>'
                ).encode(),
                "tools/host-tools-manifest.json": json.dumps(manifest).encode(),
            }
            for name, data in sorted((payload | metadata).items()):
                entry = zipfile.ZipInfo(name)
                entry.create_system = 3
                mode = stat.S_IFLNK | 0o777 if defect == "symlink" and name == "tools/bin/node" else (
                    stat.S_IFREG | (0o755 if name.startswith("tools/bin/") else 0o644)
                )
                entry.external_attr = mode << 16
                archive.writestr(entry, data)

    def test_valid_payload_is_accepted(self):
        self.make_package()
        result = verify.inspect(self.package, self.RID, self.VERSION)
        self.assertEqual(self.PACKAGE_ID, result["packageId"])
        self.assertEqual("a" * 40, result["sourceCommit"])

    def test_missing_notice_extra_binary_bad_hash_architecture_symlink_and_host_fail(self):
        for defect in (
            "missing-notice", "extra-binary", "wrong-hash",
            "wrong-architecture", "symlink", "wrong-host",
        ):
            with self.subTest(defect=defect):
                self.make_package(defect=defect)
                with self.assertRaises(ValueError):
                    verify.inspect(self.package, self.RID, self.VERSION)

    def test_package_size_cap_is_enforced(self):
        self.make_package()
        with patch.object(verify, "MAX_PACKAGE_BYTES", 1):
            with self.assertRaisesRegex(ValueError, "size budget"):
                verify.inspect(self.package, self.RID, self.VERSION)

    def test_archive_path_traversal_and_symlink_escape_are_rejected(self):
        for unsafe in ("../escape", "/absolute", "nested/../escape", "nested\\escape"):
            with self.subTest(path=unsafe), self.assertRaises(ValueError):
                stage.safe_member(unsafe)

        archive = Path(self.temporary.name) / "escape.tar"
        with tarfile.open(archive, "w") as writer:
            link = tarfile.TarInfo("install/bin/wasm-ld")
            link.type = tarfile.SYMTYPE
            link.linkname = "../../outside"
            writer.addfile(link)
            for name in ("lld", "wasm-opt", "wasm-merge"):
                data = name.encode()
                item = tarfile.TarInfo(f"install/bin/{name}")
                item.size = len(data)
                writer.addfile(item, io.BytesIO(data))
        with self.assertRaisesRegex(ValueError, "in-archive link"):
            stage.read_members(archive, {
                "wasm-ld": "install/bin/lld",
                "wasm-opt": "install/bin/wasm-opt",
                "wasm-merge": "install/bin/wasm-merge",
            })

    def test_release_archive_download_and_cache_are_hash_pinned(self):
        payload = b"versioned release asset"
        source = {
            "url": "https://example.invalid/wasm-ld.zip",
            "sha256": hashlib.sha256(payload).hexdigest(),
        }
        cache = Path(self.temporary.name) / "cache"
        with patch.object(stage.urllib.request, "urlopen", return_value=io.BytesIO(payload)):
            with self.assertRaisesRegex(ValueError, "archive digest mismatch"):
                stage.acquire(dict(source, sha256="0" * 64), cache)
        with patch.object(stage.urllib.request, "urlopen", return_value=io.BytesIO(payload)):
            cached = stage.acquire(source, cache)
        self.assertEqual(payload, cached.read_bytes())
        cached.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "Cached host-tool archive digest mismatch"):
            stage.acquire(source, cache)

    def test_windows_arm64_release_binds_native_output_source_and_license(self):
        pins = copy.deepcopy(self.pins)
        license_bytes = b"pinned upstream LLVM license"
        notice_files = {
            "LLVM-LICENSE.txt": ("llvm", license_bytes),
            "LLD-LICENSE.txt": ("lld", b"pinned upstream LLD license"),
            "LLVM-BLAKE3-LICENSE": ("blake3", b"pinned bundled BLAKE3 license"),
            "LLVM-ThirdParty-NOTICES.txt": ("llvmThirdPartyNoticeSha256", b"pinned bundled notices"),
        }
        for _, (role, data) in notice_files.items():
            source = pins["hostArtifacts"]["licenseSources"]
            if role == "llvmThirdPartyNoticeSha256":
                source[role] = hashlib.sha256(data).hexdigest()
            else:
                source[role]["sha256"] = hashlib.sha256(data).hexdigest()
        binary = bytearray(128)
        binary[:2] = b"MZ"
        binary[0x3c:0x40] = (0x40).to_bytes(4, "little")
        binary[0x40:0x44] = b"PE\0\0"
        binary[0x44:0x46] = (0xAA64).to_bytes(2, "little")
        archive_path = Path(self.temporary.name) / "lld-release.zip"
        receipt = {
            "schemaVersion": 1,
            "hostRid": "win-arm64",
            "sourceCommit": pins["hostArtifacts"]["hosts"]["win-arm64"]["lldRelease"]["builderCommit"],
            "llvmCommit": pins["llvmLld"]["commit"],
            "sourceArchiveSha256": "b" * 64,
            "sourceTreeSha256": pins["hostArtifacts"]["hosts"]["win-arm64"]["llvmSource"]["treeSha256"],
            "outputSha256": hashlib.sha256(binary).hexdigest(),
            "outputSize": len(binary),
            "cmakeFlags": pins["hostArtifacts"]["winArm64LldCMakeFlags"],
            "compilerVersion": "19.50.1",
            "windowsSdkVersion": "10.0.26100.0",
            "cmakeVersion": "4.1.0",
            "ninjaVersion": "1.12.0",
        }

        def write_archive(license_payload=license_bytes):
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("wasm-ld.exe", binary)
                archive.writestr("build-receipt.json", json.dumps(receipt))
                for name, (_, data) in notice_files.items():
                    archive.writestr(name, license_payload if name == "LLVM-LICENSE.txt" else data)

        write_archive()
        payload, loaded = stage.published_windows_arm64_lld(archive_path, pins)
        self.assertEqual(bytes(binary), payload)
        self.assertEqual(receipt, loaded)

        binary[0x44:0x46] = (0x8664).to_bytes(2, "little")
        write_archive()
        with self.assertRaisesRegex(ValueError, "native Windows ARM64"):
            stage.published_windows_arm64_lld(archive_path, pins)

        binary[0x44:0x46] = (0xAA64).to_bytes(2, "little")
        receipt["sourceTreeSha256"] = "0" * 64
        write_archive()
        with self.assertRaisesRegex(ValueError, "pinned source"):
            stage.published_windows_arm64_lld(archive_path, pins)

        receipt["sourceTreeSha256"] = pins["hostArtifacts"]["hosts"]["win-arm64"]["llvmSource"]["treeSha256"]
        receipt["sourceCommit"] = "a" * 40
        write_archive()
        with self.assertRaisesRegex(ValueError, "pinned source"):
            stage.published_windows_arm64_lld(archive_path, pins)

        receipt["sourceCommit"] = pins["hostArtifacts"]["hosts"]["win-arm64"]["lldRelease"]["builderCommit"]
        write_archive(b"wrong license")
        with self.assertRaisesRegex(ValueError, "notice digest"):
            stage.published_windows_arm64_lld(archive_path, pins)

        with zipfile.ZipFile(archive_path, "w") as archive:
            archive.writestr("wasm-ld.exe", binary)
            archive.writestr("build-receipt.json", json.dumps(receipt))
        with self.assertRaisesRegex(ValueError, "inventory"):
            stage.published_windows_arm64_lld(archive_path, pins)

    def test_windows_arm64_package_accepts_pinned_tree_with_distinct_archive_hash(self):
        rid = "win-arm64"
        package_id = f"NetWasm.HostTools.{rid}"
        package = Path(self.temporary.name) / f"{package_id}.{self.VERSION}.nupkg"
        binary = bytearray(128)
        binary[:2] = b"MZ"
        binary[0x3c:0x40] = (0x40).to_bytes(4, "little")
        binary[0x40:0x44] = b"PE\0\0"
        binary[0x44:0x46] = (0xAA64).to_bytes(2, "little")
        roles = {role: f"tools/bin/{role}.exe" for role in verify.ROLES}
        payload = {
            "LICENSE.txt": b"combined",
            "README.md": b"readme",
            "licenses/Node-LICENSE": b"node notice",
            "licenses/LLVM-LICENSE.txt": b"llvm notice",
            "licenses/LLD-LICENSE.txt": b"lld notice",
            "licenses/LLVM-BLAKE3-LICENSE": b"blake3 notice",
            "licenses/LLVM-ThirdParty-NOTICES.txt": b"embedded notices",
            "licenses/Binaryen-LICENSE": b"binaryen notice",
            **{path: bytes(binary) for path in roles.values()},
        }
        artifacts = self.pins["hostArtifacts"]
        receipt = {
            "schemaVersion": 1,
            "hostRid": rid,
            "sourceCommit": artifacts["hosts"][rid]["lldRelease"]["builderCommit"],
            "llvmCommit": self.pins["llvmLld"]["commit"],
            "sourceArchiveSha256": "b" * 64,
            "sourceTreeSha256": artifacts["hosts"][rid]["llvmSource"]["treeSha256"],
            "outputSha256": hashlib.sha256(binary).hexdigest(),
            "outputSize": len(binary),
            "cmakeFlags": artifacts["winArm64LldCMakeFlags"],
            "compilerVersion": "19.50.1",
            "windowsSdkVersion": "10.0.26100.0",
            "cmakeVersion": "4.1.0",
            "ninjaVersion": "1.12.0",
        }
        manifest = {
            "schemaVersion": 1,
            "packageId": package_id,
            "packageVersion": self.VERSION,
            "hostRid": rid,
            "sourceCommit": "a" * 40,
            "upstream": {
                "emscripten": self.pins["emscripten"],
                "emsdkReleaseCommit": artifacts["emsdkReleaseCommit"],
                "lldOrigin": "pinned-native-tools-release",
                "llvmLld": self.pins["llvmLld"],
                "binaryen": self.pins["binaryen"],
                "node": self.pins["node"],
                "archives": [
                    {"role": role, **artifacts["hosts"][rid][role]}
                    for role in ("llvmSource", "lldRelease", "binaryen", "node")
                ],
                "lldBuild": receipt,
            },
            "roles": roles,
            "files": [
                {"path": name, "size": len(data),
                 "sha256": hashlib.sha256(data).hexdigest()}
                for name, data in sorted(payload.items())
            ],
        }
        with zipfile.ZipFile(package, "w") as archive:
            metadata = {
                "[Content_Types].xml": b"",
                "_rels/.rels": b"",
                "package/services/metadata/core-properties/core-properties.psmdcp": b"",
                f"{package_id}.nuspec": (
                    f'<package><metadata><id>{package_id}</id>'
                    f'<version>{self.VERSION}</version>'
                    '<repository type="git" url="https://github.com/zion-sati/netwasm" '
                    f'commit="{"a" * 40}"/></metadata></package>'
                ).encode(),
                "tools/host-tools-manifest.json": json.dumps(manifest).encode(),
            }
            for name, data in sorted((payload | metadata).items()):
                entry = zipfile.ZipInfo(name)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | 0o644) << 16
                archive.writestr(entry, data)
        self.assertEqual(package_id, verify.inspect(package, rid, self.VERSION)["packageId"])


if __name__ == "__main__":
    unittest.main()

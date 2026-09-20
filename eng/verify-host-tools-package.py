#!/usr/bin/env python3
"""Verify one immutable NetWasm native host-tools NuGet payload."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import zipfile
from pathlib import Path
from xml.etree import ElementTree


ROOT = Path(__file__).resolve().parent.parent
MAX_PACKAGE_BYTES = 240_000_000
ROLES = ("node", "wasm-ld", "wasm-opt", "wasm-merge")


def safe_path(path: str) -> None:
    if not path or path.startswith("/") or "\\" in path or any(
        part in ("", ".", "..") for part in path.split("/")
    ):
        raise ValueError(f"Unsafe package entry: {path}")


def assert_architecture(data: bytes, rid: str, role: str) -> None:
    if rid.startswith("linux-"):
        machine = 62 if rid == "linux-x64" else 183
        if data[:4] != b"\x7fELF" or len(data) < 20 or int.from_bytes(data[18:20], "little") != machine:
            raise ValueError(f"{role} has the wrong ELF architecture for {rid}.")
    elif rid.startswith("osx-"):
        if data[:4] != b"\xcf\xfa\xed\xfe" or len(data) < 8 or int.from_bytes(data[4:8], "little") != 0x0100000C:
            raise ValueError(f"{role} has the wrong Mach-O architecture for {rid}.")
    elif rid.startswith("win-"):
        machine = 0x8664 if rid == "win-x64" else 0xAA64
        if data[:2] != b"MZ" or len(data) < 0x40:
            raise ValueError(f"{role} is not a PE executable.")
        offset = int.from_bytes(data[0x3C:0x40], "little")
        if data[offset:offset + 4] != b"PE\0\0" or int.from_bytes(data[offset + 4:offset + 6], "little") != machine:
            raise ValueError(f"{role} has the wrong PE architecture for {rid}.")
    else:
        raise ValueError(f"Unsupported host RID: {rid}")


def inspect(
    path: Path,
    expected_rid: str,
    expected_version: str,
    pins_path: Path | None = None,
    source_commit: str | None = None,
) -> dict[str, object]:
    pins = json.loads((pins_path or ROOT / "eng/toolchain.json").read_text(encoding="utf-8"))
    artifacts = pins["hostArtifacts"]
    host = artifacts["hosts"].get(expected_rid)
    if host is None:
        raise ValueError(f"Unsupported host RID: {expected_rid}")
    expected_id = f"NetWasm.HostTools.{expected_rid}"
    if path.name != f"{expected_id}.{expected_version}.nupkg":
        raise ValueError(f"Unexpected host-tools package filename: {path.name}")
    if path.stat().st_size > MAX_PACKAGE_BYTES:
        raise ValueError("Host-tools package exceeds the NuGet size budget.")
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)):
            raise ValueError("Host-tools package has duplicate entries.")
        for entry in entries:
            safe_path(entry.filename)
            if stat.S_IFMT(entry.external_attr >> 16) == stat.S_IFLNK:
                raise ValueError(f"Host-tools package contains a symlink: {entry.filename}")
        if archive.testzip() is not None:
            raise ValueError("Host-tools package has a corrupt entry.")
        manifest = json.loads(archive.read("tools/host-tools-manifest.json"))
        if manifest.get("schemaVersion") != 1 or manifest.get("packageId") != expected_id or manifest.get("packageVersion") != expected_version or manifest.get("hostRid") != expected_rid:
            raise ValueError("Host-tools package manifest identity mismatch.")
        if source_commit is not None and manifest.get("sourceCommit") != source_commit:
            raise ValueError("Host-tools package source revision mismatch.")
        upstream = manifest.get("upstream", {})
        if (upstream.get("emscripten") != pins["emscripten"] or
                upstream.get("emsdkReleaseCommit") != artifacts["emsdkReleaseCommit"] or
                upstream.get("lldOrigin") != (
                    "pinned-native-tools-release" if expected_rid == "win-arm64" else "emsdk-release") or
                upstream.get("llvmLld") != pins["llvmLld"] or
                upstream.get("binaryen") != pins["binaryen"] or
                upstream.get("node") != pins["node"]):
            raise ValueError("Host-tools package uses unpinned upstream versions.")
        archive_roles = (
            ("llvmSource", "lldRelease", "binaryen", "node") if expected_rid == "win-arm64"
            else ("emsdk", "node", "libatomic") if expected_rid.startswith("linux-")
            else ("emsdk", "node")
        )
        if upstream.get("archives") != [{"role": role, **host[role]} for role in archive_roles]:
            raise ValueError("Host-tools package archive provenance mismatch.")
        expected_roles = {
            role: f"tools/bin/{role}{'.exe' if expected_rid.startswith('win-') else ''}"
            for role in ROLES
        }
        if manifest.get("roles") != expected_roles:
            raise ValueError("Host-tools package role map mismatch.")
        files = manifest.get("files")
        if not isinstance(files, list) or not files:
            raise ValueError("Host-tools package manifest has no files.")
        listed = [item["path"] for item in files]
        if listed != sorted(set(listed)):
            raise ValueError("Host-tools package file list must be sorted and unique.")
        required = set(expected_roles.values()) | {
            "LICENSE.txt", "README.md", "licenses/Node-LICENSE",
            "licenses/LLVM-LICENSE.txt", "licenses/LLD-LICENSE.txt",
            "licenses/LLVM-BLAKE3-LICENSE", "licenses/LLVM-ThirdParty-NOTICES.txt",
            "licenses/Binaryen-LICENSE",
        }
        if expected_rid.startswith("linux-"):
            required |= {
                "tools/bin/libatomic.so.1",
                "licenses/GCC-Libatomic-COPYRIGHT",
                "licenses/GPL-3.0.txt",
                "licenses/GCC-Libatomic-SOURCE.txt",
            }
        if set(listed) != required:
            raise ValueError("Host-tools package has missing or extra payload files.")
        metadata = {
            "[Content_Types].xml", "_rels/.rels",
            f"{expected_id}.nuspec",
            "package/services/metadata/core-properties/core-properties.psmdcp",
            "tools/host-tools-manifest.json",
        }
        if set(names) != set(listed) | metadata:
            raise ValueError("Host-tools package has missing or extra archive entries.")
        for item in files:
            data = archive.read(item["path"])
            if len(data) != item["size"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
                raise ValueError(f"Host-tools package payload digest mismatch: {item['path']}")
        for role, relative in expected_roles.items():
            data = archive.read(relative)
            assert_architecture(data, expected_rid, role)
            mode = archive.getinfo(relative).external_attr >> 16
            if not expected_rid.startswith("win-") and not mode & 0o111:
                raise ValueError(f"Host-tools executable has no execute bit: {relative}")
        node_patch = upstream.get("linuxNodePatch")
        if expected_rid.startswith("linux-"):
            libatomic = archive.read("tools/bin/libatomic.so.1")
            assert_architecture(libatomic, expected_rid, "libatomic")
            if hashlib.sha256(libatomic).hexdigest() != host["libatomicExecutableSha256"]:
                raise ValueError("Linux libatomic payload digest differs from the pinned binary.")
            patch_pin = artifacts["linuxNodePatcher"]
            if (not isinstance(node_patch, dict) or set(node_patch) != {
                    "upstreamNodeSha256", "packagedNodeSha256", "patcher",
                    "patcherVersion", "runpath"} or
                    node_patch["upstreamNodeSha256"] != host["nodeExecutableSha256"] or
                    node_patch["packagedNodeSha256"] != hashlib.sha256(
                        archive.read(expected_roles["node"])).hexdigest() or
                    node_patch["packagedNodeSha256"] != host["packagedNodeExecutableSha256"] or
                    node_patch["patcher"] != patch_pin["package"] or
                    node_patch["patcherVersion"] != patch_pin["version"] or
                    node_patch["runpath"] != "$ORIGIN" or
                    upstream.get("gccLibatomicSource") != artifacts["gccLibatomicSource"]):
                raise ValueError("Linux Node/libatomic closure provenance mismatch.")
            for name, pin_name in (
                ("licenses/GCC-Libatomic-COPYRIGHT", "gccLibatomicCopyright"),
                ("licenses/GPL-3.0.txt", "gpl3"),
            ):
                if hashlib.sha256(archive.read(name)).hexdigest() != \
                        artifacts["licenseSources"][pin_name]["sha256"]:
                    raise ValueError(f"Linux libatomic notice digest mismatch: {name}.")
        elif node_patch is not None or upstream.get("gccLibatomicSource") is not None:
            raise ValueError("Non-Linux host package must not claim a Linux Node patch.")
        lld_build = upstream.get("lldBuild")
        if expected_rid == "win-arm64":
            receipt_fields = {
                "schemaVersion", "hostRid", "sourceCommit", "llvmCommit",
                "sourceArchiveSha256", "sourceTreeSha256", "outputSha256", "outputSize", "cmakeFlags",
                "compilerVersion", "windowsSdkVersion", "cmakeVersion", "ninjaVersion",
            }
            if not isinstance(lld_build, dict) or (
                set(lld_build) != receipt_fields or
                lld_build.get("schemaVersion") != 1 or
                lld_build.get("hostRid") != expected_rid or
                lld_build.get("sourceCommit") != host["lldRelease"]["builderCommit"] or
                lld_build.get("llvmCommit") != pins["llvmLld"]["commit"] or
                lld_build.get("sourceTreeSha256") != host["llvmSource"]["treeSha256"] or
                not isinstance(lld_build.get("sourceArchiveSha256"), str) or
                not re.fullmatch(r"[0-9a-f]{64}", lld_build["sourceArchiveSha256"]) or
                lld_build.get("cmakeFlags") != artifacts["winArm64LldCMakeFlags"] or
                lld_build.get("outputSha256") != hashlib.sha256(
                    archive.read(expected_roles["wasm-ld"])).hexdigest() or
                lld_build.get("outputSize") != len(archive.read(expected_roles["wasm-ld"])) or
                any(not isinstance(lld_build.get(field), str) or not lld_build[field] or
                    "\\" in lld_build[field] or "/" in lld_build[field]
                    for field in ("compilerVersion", "windowsSdkVersion",
                                  "cmakeVersion", "ninjaVersion"))
            ):
                raise ValueError("Windows ARM64 LLD build receipt mismatch.")
        elif lld_build is not None:
            raise ValueError("Emsdk-origin host package must not claim a source-built LLD.")
        nuspec = ElementTree.fromstring(archive.read(f"{expected_id}.nuspec"))
        metadata_element = nuspec.find("{*}metadata")
        if metadata_element is None:
            raise ValueError("Host-tools nuspec metadata is missing.")
        def value(name: str) -> str | None:
            element = metadata_element.find(f"{{*}}{name}")
            return element.text if element is not None else None
        if value("id") != expected_id or value("version") != expected_version:
            raise ValueError("Host-tools nuspec identity mismatch.")
        repository = metadata_element.find("{*}repository")
        if repository is None or repository.get("url") != "https://github.com/zion-sati/netwasm" or repository.get("commit") != manifest.get("sourceCommit"):
            raise ValueError("Host-tools nuspec source revision mismatch.")
        if metadata_element.find("{*}dependencies") is not None:
            raise ValueError("Host-tools package must not add a dependency graph.")
    return {
        "packageId": expected_id,
        "packageVersion": expected_version,
        "hostRid": expected_rid,
        "size": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "sourceCommit": manifest["sourceCommit"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--pins", type=Path)
    parser.add_argument("--source-commit")
    parser.add_argument("--receipt", type=Path)
    args = parser.parse_args()
    result = inspect(args.package.resolve(), args.rid, args.version,
                     args.pins.resolve() if args.pins else None, args.source_commit)
    if args.receipt:
        args.receipt.parent.mkdir(parents=True, exist_ok=True)
        args.receipt.write_text(json.dumps(result, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {result['packageId']}: {result['size']:,} bytes, {result['sha256']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

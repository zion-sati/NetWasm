#!/usr/bin/env python3
"""Stage one pinned, minimal native host-tool payload for NuGet packing."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import posixpath
import re
import shutil
import stat
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile
from pathlib import Path
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parent.parent
PIN_PATH = ROOT / "eng/toolchain.json"
CHUNK = 1024 * 1024


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def acquire(source: dict[str, str], cache: Path) -> Path:
    expected = source["sha256"]
    filename = posixpath.basename(urlparse(source["url"]).path)
    if not filename or len(expected) != 64:
        raise ValueError("Host-tool source pin is incomplete.")
    cache.mkdir(parents=True, exist_ok=True)
    target = cache / f"{expected[:16]}-{filename}"
    if not target.exists():
        with tempfile.NamedTemporaryFile(dir=cache, delete=False) as temporary:
            partial = Path(temporary.name)
            try:
                with urllib.request.urlopen(
                    urllib.request.Request(
                        source["url"], headers={"User-Agent": "NetWasm-host-tools"}
                    ),
                    timeout=60,
                ) as response:
                    shutil.copyfileobj(response, temporary, CHUNK)
            except BaseException:
                partial.unlink(missing_ok=True)
                raise
        if digest(partial) != expected:
            partial.unlink()
            raise ValueError(f"Host-tool archive digest mismatch: {filename}")
        partial.replace(target)
    if digest(target) != expected:
        raise ValueError(f"Cached host-tool archive digest mismatch: {target}")
    return target


def safe_member(name: str) -> None:
    if not name or name.startswith("/") or "\\" in name:
        raise ValueError(f"Unsafe host-tool archive member: {name}")
    parts = name.rstrip("/").split("/")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"Unsafe host-tool archive member: {name}")


def read_members(archive_path: Path, names: dict[str, str]) -> dict[str, bytes]:
    """Read only exact allowlisted regular files; validate the whole archive index."""
    selected: dict[str, bytes] = {}
    if zipfile.is_zipfile(archive_path):
        with zipfile.ZipFile(archive_path) as archive:
            entries = archive.infolist()
            for entry in entries:
                safe_member(entry.filename)
                if stat.S_IFMT(entry.external_attr >> 16) == stat.S_IFLNK:
                    raise ValueError(f"Symlink in ZIP archive: {entry.filename}")
            by_name = {entry.filename: entry for entry in entries}
            if len(by_name) != len(entries):
                raise ValueError("Duplicate archive members are not permitted.")
            for role, name in names.items():
                entry = by_name.get(name)
                if entry is None or entry.is_dir():
                    raise ValueError(f"Missing host-tool archive member: {name}")
                selected[role] = archive.read(entry)
    else:
        with tarfile.open(archive_path, mode="r:*") as archive:
            entries = archive.getmembers()
            by_name = {}
            for entry in entries:
                safe_member(entry.name)
                if entry.name in by_name:
                    raise ValueError(f"Duplicate archive member: {entry.name}")
                by_name[entry.name] = entry
            for role, name in names.items():
                entry = by_name.get(name)
                if entry is None or not entry.isfile():
                    raise ValueError(f"Missing regular host-tool archive member: {name}")
                stream = archive.extractfile(entry)
                if stream is None:
                    raise ValueError(f"Cannot read host-tool archive member: {name}")
                with stream:
                    selected[role] = stream.read()
            if "wasm-ld" in names:
                link = by_name.get("install/bin/wasm-ld")
                if link is None or not link.issym() or link.linkname != "lld":
                    raise ValueError("Expected wasm-ld to be an in-archive link to lld.")
    return selected


def executable_name(role: str, rid: str) -> str:
    return role + (".exe" if rid.startswith("win-") else "")


def linux_node_closure(
    node: bytes, rid: str, host: dict[str, object], artifacts: dict[str, object], cache: Path,
) -> tuple[bytes, bytes, dict[str, str]]:
    """Bundle Node's libatomic and give Node a package-relative loader path."""
    original_sha = hashlib.sha256(node).hexdigest()
    if original_sha != host["nodeExecutableSha256"]:
        raise ValueError("Pinned Linux Node executable digest mismatch.")
    debian_archive = acquire(host["libatomic"], cache)
    architecture = "x86_64-linux-gnu" if rid == "linux-x64" else "aarch64-linux-gnu"
    with tempfile.TemporaryDirectory(prefix="netwasm-libatomic-") as temporary:
        root = Path(temporary)
        subprocess.run(["dpkg-deb", "--extract", str(debian_archive), str(root / "deb")],
                       check=True, capture_output=True, text=True)
        library_path = root / "deb/usr/lib" / architecture / "libatomic.so.1.2.0"
        if not library_path.is_file() or library_path.is_symlink():
            raise ValueError("Pinned libatomic package has no regular library payload.")
        library = library_path.read_bytes()
        if hashlib.sha256(library).hexdigest() != host["libatomicExecutableSha256"]:
            raise ValueError("Pinned libatomic executable digest mismatch.")
        expected_machine = 62 if rid == "linux-x64" else 183
        if library[:4] != b"\x7fELF" or int.from_bytes(library[18:20], "little") != expected_machine:
            raise ValueError("Pinned libatomic payload has the wrong ELF architecture.")

        patch = artifacts["linuxNodePatcher"]
        package_version = subprocess.check_output(
            ["dpkg-query", "-W", "-f=${Version}", patch["package"]], text=True
        ).strip()
        if package_version != patch["version"]:
            raise ValueError(f"Expected {patch['package']} {patch['version']}; found {package_version}.")
        node_path = root / "node"
        node_path.write_bytes(node)
        node_path.chmod(0o755)
        subprocess.run(["patchelf", "--set-rpath", patch["runpath"], str(node_path)],
                       check=True, capture_output=True, text=True)
        actual_runpath = subprocess.check_output(
            ["patchelf", "--print-rpath", str(node_path)], text=True
        ).strip()
        if actual_runpath != "$ORIGIN" or actual_runpath != patch["runpath"]:
            raise ValueError("Linux Node does not use the package-relative libatomic path.")
        patched_node = node_path.read_bytes()
        if hashlib.sha256(patched_node).hexdigest() != host["packagedNodeExecutableSha256"]:
            raise ValueError("Pinned patched Linux Node executable digest mismatch.")
    return patched_node, library, {
        "upstreamNodeSha256": original_sha,
        "packagedNodeSha256": hashlib.sha256(patched_node).hexdigest(),
        "patcher": patch["package"],
        "patcherVersion": package_version,
        "runpath": actual_runpath,
    }


def llvm_third_party_notices(sources: list[dict[str, object]], cache: Path) -> bytes:
    notices: list[str] = []
    for source in sources:
        source_text = acquire(source, cache).read_text(encoding="utf-8")
        blocks = re.findall(r"/\*.*?\*/", source_text, flags=re.DOTALL)
        index = int(source["noticeBlock"])
        if index < 0 or index >= len(blocks):
            raise ValueError(f"Missing pinned LLVM notice block: {source['path']}")
        notice = blocks[index]
        if not any(term in notice.lower() for term in
                   ("copyright", "license", "permission", "public domain")):
            raise ValueError(f"Pinned LLVM source has no notice: {source['path']}")
        notices.append(f"===== {source['path']} =====\n{notice}\n")
    return ("LLVM embedded third-party notices from the pinned source revision.\n\n" +
            "\n".join(notices)).encode("utf-8")


def archive_members(rid: str, node_root: str) -> tuple[dict[str, str], dict[str, str]]:
    suffix = ".exe" if rid.startswith("win-") else ""
    emsdk = {
        "wasm-ld": f"install/bin/wasm-ld{suffix}" if suffix else "install/bin/lld",
        "wasm-opt": f"install/bin/wasm-opt{suffix}",
        "wasm-merge": f"install/bin/wasm-merge{suffix}",
    }
    node = {
        "node": f"{node_root}/node.exe" if suffix else f"{node_root}/bin/node",
        "license": f"{node_root}/LICENSE",
    }
    return emsdk, node


def published_windows_arm64_lld(
    archive_path: Path,
    pins: dict[str, object],
) -> tuple[bytes, dict[str, object]]:
    host_artifacts = pins["hostArtifacts"]
    host = host_artifacts["hosts"]["win-arm64"]
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist()
        if [entry.filename for entry in entries] != [
            "wasm-ld.exe", "build-receipt.json", "LLVM-LICENSE.txt",
            "LLD-LICENSE.txt", "LLVM-BLAKE3-LICENSE", "LLVM-ThirdParty-NOTICES.txt",
        ] or archive.testzip() is not None:
            raise ValueError("Published Windows ARM64 wasm-ld archive inventory is invalid.")
        binary = archive.read("wasm-ld.exe")
        receipt = json.loads(archive.read("build-receipt.json"))
        notice_files = {
            "LLVM-LICENSE.txt": "llvm",
            "LLD-LICENSE.txt": "lld",
            "LLVM-BLAKE3-LICENSE": "blake3",
            "LLVM-ThirdParty-NOTICES.txt": "llvmThirdPartyNoticeSha256",
        }
        for name, role in notice_files.items():
            pin = host_artifacts["licenseSources"][role]
            expected = pin if isinstance(pin, str) else pin["sha256"]
            if hashlib.sha256(archive.read(name)).hexdigest() != expected:
                raise ValueError(f"Published Windows ARM64 wasm-ld notice digest mismatch: {name}.")
    if binary[:2] != b"MZ" or len(binary) < 0x40:
        raise ValueError("Published wasm-ld is not a PE executable.")
    pe_offset = int.from_bytes(binary[0x3c:0x40], "little")
    if pe_offset + 6 > len(binary) or binary[pe_offset:pe_offset + 4] != b"PE\0\0" or \
            int.from_bytes(binary[pe_offset + 4:pe_offset + 6], "little") != 0xAA64:
        raise ValueError("Published wasm-ld is not native Windows ARM64.")
    required = {
        "schemaVersion", "hostRid", "sourceCommit", "llvmCommit",
        "sourceArchiveSha256", "sourceTreeSha256", "outputSha256", "outputSize", "cmakeFlags",
        "compilerVersion", "windowsSdkVersion", "cmakeVersion", "ninjaVersion",
    }
    if set(receipt) != required or receipt["schemaVersion"] != 1 or \
            receipt["hostRid"] != "win-arm64" or \
            receipt["sourceCommit"] != host["lldRelease"]["builderCommit"] or \
            receipt["llvmCommit"] != pins["llvmLld"]["commit"] or \
            receipt["sourceTreeSha256"] != host_artifacts["hosts"]["win-arm64"]["llvmSource"]["treeSha256"] or \
            not isinstance(receipt["sourceArchiveSha256"], str) or \
            not re.fullmatch(r"[0-9a-f]{64}", receipt["sourceArchiveSha256"]) or \
            receipt["cmakeFlags"] != host_artifacts["winArm64LldCMakeFlags"] or \
            receipt["outputSha256"] != hashlib.sha256(binary).hexdigest() or \
            receipt["outputSize"] != len(binary):
        raise ValueError("Published Windows ARM64 wasm-ld receipt does not match its pinned source and output.")
    if any(not isinstance(receipt[field], str) or not receipt[field] or
           "\\" in receipt[field] or "/" in receipt[field]
           for field in ("compilerVersion", "windowsSdkVersion", "cmakeVersion", "ninjaVersion")):
        raise ValueError("Published Windows ARM64 wasm-ld receipt contains an invalid tool version.")
    return binary, receipt


def native_host_rid() -> str:
    machine = platform.machine().lower()
    if sys_platform := platform.system().lower():
        if sys_platform == "darwin" and machine in ("arm64", "aarch64"):
            return "osx-arm64"
        if sys_platform == "linux" and machine in ("x86_64", "amd64"):
            return "linux-x64"
        if sys_platform == "linux" and machine in ("arm64", "aarch64"):
            return "linux-arm64"
        if sys_platform == "windows" and machine in ("amd64", "x86_64"):
            return "win-x64"
        if sys_platform == "windows" and (
            machine in ("arm64", "aarch64") or
            os.environ.get("PROCESSOR_ARCHITEW6432", "").lower() == "arm64"
        ):
            return "win-arm64"
    raise ValueError(f"Unsupported staging host: {platform.system()} {platform.machine()}")


def probe(stage: Path, rid: str, pins: dict[str, object]) -> None:
    if native_host_rid() != rid:
        raise ValueError(f"Cannot execute {rid} tools on {native_host_rid()}.")
    expected = {
        "node": str(pins["node"]),
        "wasm-ld": str(pins["llvmLld"]["version"]),
        "wasm-opt": str(pins["binaryen"]),
        "wasm-merge": str(pins["binaryen"]),
    }
    for role, version in expected.items():
        path = stage / "tools/bin" / executable_name(role, rid)
        result = subprocess.run(
            [str(path), "--version"],
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
        observation = result.stdout + result.stderr
        if version not in observation:
            raise ValueError(f"{role} version mismatch: {observation.strip()}")


def stage(
    rid: str, version: str, output: Path, cache: Path, probe_native: bool,
) -> dict[str, object]:
    pins = json.loads(PIN_PATH.read_text(encoding="utf-8"))
    artifacts = pins["hostArtifacts"]
    if artifacts["schemaVersion"] != 1 or rid not in artifacts["hosts"]:
        raise ValueError(f"Unsupported host RID: {rid}")
    if output.exists():
        raise ValueError(f"Staging output already exists: {output}")
    host = artifacts["hosts"][rid]
    source_commit = subprocess.check_output(
        ["git", "-C", str(ROOT), "rev-parse", "HEAD"], text=True
    ).strip()
    lld_receipt = None
    node_patch = None
    if rid == "win-arm64":
        lld_archive = acquire(host["lldRelease"], cache)
        lld_binary, lld_receipt = published_windows_arm64_lld(lld_archive, pins)
        binaryen_archive = acquire(host["binaryen"], cache)
        tool_files = read_members(binaryen_archive, {
            role: f"binaryen-version_{pins['binaryen']}/bin/{role}.exe"
            for role in ("wasm-opt", "wasm-merge")
        })
        tool_files["wasm-ld"] = lld_binary
        archive_roles = ("llvmSource", "lldRelease", "binaryen", "node")
    else:
        emsdk_archive = acquire(host["emsdk"], cache)
        emsdk_names, _ = archive_members(rid, host["nodeRoot"])
        tool_files = read_members(emsdk_archive, emsdk_names)
        archive_roles = ("emsdk", "node", "libatomic") if rid.startswith("linux-") else ("emsdk", "node")
    node_archive = acquire(host["node"], cache)
    llvm_license = acquire(artifacts["licenseSources"]["llvm"], cache).read_bytes()
    lld_license = acquire(artifacts["licenseSources"]["lld"], cache).read_bytes()
    blake3_license = acquire(artifacts["licenseSources"]["blake3"], cache).read_bytes()
    llvm_notices = llvm_third_party_notices(
        artifacts["licenseSources"]["llvmThirdParty"], cache)
    if hashlib.sha256(llvm_notices).hexdigest() != artifacts["licenseSources"]["llvmThirdPartyNoticeSha256"]:
        raise ValueError("Pinned LLVM third-party notice digest mismatch.")
    binaryen_license = acquire(artifacts["licenseSources"]["binaryen"], cache).read_bytes()
    _, node_names = archive_members(rid, host["nodeRoot"])
    node_files = read_members(node_archive, node_names)
    libatomic = None
    gcc_copyright = None
    gpl3 = None
    gcc_source_notice = None
    if rid.startswith("linux-"):
        patched_node, libatomic, node_patch = linux_node_closure(
            node_files["node"], rid, host, artifacts, cache)
        node_files["node"] = patched_node
        gcc_copyright = acquire(artifacts["licenseSources"]["gccLibatomicCopyright"], cache).read_bytes()
        gpl3 = acquire(artifacts["licenseSources"]["gpl3"], cache).read_bytes()
        source = artifacts["gccLibatomicSource"]
        descriptor = acquire({"url": source["descriptorUrl"], "sha256": source["descriptorSha256"]}, cache)
        descriptor_text = descriptor.read_text(encoding="utf-8")
        source_hashes = {}
        for line in descriptor_text.splitlines():
            fields = line.split()
            if len(fields) == 3 and re.fullmatch(r"[0-9a-f]{64}", fields[0]) and fields[1].isdigit():
                source_hashes[fields[2]] = fields[0]
        for digest, filename in (
            (source["sourceArchiveSha256"], "gcc-14_14.2.0.orig.tar.gz"),
            (source["debianPatchSha256"], "gcc-14_14.2.0-4ubuntu2~24.04.1.debian.tar.xz"),
        ):
            if source_hashes.get(filename) != digest:
                raise ValueError("Pinned GCC source descriptor does not list the source inputs.")
        gcc_source_notice = (
            "Corresponding Ubuntu GCC 14 source for bundled libatomic.so.1\n"
            f"Version: {source['version']}\n"
            f"Source descriptor: {source['descriptorUrl']}\n"
            f"SHA-256: {source['descriptorSha256']}\n"
            f"Original source: {source['sourceArchiveUrl']}\n"
            f"SHA-256: {source['sourceArchiveSha256']}\n"
            f"Ubuntu patch: {source['debianPatchUrl']}\n"
            f"SHA-256: {source['debianPatchSha256']}\n"
        ).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix="host-tools-stage-", dir=output.parent))
    try:
        package_files = {
            f"tools/bin/{executable_name(role, rid)}": data
            for role, data in (tool_files | {"node": node_files["node"]}).items()
        }
        package_files |= {
            "licenses/Node-LICENSE": node_files["license"],
            "licenses/LLVM-LICENSE.txt": llvm_license,
            "licenses/LLD-LICENSE.txt": lld_license,
            "licenses/LLVM-BLAKE3-LICENSE": blake3_license,
            "licenses/LLVM-ThirdParty-NOTICES.txt": llvm_notices,
            "licenses/Binaryen-LICENSE": binaryen_license,
        }
        if libatomic is not None:
            package_files |= {
                "tools/bin/libatomic.so.1": libatomic,
                "licenses/GCC-Libatomic-COPYRIGHT": gcc_copyright,
                "licenses/GPL-3.0.txt": gpl3,
                "licenses/GCC-Libatomic-SOURCE.txt": gcc_source_notice,
            }
        license_text = b"NetWasm host tools contain upstream software under these notices.\n\n"
        for label, data in (
            ("Node.js", node_files["license"]),
            ("LLVM/LLD", llvm_license),
            ("LLD", lld_license),
            ("LLVM BLAKE3", blake3_license),
            ("LLVM embedded third parties", llvm_notices),
            ("Binaryen", binaryen_license),
        ):
            license_text += f"===== {label} =====\n".encode() + data + b"\n\n"
        if libatomic is not None:
            license_text += (b"===== GCC libatomic copyright and runtime exception =====\n" +
                             gcc_copyright + b"\n\n===== GPL-3.0 =====\n" + gpl3 +
                             b"\n\n===== GCC libatomic corresponding source =====\n" +
                             gcc_source_notice + b"\n")
        package_files["LICENSE.txt"] = license_text
        package_files["README.md"] = (
            f"# NetWasm host tools for {rid}\n\n"
            "Private SDK build tools: Node, wasm-ld, wasm-opt and wasm-merge. "
            "This package does not contribute application runtime assets.\n" +
            ("The Linux Node executable loads bundled libatomic.so.1 from its own directory. "
             "See licenses/GCC-Libatomic-SOURCE.txt for corresponding source.\n"
             if libatomic is not None else "")
        ).encode()
        manifest_files = []
        for relative, data in sorted(package_files.items()):
            destination = temporary / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
            if relative.startswith("tools/bin/") and not rid.startswith("win-"):
                destination.chmod(0o755)
            manifest_files.append({
                "path": relative,
                "size": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
            })
        manifest = {
            "schemaVersion": 1,
            "packageId": f"NetWasm.HostTools.{rid}",
            "packageVersion": version,
            "hostRid": rid,
            "sourceCommit": source_commit,
            "upstream": {
                "emscripten": pins["emscripten"],
                "emsdkReleaseCommit": artifacts["emsdkReleaseCommit"],
                "lldOrigin": "pinned-native-tools-release" if rid == "win-arm64" else "emsdk-release",
                "llvmLld": pins["llvmLld"],
                "binaryen": pins["binaryen"],
                "node": pins["node"],
                "archives": [
                    {"role": role, **host[role]}
                    for role in archive_roles
                ],
            },
            "roles": {
                role: f"tools/bin/{executable_name(role, rid)}"
                for role in ("node", "wasm-ld", "wasm-opt", "wasm-merge")
            },
            "files": manifest_files,
        }
        if lld_receipt is not None:
            manifest["upstream"]["lldBuild"] = lld_receipt
        if node_patch is not None:
            manifest["upstream"]["linuxNodePatch"] = node_patch
            manifest["upstream"]["gccLibatomicSource"] = artifacts["gccLibatomicSource"]
        manifest_path = temporary / "tools/host-tools-manifest.json"
        manifest_path.write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n")
        if probe_native:
            probe(temporary, rid, pins)
        temporary.rename(output)
        return manifest
    except BaseException:
        shutil.rmtree(temporary)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--probe", action="store_true")
    args = parser.parse_args()
    manifest = stage(args.rid, args.version, args.output.resolve(), args.cache.resolve(),
                     args.probe)
    print(f"Staged {manifest['packageId']} {manifest['packageVersion']} "
          f"({sum(item['size'] for item in manifest['files']):,} bytes).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Verify that a Runtime.Pack package owns its exact, path-clean link closure."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path
from xml.etree import ElementTree


def inspect(package: Path, pins_root: Path, version: str) -> None:
    pins = json.loads((pins_root / "eng/toolchain.json").read_text(encoding="utf-8"))
    policy = json.loads((pins_root / "src/NetWasm.Runtime.Pack/runtime/runtime-policy.json").read_text(
        encoding="utf-8"))
    expected_name = f"NetWasm.Runtime.Pack.{version}.nupkg"
    if package.name != expected_name:
        raise ValueError("Runtime-pack package identity mismatch.")

    with zipfile.ZipFile(package) as archive:
        if archive.testzip() is not None:
            raise ValueError("Runtime-pack archive is corrupt.")
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Runtime-pack archive has duplicate entries.")
        manifest = json.loads(archive.read("runtime/runtime-pack.json"))
        if manifest.get("schemaVersion") != 3 or manifest.get("emscriptenVersion") != pins["emscripten"]:
            raise ValueError("Runtime-pack manifest uses an unexpected source version.")
        targets = manifest.get("targets", [])
        if [target.get("target") for target in targets] != ["wasm32", "wasm64"]:
            raise ValueError("Runtime-pack target inventory is incomplete.")

        expected_archives: set[str] = set()
        forbidden = (
            b"/" + b"Users" + b"/",
            b"/" + b"home" + b"/",
            b"/" + b"var" + b"/" + b"folders" + b"/",
            b"/" + b"tmp" + b"/",
        )
        for target in targets:
            name = target["target"]
            required = policy["targets"][name]["systemLibraries"]
            closure = target["systemLibraries"]
            if closure.get("names") != required or len(closure.get("assets", [])) != len(required):
                raise ValueError(f"Runtime-pack {name} system-library inventory is incomplete.")
            assets = [target["runtimeArchive"], *closure["assets"]]
            expected_paths = [f"{name}/libnetwasm-runtime.a", *(
                f"{name}/system-libraries/{library}" for library in required)]
            if [asset.get("path") for asset in assets] != expected_paths:
                raise ValueError(f"Runtime-pack {name} archive paths are unexpected.")
            for asset in assets:
                entry = "runtime/" + asset["path"]
                data = archive.read(entry)
                if hashlib.sha256(data).hexdigest() != asset.get("sha256"):
                    raise ValueError(f"Runtime-pack archive digest mismatch: {entry}")
                path_clean = data.replace(b"/tmp/tmpnam_XXXXXX\x00", b"")
                path_clean = path_clean.replace(b"/tmp/tmpfile_XXXXXX\x00", b"")
                if any(pattern in path_clean for pattern in forbidden):
                    raise ValueError(f"Runtime-pack archive contains a machine path: {entry}")
                expected_archives.add(entry)
        if {name for name in names if name.endswith(".a")} != expected_archives:
            raise ValueError("Runtime-pack package has an extra or missing static archive.")
        license_text = archive.read("LICENSE.txt").decode("utf-8")
        if any(title not in license_text for title in (
            "=== NetWasm code: MIT ===", "=== Emscripten notice ===",
            "=== musl libc notice ===", "=== LLVM compiler-rt notice ===",
        )):
            raise ValueError("Runtime-pack package is missing an upstream notice.")
        nuspec_name = next(name for name in names if name.endswith(".nuspec"))
        nuspec = ElementTree.fromstring(archive.read(nuspec_name))
        license_element = nuspec.find(".//{*}license")
        if license_element is None or license_element.get("type") != "file" or license_element.text != "LICENSE.txt":
            raise ValueError("Runtime-pack NuGet license metadata does not name its notice file.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--pins-root", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    inspect(args.package, args.pins_root, args.version)
    print(f"Verified {args.package.name} runtime closure")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3

"""Regenerate the deterministic Hosting Preview 2 provider catalog."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


WASI_PACKAGES = ("cli", "clocks", "filesystem", "http", "io", "random", "sockets")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def resolve_node(explicit: str | None) -> str:
    candidate = explicit or os.environ.get("NETWASM_NODE_PATH") \
        or os.environ.get("EMSDK_NODE") or shutil.which("node")
    if not candidate:
        raise RuntimeError(
            "Node was not found; activate Emscripten or pass --node explicitly")
    return str(Path(candidate).resolve())


def run(arguments: list[str], *, capture: bool = False) -> bytes:
    result = subprocess.run(
        arguments,
        check=False,
        stdout=subprocess.PIPE if capture else None,
        stderr=None,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"command failed with exit code {result.returncode}: {arguments[0]}")
    return result.stdout if capture else b""


def select_asset(manifest: dict[str, object], asset_id: str) -> dict[str, object]:
    assets = manifest.get("assets")
    if not isinstance(assets, list):
        raise RuntimeError("the Toolchain manifest has no asset inventory")
    matches = [asset for asset in assets
               if isinstance(asset, dict) and asset.get("id") == asset_id]
    if len(matches) != 1:
        raise RuntimeError(f"the Toolchain manifest does not select one '{asset_id}' asset")
    return matches[0]


def stage_wit(repository: Path, destination: Path) -> None:
    source = repository / "wit/wasi-0.2.11"
    dependencies = destination / "deps"
    dependencies.mkdir()
    for package in WASI_PACKAGES:
        shutil.copytree(source / package, dependencies / f"wasi-{package}")
    shutil.copy2(
        repository / "wit/netwasm-hosting-platform-1.0.0/world.wit",
        destination / "world.wit")


def normalize_wit(
    node: str,
    runner: Path,
    module: Path,
    source: Path,
) -> bytes:
    return run([
        node,
        str(runner),
        str(module),
        "component",
        "wit",
        str(source),
        "--json",
        "--no-docs",
    ], capture=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--node")
    parser.add_argument("--wasm-tools-module")
    parser.add_argument("--preview2-shim-package-json")
    arguments = parser.parse_args()

    repository = Path(__file__).resolve().parent.parent
    toolchain = repository / "src/NetWasm.Toolchain"
    manifest = json.loads((toolchain / "toolchain-manifest.json").read_text())
    wasm_tools_asset = select_asset(manifest, "wasm-tools.module")
    shim_asset = select_asset(manifest, "preview2-shim.package")
    module = Path(
        arguments.wasm_tools_module
        or os.environ.get("NETWASM_WASM_TOOLS_MODULE_PATH", "")
        or toolchain / "platform-assets/wasm-tools/wasm-tools.wasm"
    ).resolve()
    shim_package_json = Path(
        arguments.preview2_shim_package_json
        or repository / "node_modules/@bytecodealliance/preview2-shim/package.json"
    ).resolve()
    if sha256(module) != wasm_tools_asset.get("sha256"):
        raise RuntimeError("the wasm-tools module does not match the pinned Toolchain asset")
    if sha256(shim_package_json) != shim_asset.get("sha256"):
        raise RuntimeError("the Preview 2 shim package does not match the pinned Toolchain asset")

    node = resolve_node(arguments.node)
    runner = toolchain / "JavaScript/run-wasm-tools.mjs"
    generator = repository / "tools/NetWasm.Hosting.Generator/NetWasm.Hosting.Generator.csproj"
    output = repository / "src/NetWasm.Hosting/JavaScript/platform-provider-catalog.generated.mjs"
    with tempfile.TemporaryDirectory(prefix="netwasm-hosting-catalog-") as temporary_root:
        temporary = Path(temporary_root)
        first_source = temporary / "first-source"
        second_source = temporary / "second-source"
        first_source.mkdir()
        second_source.mkdir()
        stage_wit(repository, first_source)
        stage_wit(repository, second_source)
        first_json = normalize_wit(node, runner, module, first_source)
        second_json = normalize_wit(node, runner, module, second_source)
        if first_json != second_json:
            raise RuntimeError("normalized Hosting platform WIT is not deterministic")

        normalized = temporary / "normalized.json"
        normalized.write_bytes(first_json)
        first_output = temporary / "first.mjs"
        second_output = temporary / "second.mjs"
        for candidate in (first_output, second_output):
            run([
                "dotnet",
                "run",
                "--project",
                str(generator),
                "--",
                str(normalized),
                str(shim_package_json),
                str(candidate),
            ])
        if first_output.read_bytes() != second_output.read_bytes():
            raise RuntimeError("Hosting platform catalog generation is not deterministic")
        shutil.copy2(first_output, output)


if __name__ == "__main__":
    main()

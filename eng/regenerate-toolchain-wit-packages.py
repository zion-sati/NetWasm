#!/usr/bin/env python3

"""Regenerate deterministic binary WIT products consumed by the NetWasm SDK."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def resolve_node(explicit: str | None) -> str:
    candidate = explicit or os.environ.get("NETWASM_NODE_PATH") \
        or os.environ.get("EMSDK_NODE") or shutil.which("node")
    if not candidate:
        raise RuntimeError(
            "Node was not found; activate Emscripten or pass --node explicitly")
    return str(Path(candidate).resolve())


def run_wasm_tools(
    node: str,
    runner: Path,
    module: Path,
    *arguments: str,
) -> None:
    result = subprocess.run(
        [node, str(runner), str(module), *arguments],
        check=False,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"wasm-tools failed with exit code {result.returncode}")


def stage_diagnostic_source(repository: Path, destination: Path) -> None:
    source = repository / "src/NetWasm.Toolchain/diagnostic-wit"
    shutil.copytree(source, destination)
    command = repository / "src/NetWasm.Toolchain/wit"
    shutil.copytree(command / "deps", destination / "deps", dirs_exist_ok=True)


def generate_twice(
    node: str,
    runner: Path,
    module: Path,
    source: Path,
    destination: Path,
) -> None:
    first = destination.with_suffix(destination.suffix + ".first")
    second = destination.with_suffix(destination.suffix + ".second")
    for output in (first, second):
        run_wasm_tools(
            node,
            runner,
            module,
            "component",
            "wit",
            str(source),
            "--wasm",
            "--output",
            str(output),
        )
        run_wasm_tools(
            node,
            runner,
            module,
            "component",
            "wit",
            str(output),
            "--json",
            "--no-docs",
        )
    if first.read_bytes() != second.read_bytes():
        raise RuntimeError(f"binary WIT generation is not deterministic for {source}")
    first.replace(destination)
    second.unlink()


def update_asset_hashes(
    manifest: dict[str, object],
    artifacts: dict[str, Path],
) -> None:
    assets = manifest.get("assets")
    if not isinstance(assets, list):
        raise RuntimeError("the Toolchain manifest has no asset inventory")
    indexed = {
        asset.get("id"): asset
        for asset in assets
        if isinstance(asset, dict)
    }
    for asset_id, artifact in artifacts.items():
        asset = indexed.get(asset_id)
        if not isinstance(asset, dict):
            raise RuntimeError(
                f"the Toolchain manifest has no '{asset_id}' asset")
        asset["sha256"] = sha256(artifact)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--node")
    parser.add_argument("--wasm-tools-module")
    arguments = parser.parse_args()

    repository = Path(__file__).resolve().parent.parent
    toolchain = repository / "src/NetWasm.Toolchain"
    runner = toolchain / "JavaScript/run-wasm-tools.mjs"
    module = Path(
        arguments.wasm_tools_module
        or os.environ.get("NETWASM_WASM_TOOLS_MODULE_PATH", "")
        or toolchain / "platform-assets/wasm-tools/wasm-tools.wasm"
    ).resolve()
    manifest = json.loads((toolchain / "toolchain-manifest.json").read_text())
    module_descriptor = next(
        asset for asset in manifest["assets"] if asset["id"] == "wasm-tools.module")
    if sha256(module) != module_descriptor["sha256"]:
        raise RuntimeError("the staged wasm-tools module does not match the pinned manifest")

    node = resolve_node(arguments.node)
    output = toolchain / "wit-packages"
    output.mkdir(exist_ok=True)
    source_manifests = {
        "command": toolchain / "wit/wit-manifest.json",
        "diagnostic-command": toolchain / "diagnostic-wit/wit-manifest.json",
        "compiler": repository / "wit/netwasm-platform-1.0.0/wit-manifest.json",
    }
    packaged_source_manifests = {
        "command": "../wit/wit-manifest.json",
        "diagnostic-command": "../diagnostic-wit/wit-manifest.json",
        "compiler": "../compiler-wit/wit-manifest.json",
    }
    products = (
        ("command", "netwasm:command@1.0.0", "command", toolchain / "wit"),
        (
            "diagnostic-command",
            "netwasm:component@1.0.0",
            "diagnostic-command",
            None,
        ),
        (
            "compiler",
            "netwasm:platform@1.0.0",
            "platform",
            repository / "wit/netwasm-platform-1.0.0",
        ),
    )

    with tempfile.TemporaryDirectory(prefix="netwasm-wit-packages-") as raw_temp:
        temporary = Path(raw_temp)
        diagnostic_source = temporary / "diagnostic-source"
        stage_diagnostic_source(repository, diagnostic_source)
        descriptors = []
        for name, package, world, source in products:
            generated = temporary / f"{name}.wit.wasm"
            generate_twice(
                node,
                runner,
                module,
                diagnostic_source if source is None else source,
                generated,
            )
            descriptors.append(
                {
                    "name": name,
                    "package": package,
                    "world": world,
                    "sourceManifest": packaged_source_manifests[name],
                    "sourceManifestSha256": sha256(source_manifests[name]),
                    "file": generated.name,
                    "sha256": sha256(generated),
                }
            )

        document = {
            "schemaVersion": "1",
            "generator": {
                "assetId": "wasm-tools.module",
                "version": module_descriptor["version"],
                "sha256": module_descriptor["sha256"],
            },
            "products": descriptors,
        }
        manifest_output = temporary / "wit-package-manifest.json"
        manifest_output.write_text(
            json.dumps(document, indent=2) + "\n", encoding="utf-8")
        update_asset_hashes(
            manifest,
            {
                "wit.package-manifest": manifest_output,
                **{
                    f"wit.{descriptor['name']}-package":
                        temporary / descriptor["file"]
                    for descriptor in descriptors
                },
            },
        )
        toolchain_manifest_output = temporary / "toolchain-manifest.json"
        toolchain_manifest_output.write_text(
            json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        for descriptor in descriptors:
            (temporary / descriptor["file"]).replace(output / descriptor["file"])
        manifest_output.replace(output / manifest_output.name)
        toolchain_manifest_output.replace(toolchain / "toolchain-manifest.json")


if __name__ == "__main__":
    main()

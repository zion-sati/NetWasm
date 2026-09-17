#!/usr/bin/env python3
"""Update the current Emscripten pin and its user-facing mirrors."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MIRRORS = (
    "README.md",
    "QUICKSTART.md",
    "docs/index.md",
    "docs/sdk-quickstart.md",
    "docs/size-and-methodology.md",
    "docs/support-status.md",
    "docs/cli-build-deployment.md",
    "src/NetWasm.Sdk/README.md",
    "src/NetWasm.Templates/README.md",
    "src/NetWasm.Toolchain/README.md",
    "compiler-qualification/vstest/docker/Dockerfile",
    "scripts/oss-export/templates/netwasm/README.md",
    "scripts/oss-export/templates/netwasm/QUICKSTART.md",
    "scripts/oss-export/templates/netwasm/docs/index.md",
    "scripts/oss-export/templates/netwasm/docs/sdk-quickstart.md",
    "scripts/oss-export/templates/netwasm/docs/size-and-methodology.md",
    "scripts/oss-export/templates/netwasm/docs/support-status.md",
    "scripts/oss-export/templates/netwasm/docs/cli-build-deployment.md",
    "scripts/oss-export/verify-oss-export.sh",
)


def main() -> int:
    if len(sys.argv) != 2 or not re.fullmatch(r"\d+\.\d+\.\d+", sys.argv[1]):
        print("usage: eng/update-emscripten-version.py <major.minor.patch>", file=sys.stderr)
        return 2

    new_version = sys.argv[1]
    manifest_path = ROOT / "eng/toolchain.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    old_version = manifest["emscripten"]
    if new_version == old_version:
        print(f"Emscripten is already pinned to {new_version}")
        return 0

    manifest["emscripten"] = new_version
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for relative_path in MIRRORS:
        path = ROOT / relative_path
        text = path.read_text(encoding="utf-8")
        updated = text.replace(old_version, new_version)
        if updated == text:
            raise RuntimeError(f"expected Emscripten {old_version} mirror is missing from {relative_path}")
        path.write_text(updated, encoding="utf-8")

    subprocess.run(
        [
            "node",
            str(ROOT / "src/NetWasm.Runtime.Pack/tools/write-runtime-pack-manifest.mjs"),
            str(ROOT / "src/NetWasm.Runtime.Pack"),
            str(manifest_path),
        ],
        check=True,
        cwd=ROOT,
    )
    print(f"Updated Emscripten {old_version} -> {new_version}")
    print("Regenerate and qualify the runtime pack with the newly activated SDK before committing.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

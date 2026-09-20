#!/usr/bin/env python3
"""Stage the exact notices for Emscripten archives shipped in Runtime.Pack."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


SOURCES = (
    ("emscripten", "Emscripten", "upstream/emscripten/LICENSE"),
    ("musl", "musl libc", "upstream/emscripten/system/lib/libc/musl/COPYRIGHT"),
    ("compilerRt", "LLVM compiler-rt", "upstream/emscripten/system/lib/compiler-rt/LICENSE.TXT"),
)

NETWASM_MIT = """MIT License

Copyright (c) 2026 Zion Sati

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""


def stage(emsdk_root: Path, toolchain: Path, output: Path) -> None:
    pins = json.loads(toolchain.read_text(encoding="utf-8"))
    version = (emsdk_root / "upstream/emscripten/emscripten-version.txt").read_text(
        encoding="utf-8").strip().strip('"')
    if version != pins["emscripten"]:
        raise ValueError("Runtime pack license sources require the pinned Emscripten SDK.")
    sections = [
        "NetWasm.Runtime.Pack includes NetWasm code and selected system archives",
        f"built from Emscripten {version}. Each component retains its own terms.",
        "\n=== NetWasm code: MIT ===\n",
        NETWASM_MIT,
    ]
    for key, title, relative in SOURCES:
        data = (emsdk_root / relative).read_bytes()
        if hashlib.sha256(data).hexdigest() != pins["runtimePackLicenseSources"][key]:
            raise ValueError(f"The pinned {title} notice changed.")
        sections.extend((f"\n=== {title} notice ===\n", data.decode("utf-8")))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(sections), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--emsdk-root", type=Path, required=True)
    parser.add_argument("--toolchain", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    stage(args.emsdk_root, args.toolchain, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

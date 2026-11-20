#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
policy="$repo_root/src/NetWasm.Runtime.Pack/runtime/runtime-policy.json"

for tool in node; do
  command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
[[ -n "${EMSDK:-}" ]] || { echo "activate the pinned Emscripten SDK first" >&2; exit 1; }
embuilder="$EMSDK/upstream/emscripten/embuilder"
[[ -x "$embuilder" ]] || { echo "missing required Emscripten system-library builder" >&2; exit 1; }

for target in wasm32 wasm64; do
  libraries=()
  while IFS= read -r library; do
    [[ "$library" == *.a ]] || {
      echo "Emscripten system library '$library' must be a static archive" >&2
      exit 1
    }
    libraries+=("${library%.a}")
  done < <(node -p \
    'require(process.argv[1]).targets[process.argv[2]].systemLibraries.join("\n")' \
    "$policy" "$target")

  arguments=(--lto)
  if [[ "$target" = wasm64 ]]; then
    arguments=(--wasm64 --lto)
  fi
  "$embuilder" "${arguments[@]}" build "${libraries[@]}"

  # The libraries a program with C++ exceptions links instead (libc++, libc++abi and libunwind with Wasm exception
  # handling, and the standalone runtime without its aborting __cxa_throw), as object files: their exception
  # handling then needs no LTO code generation flags in the final link.
  exception_libraries=()
  while IFS= read -r library; do
    [[ -n "$library" ]] && exception_libraries+=("${library%.a}")
  done < <(node -p \
    'const t = require(process.argv[1]).targets[process.argv[2]];
     (t.exceptionSystemLibraries ?? []).filter(name => !t.systemLibraries.includes(name)).join("\n")' \
    "$policy" "$target")
  if (( ${#exception_libraries[@]} > 0 )); then
    exception_arguments=()
    if [[ "$target" = wasm64 ]]; then exception_arguments=(--wasm64); fi
    "$embuilder" "${exception_arguments[@]}" build "${exception_libraries[@]}"
  fi
done

#!/usr/bin/env bash
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

  arguments=()
  if [[ "$target" = wasm64 ]]; then
    arguments+=(--wasm64 --lto)
  fi
  "$embuilder" "${arguments[@]}" build "${libraries[@]}"
done

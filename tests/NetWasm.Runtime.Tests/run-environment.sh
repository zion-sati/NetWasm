#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
workspace_dir="$(cd "$script_dir/../.." && pwd)"
output_dir="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-runtime-environment.XXXXXX")"
trap 'rm -rf "$output_dir"' EXIT

for target in wasm32 wasm64; do
  layout="$output_dir/$target.layout.json"
  node "$workspace_dir/src/NetWasm.Runtime.Pack/tools/write-complete-runtime-layout.mjs" "$layout" "$target" 65536
  for configuration in debug release; do
    module="$output_dir/$target-$configuration.wasm"
    bash "$workspace_dir/eng/build-netwasm-runtime.sh" \
      --target "$target" --configuration "$configuration" \
      --runtime-layout "$layout" --output "$module"
    node "$script_dir/runtime_environment_contract.mjs" "$module" "$target" normal
    node "$script_dir/runtime_metadata_contract.mjs" "$module" "$target"
    node "$script_dir/command_diagnostics_gc_contract.mjs" "$module" "$target"
  done
done

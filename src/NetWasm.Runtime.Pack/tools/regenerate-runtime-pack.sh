#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
package_root="$repo_root/src/NetWasm.Runtime.Pack"
runtime_root="$package_root/runtime"
policy="$runtime_root/runtime-policy.json"
if [[ "${1:-}" != "--allow-emscripten" ]]; then
  echo "runtime pack regeneration is maintainer-only; pass --allow-emscripten explicitly" >&2
  exit 2
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-runtime-pack.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT

emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK_ROOT:-}}"
if [[ -n "$emsdk_root" ]]; then
  [[ -f "$emsdk_root/emsdk_env.sh" ]] || {
    echo "missing Emscripten SDK environment '$emsdk_root/emsdk_env.sh'" >&2
    exit 1
  }
  export EMSDK_QUIET=1
  source "$emsdk_root/emsdk_env.sh"
fi
for tool in emcc emar node rg wasm-tools; do
  command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
wasm_ld="$EMSDK/upstream/bin/wasm-ld"
[[ -x "$wasm_ld" ]] || { echo "missing required Emscripten linker" >&2; exit 1; }

alignment="$(node -p 'require(process.argv[1]).alignment' "$policy")"
wasm_page_size="$(node -p 'require(process.argv[1]).wasmPageSize' "$policy")"
native_stack_size="$(node -p 'require(process.argv[1]).nativeStackSizeBytes' "$policy")"
toolchain="$repo_root/eng/toolchain.json"
emscripten_version="$(node -p 'require(process.argv[1]).emscripten' "$toolchain")"
gc_version="$(node -p 'require(process.argv[1]).bdwgc.version' "$toolchain")"
dependency_root="${TMPDIR:-/tmp}/netwasm-dependencies"
gc_source="$dependency_root/bdwgc-v$gc_version"

wasm-tools component embed "$repo_root/src/NetWasm.Runtime/wit" \
  --only-custom --encoding utf8 --output "$temporary_root/runtime-component-type.bin"

rg -o 'export_name\("[^"]+"\)' \
  "$repo_root"/src/NetWasm.Runtime/*.c \
  "$repo_root"/src/NetWasm.Runtime/collector/*.c \
  | sed 's/.*export_name("//;s/")$//' \
  | sort -u > "$runtime_root/runtime-exports.txt"

for target in wasm32 wasm64; do
  target_root="$runtime_root/$target"
  system_root="$target_root/system"
  rm -rf "$target_root"
  mkdir -p "$system_root"

  layout="$temporary_root/$target.layout.json"
  node - "$layout" "$target" <<'NODE'
const fs = require("node:fs");
const [layout, target] = process.argv.slice(2);
fs.writeFileSync(layout, `${JSON.stringify({ schemaVersion: 2, target, applicationStaticDataEnd: 0 }, null, 2)}\n`);
NODE

  "$repo_root/eng/build-netwasm-runtime.sh" \
    --runtime-layout "$layout" \
    --target "$target" \
    --configuration release \
    --relocatable \
    --output "$target_root/libnetwasm-runtime.a"

  gc_work="$gc_source"
  if [[ "$target" = wasm64 ]]; then
    patch_hash="$(shasum -a 256 "$repo_root/tests/end-to-end/host-interop/runtime-memory64/bdwgc-wasm64.patch" | cut -d' ' -f1)"
    gc_work="$dependency_root/bdwgc-v$gc_version-wasm64-$patch_hash"
  fi
  build_root="$dependency_root/build-bdwgc-$gc_version-$emscripten_version-$target"

  if [[ "$target" = wasm32 ]]; then
    machine=-mwasm32
    system_source="$EMSDK/upstream/emscripten/cache/sysroot/lib/wasm32-emscripten"
  else
    machine=-mwasm64
    system_source="$EMSDK/upstream/emscripten/cache/sysroot/lib/wasm64-emscripten/lto"
  fi

  system_libraries=()
  while IFS= read -r library; do
    system_libraries+=("$library")
  done < <(node -p \
    'require(process.argv[1]).targets[process.argv[2]].systemLibraries.join("\n")' \
    "$policy" "$target")
  system_paths=()
  for library in "${system_libraries[@]}"; do
    [[ -f "$system_source/$library" ]] || {
      echo "missing pinned Emscripten system library '$library' for '$target'" >&2
      exit 1
    }
    cp "$system_source/$library" "$system_root/$library"
    system_paths+=("$system_root/$library")
  done

  normalization_arguments=(
    --prefix "$repo_root"
    --prefix "$EMSDK"
    --prefix "$dependency_root"
    --prefix "$temporary_root"
    --prefix "$gc_work"
    --prefix "$build_root"
    --prefix "$system_source"
    --archive "$target_root/libnetwasm-runtime.a"
  )
  for system_path in "${system_paths[@]}"; do
    normalization_arguments+=(--archive "$system_path")
  done
  node "$package_root/tools/normalize-archive-paths.mjs" \
    "${normalization_arguments[@]}"

  maximum_memory="$(node -p \
    'require(process.argv[1]).targets[process.argv[2]].maximumMemorySizeBytes' \
    "$policy" "$target")"
  export_args=()
  while IFS= read -r export_name; do
    export_args+=("--export=$export_name")
  done < "$runtime_root/runtime-exports.txt"

  footprint=""
  for base in "$alignment" 123456 67108864; do
    (( base % alignment == 0 )) || {
      echo "runtime footprint proof base '$base' is not aligned" >&2
      exit 1
    }
    initial_memory=$(( (base + 262144 + wasm_page_size - 1) / wasm_page_size * wasm_page_size ))
    proof="$temporary_root/$target-$base.wasm"
    "$wasm_ld" "$machine" \
      --whole-archive "$target_root/libnetwasm-runtime.a" --no-whole-archive \
      "${system_paths[@]}" \
      --allow-multiple-definition --no-entry --gc-sections --no-stack-first \
      --global-base="$base" -z stack-size="$native_stack_size" \
      --initial-memory="$initial_memory" --max-memory="$maximum_memory" \
      --export-memory --export-table --export=__heap_base --export=__data_end \
      "${export_args[@]}" -o "$proof"
    wasm-tools validate "$proof" --features all
    wat="$temporary_root/$target-$base.wat"
    wasm-tools print "$proof" > "$wat"
    heap_index="$(rg '\(export "__heap_base"' "$wat" | sed -E 's/.*global ([0-9]+).*/\1/')"
    heap_base="$(rg '^  \(global ' "$wat" | sed -n "$((heap_index + 1))p" | sed -E 's/.*const ([0-9]+)\).*/\1/')"
    measured="$((heap_base - base))"
    if [[ -z "$footprint" ]]; then
      footprint="$measured"
    elif [[ "$footprint" != "$measured" ]]; then
      echo "runtime footprint is not invariant for '$target'" >&2
      exit 1
    fi

    node "$package_root/tools/validate-runtime-imports.mjs" "$temporary_root/$target-$base.wasm" "$target"
    node "$package_root/tools/validate-runtime-component-metadata.mjs" \
      "$proof" "$temporary_root/runtime-component-type.bin"
  done

  node - "$target_root/layout.json" "$target" "$footprint" <<'NODE'
const fs = require("node:fs");
const [path, target, footprint] = process.argv.slice(2);
fs.writeFileSync(path, `${JSON.stringify({
  schemaVersion: 1,
  target,
  runtimeFootprintBytes: Number(footprint),
}, null, 2)}\n`);
NODE
done

node "$package_root/tools/write-runtime-pack-manifest.mjs" \
  "$package_root" "$repo_root/eng/toolchain.json"

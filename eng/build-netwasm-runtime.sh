#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
layout=""
output=""
target="wasm32"
configuration="release"
force_component_collection=0
output_kind="module"
link_map=""
additional_sources=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime-layout) layout="$2"; shift 2 ;;
        --output) output="$2"; shift 2 ;;
        --target) target="$2"; shift 2 ;;
        --configuration) configuration="$2"; shift 2 ;;
        --force-component-collection) force_component_collection=1; shift ;;
        --relocatable) output_kind="relocatable"; shift ;;
        --link-map) link_map="$2"; shift 2 ;;
        --additional-source) additional_sources+=("$2"); shift 2 ;;
        *) echo "unknown runtime-build option '$1'" >&2; exit 2 ;;
    esac
done

[[ -z "$link_map" || "$output_kind" = module ]] || {
    echo "--link-map requires a final runtime module" >&2
    exit 2
}

[[ -f "$layout" ]] || { echo "missing --runtime-layout file '$layout'" >&2; exit 2; }
[[ -n "$output" ]] || { echo "missing required option '--output'" >&2; exit 2; }
[[ "$target" = wasm32 || "$target" = wasm64 ]] || {
    echo "target must be 'wasm32' or 'wasm64'" >&2
    exit 2
}
[[ "$configuration" = debug || "$configuration" = release ]] || {
    echo "configuration must be 'debug' or 'release'" >&2
    exit 2
}

for source in "${additional_sources[@]}"; do
    [[ -f "$source" ]] || { echo "missing additional runtime source" >&2; exit 2; }
done
[[ ${#additional_sources[@]} = 0 || "$output_kind" = module ]] || {
    echo "additional sources require a final runtime module" >&2
    exit 2
}

# Node's require() resolves a bare relative path as a package name. Canonicalize
# the user-provided layout before the manifest reads below so the documented
# relative-path invocation is valid as well as absolute fixture paths.
layout="$(cd "$(dirname "$layout")" && pwd)/$(basename "$layout")"

command -v node >/dev/null || { echo "missing required tool: node" >&2; exit 1; }
runtime_manifest="$repo_root/src/NetWasm.Runtime.Pack/runtime/runtime-pack.json"
[[ -f "$runtime_manifest" ]] || {
    echo "missing runtime-pack manifest '$runtime_manifest'" >&2
    exit 1
}
runtime_layout_values="$(node - "$layout" "$runtime_manifest" "$target" <<'NODE'
const fs = require("node:fs");

const [layoutPath, manifestPath, requestedTarget] = process.argv.slice(2);
let layout;
let manifest;
try {
  layout = JSON.parse(fs.readFileSync(layoutPath, "utf8"));
  manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
} catch {
  process.stderr.write("runtime layout contract evidence is malformed\n");
  process.exit(1);
}
if (!layout || typeof layout !== "object" || Array.isArray(layout) ||
    layout.schemaVersion !== 2 || layout.target !== requestedTarget ||
    !Number.isSafeInteger(layout.applicationStaticDataEnd) ||
    layout.applicationStaticDataEnd < 0 ||
    Object.prototype.hasOwnProperty.call(layout, "runtimeGlobalBase")) {
  process.stderr.write(
    "runtime layout must contain schemaVersion 2, matching target, and applicationStaticDataEnd\n");
  process.exit(1);
}

const manifestTarget = Array.isArray(manifest?.targets)
  ? manifest.targets.find(({ target }) => target === requestedTarget)
  : undefined;
if (!manifestTarget || !Number.isSafeInteger(manifestTarget.alignment) ||
    manifestTarget.alignment <= 0) {
  process.stderr.write("runtime-pack manifest has no valid target alignment\n");
  process.exit(1);
}

const runtimeGlobalBase = Math.ceil(
  layout.applicationStaticDataEnd / manifestTarget.alignment) * manifestTarget.alignment;
if (!Number.isSafeInteger(runtimeGlobalBase)) {
  process.stderr.write("runtime layout base exceeds the supported integer range\n");
  process.exit(1);
}
process.stdout.write(`${layout.target}\n${runtimeGlobalBase}`);
NODE
)"
runtime_target="${runtime_layout_values%%$'\n'*}"
runtime_global_base="${runtime_layout_values#*$'\n'}"
[[ "$runtime_target" = "$target" && "$runtime_global_base" =~ ^[0-9]+$ ]] || {
    echo "runtime layout contract validation failed" >&2
    exit 1
}

for tool in cmake git ninja wasm-tools; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done

toolchain="$repo_root/eng/toolchain.json"
emscripten_version="$(node -p 'require(process.argv[1]).emscripten' "$toolchain")"
gc_version="$(node -p 'require(process.argv[1]).bdwgc.version' "$toolchain")"
gc_commit="$(node -p 'require(process.argv[1]).bdwgc.commit' "$toolchain")"
emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK_ROOT:-}}"
if [[ -n "$emsdk_root" ]]; then
    [[ -f "$emsdk_root/emsdk_env.sh" ]] || {
        echo "missing Emscripten SDK environment '$emsdk_root/emsdk_env.sh'" >&2
        exit 1
    }
    export EMSDK_QUIET=1
    source "$emsdk_root/emsdk_env.sh"
fi
command -v emcc >/dev/null || {
    echo "missing Emscripten; set NETWASM_EMSDK_ROOT to an emsdk installation" >&2
    exit 1
}
command -v emar >/dev/null || {
    echo "missing Emscripten archiver; set NETWASM_EMSDK_ROOT to an emsdk installation" >&2
    exit 1
}
actual_emscripten="$(emcc --version | sed -n '1s/.*) \([0-9][0-9.]*\) .*/\1/p')"
[[ "$actual_emscripten" = "$emscripten_version" ]] || {
    echo "Emscripten $emscripten_version is required; found $actual_emscripten" >&2
    exit 1
}

dependency_root="${TMPDIR:-/tmp}/netwasm-dependencies"
gc_source="$dependency_root/bdwgc-v$gc_version"
mkdir -p "$dependency_root"
if [[ ! -d "$gc_source/.git" ]]; then
    git clone --depth 1 --branch "v$gc_version" \
        https://github.com/ivmai/bdwgc.git "$gc_source"
fi
[[ "$(git -C "$gc_source" rev-parse HEAD)" = "$gc_commit" ]] || {
    echo "BDWGC checkout does not match pinned commit $gc_commit" >&2
    exit 1
}

gc_work="$gc_source"
if [[ "$target" = wasm64 ]]; then
    patch_hash="$(shasum -a 256 "$repo_root/tests/end-to-end/host-interop/runtime-memory64/bdwgc-wasm64.patch" | cut -d' ' -f1)"
    gc_work="$dependency_root/bdwgc-v$gc_version-wasm64-$patch_hash"
    if [[ ! -d "$gc_work/.git" ]]; then
        cp -R "$gc_source" "$gc_work"
        git -C "$gc_work" apply "$repo_root/tests/end-to-end/host-interop/runtime-memory64/bdwgc-wasm64.patch"
    fi
    git -C "$gc_work" diff --check
fi

build_root="$dependency_root/build-bdwgc-$gc_version-$emscripten_version-$target"
defines=(-DSTACK_NOT_SCANNED -DSMALL_CONFIG -DGC_NO_DLOPEN
    -DGC_DONT_REGISTER_MAIN_STATIC_DATA -DNO_CLOCK -DGC_DISABLE_INCREMENTAL)
if [[ "$force_component_collection" = 1 ]]; then
    defines+=(-DNETWASM_FORCE_COMPONENT_COLLECTION)
fi
target_cflags=""
if [[ "$target" = wasm64 ]]; then
    target_cflags="-sMEMORY64=1 -sWASM_BIGINT=1"
fi
cmake -S "$gc_work" -B "$build_root" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$EMSDK/upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake" \
    -DCMAKE_BUILD_TYPE=MinSizeRel -DBUILD_SHARED_LIBS=OFF \
    -Dbuild_cord=OFF -Dbuild_tests=OFF -Denable_docs=OFF \
    -Denable_threads=OFF -Denable_parallel_mark=OFF \
    -Denable_thread_local_alloc=OFF -Denable_gcj_support=OFF \
    -Denable_disclaim=OFF -Denable_atomic_uncollectable=ON \
    -Denable_dynamic_loading=OFF -Denable_register_main_static_data=OFF \
    -Denable_munmap=OFF -Ddisable_handle_fork=ON -Ddisable_gc_debug=ON \
    -Denable_gc_assertions=OFF \
    -DCMAKE_C_FLAGS="-Oz -flto${target_cflags:+ $target_cflags} ${defines[*]}" >/dev/null
cmake --build "$build_root" --target gc -j 8 >/dev/null

optimization=(-Oz -flto)
if [[ "$configuration" = debug ]]; then optimization=(-O0); fi
mkdir -p "$(dirname "$output")"
runtime_work="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-runtime.XXXXXX")"
trap 'rm -rf "$runtime_work"' EXIT
target_args=()
if [[ "$target" = wasm64 ]]; then
    target_args=(-sMEMORY64=1 -sWASM_BIGINT=1)
fi

# Keep native interface metadata in an ordinary relocatable object. Both final
# module linking and runtime-pack archive linking retain this custom section;
# component creation combines it with the application's independent WIT world.
objcopy="$EMSDK/upstream/bin/llvm-objcopy"
[[ -x "$objcopy" ]] || { echo "missing Emscripten object copier" >&2; exit 1; }
wasm-tools component embed "$repo_root/src/NetWasm.Runtime/wit" \
    --only-custom --encoding utf8 --output "$runtime_work/component-type.bin"
emcc -x c -c /dev/null "${target_args[@]}" -o "$runtime_work/metadata-empty.o"
metadata_object="$runtime_work/runtime-component-type.o"
"$objcopy" --add-section \
    "component-type:netwasm-runtime=$runtime_work/component-type.bin" \
    "$runtime_work/metadata-empty.o" "$metadata_object"
node "$repo_root/src/NetWasm.Runtime.Pack/tools/validate-runtime-component-metadata.mjs" \
    "$metadata_object" "$runtime_work/component-type.bin"

runtime_sources=(
  "$repo_root/src/NetWasm.Runtime/native_runtime.c"
  "$repo_root/src/NetWasm.Runtime/runtime_system_initializer.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_libc_initializer.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_environment_reader.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_standard_output.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_posix_io.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_stdio.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_process_exit.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_posix_exit.c" \
  "$repo_root/src/NetWasm.Runtime/runtime_abort.c" \
  "$repo_root/src/NetWasm.Runtime/object_data_address_resolver.c" \
  "$repo_root/src/NetWasm.Runtime/gc_metric_reader.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_weak_reference.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_metadata.c" \
  "$repo_root/src/NetWasm.Runtime/collector/weak_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_roots.c" \
  "$repo_root/src/NetWasm.Runtime/collector/strong_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/gc_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_allocation.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_lifecycle.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_collection.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_finalization.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_identity_hash.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_reference_store.c" \
  "$repo_root/src/NetWasm.Runtime/collector/boehm_pinning.c"
)
runtime_sources+=("${additional_sources[@]}")
libc_internal_include="$EMSDK/upstream/emscripten/system/lib/libc/musl/src/internal"
libc_arch_include="$EMSDK/upstream/emscripten/system/lib/libc/musl/arch/emscripten"
libc_source_include="$EMSDK/upstream/emscripten/system/lib/libc"
if [[ "$output_kind" = relocatable ]]; then
    runtime_sources+=("$repo_root/src/NetWasm.Runtime/runtime_system_support.c")
    archive_work="$runtime_work/archive"
    mkdir "$archive_work"
    runtime_objects=("$metadata_object")
    for source in "${runtime_sources[@]}"; do
        object="$archive_work/runtime-$(basename "${source%.c}").o"
        emcc "$source" -c -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work/include" -I"$libc_internal_include" \
            -I"$libc_arch_include" -I"$libc_source_include" "${defines[@]}" \
            "${target_args[@]}" "${optimization[@]}" -o "$object"
        runtime_objects+=("$object")
    done
    mkdir "$archive_work/gc"
    (
        cd "$archive_work/gc"
        emar x "$build_root/libgc.a"
    )
    gc_objects=("$archive_work"/gc/*.o)
    emar rcs "$output" "${runtime_objects[@]}" "${gc_objects[@]}"
else
    initial_memory="$(( (runtime_global_base + 4194304 + 65535) / 65536 * 65536 ))"
    emcc_args=(
      "$metadata_object"
      "${runtime_sources[@]}"
      "$build_root/libgc.a"
      -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work/include" -I"$libc_internal_include" -I"$libc_arch_include"
      -I"$libc_source_include" "${defines[@]}"
    )
    if [[ "$target" = wasm64 ]]; then
        emcc_args+=(-sMEMORY64=1 -sWASM_BIGINT=1)
    fi
    emcc_args+=(-sSTANDALONE_WASM=1 -sFILESYSTEM=0 -sMALLOC=emmalloc)
    if [[ -n "$link_map" ]]; then
        mkdir -p "$(dirname "$link_map")"
        emcc_args+=(--emit-symbol-map -Wl,--print-gc-sections "-Wl,-Map=$link_map")
    fi
    emcc_args+=(
        -sGLOBAL_BASE="$runtime_global_base" -sINITIAL_MEMORY="$initial_memory"
        -sALLOW_MEMORY_GROWTH=1 -Wl,--no-entry -Wl,--gc-sections
        "${optimization[@]}" -o "$output"
    )
    emcc "${emcc_args[@]}"
    wasm-tools validate "$output" --features all
    node "$repo_root/src/NetWasm.Runtime.Pack/tools/validate-runtime-component-metadata.mjs" \
        "$output" "$runtime_work/component-type.bin"
fi

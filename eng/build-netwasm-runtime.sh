#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
layout=""
output=""
target="wasm32"
configuration="release"
force_component_collection=0
output_kind="module"
collector_output=""
collector="boehm"
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
        --collector-output) collector_output="$2"; shift 2 ;;
        --collector) collector="$2"; shift 2 ;;
        --link-map) link_map="$2"; shift 2 ;;
        --additional-source) additional_sources+=("$2"); shift 2 ;;
        *) echo "unknown runtime-build option '$1'" >&2; exit 2 ;;
    esac
done

[[ "$collector" = boehm || "$collector" = tcms ]] || {
    echo "collector must be 'boehm' or 'tcms'" >&2
    exit 2
}

[[ -z "$link_map" || "$output_kind" = module ]] || {
    echo "--link-map requires a final runtime module" >&2
    exit 2
}
[[ -z "$collector_output" || "$output_kind" = relocatable ]] || {
    echo "--collector-output requires a relocatable runtime archive" >&2
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
[[ "$output_kind" != relocatable || -n "$collector_output" ]] || {
    echo "--relocatable requires --collector-output" >&2
    exit 2
}

# Node's require() resolves a bare relative path as a package name. Canonicalize
# the user-provided layout before the manifest reads below so the documented
# relative-path invocation is valid as well as absolute fixture paths.
layout="$(cd "$(dirname "$layout")" && pwd)/$(basename "$layout")"

command -v node >/dev/null || { echo "missing required tool: node" >&2; exit 1; }
runtime_policy="$repo_root/src/NetWasm.Runtime.Pack/runtime/runtime-policy.json"
[[ -f "$runtime_policy" ]] || {
    echo "missing runtime-pack policy '$runtime_policy'" >&2
    exit 1
}
runtime_layout_values="$(node - "$layout" "$runtime_policy" "$target" <<'NODE'
const fs = require("node:fs");

const [layoutPath, policyPath, requestedTarget] = process.argv.slice(2);
let layout;
let policy;
try {
  layout = JSON.parse(fs.readFileSync(layoutPath, "utf8"));
  policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
} catch {
  process.stderr.write("runtime layout contract evidence is malformed\n");
  process.exit(1);
}
if (!layout || typeof layout !== "object" || Array.isArray(layout) ||
    ![2, 3, 4].includes(layout.schemaVersion) ||
    (layout.schemaVersion >= 3 && !Array.isArray(layout.nativeImports)) ||
    layout.target !== requestedTarget ||
    !Number.isSafeInteger(layout.applicationStaticDataEnd) ||
    layout.applicationStaticDataEnd < 0 ||
    Object.prototype.hasOwnProperty.call(layout, "runtimeGlobalBase")) {
  process.stderr.write(
    "runtime layout must contain schemaVersion 2, 3 or 4, matching target, and applicationStaticDataEnd\n");
  process.exit(1);
}

if (layout.runtimeFeatures !== undefined &&
    (!Array.isArray(layout.runtimeFeatures) ||
     layout.runtimeFeatures.some(feature => typeof feature !== "string") ||
     new Set(layout.runtimeFeatures).size !== layout.runtimeFeatures.length ||
     [...layout.runtimeFeatures].sort().some((feature, index) =>
       feature !== layout.runtimeFeatures[index]))) {
  process.stderr.write("runtime layout features must be a canonical string array\n");
  process.exit(1);
}

if (!policy?.targets?.[requestedTarget] || !Number.isSafeInteger(policy.alignment) ||
    policy.alignment <= 0) {
  process.stderr.write("runtime-pack policy has no valid target alignment\n");
  process.exit(1);
}

const runtimeGlobalBase = Math.ceil(
  layout.applicationStaticDataEnd / policy.alignment) * policy.alignment;
if (!Number.isSafeInteger(runtimeGlobalBase)) {
  process.stderr.write("runtime layout base exceeds the supported integer range\n");
  process.exit(1);
}
// Layouts written before runtime feature evidence was introduced retain the
// complete source runtime for compatibility. Current compiler layouts opt into
// ephemeron code only when ConditionalWeakTable is reachable.
const includeEphemerons = layout.runtimeFeatures === undefined ||
  layout.runtimeFeatures.includes("ephemeron-handles");
const includeStructuredCommandDiagnostics = layout.runtimeFeatures === undefined ||
  layout.runtimeFeatures.includes("structured-command-diagnostics");
process.stdout.write(`${layout.target}\n${runtimeGlobalBase}\n${includeEphemerons ? 1 : 0}\n${includeStructuredCommandDiagnostics ? 1 : 0}`);
NODE
)"
runtime_target="${runtime_layout_values%%$'\n'*}"
runtime_layout_values="${runtime_layout_values#*$'\n'}"
runtime_global_base="${runtime_layout_values%%$'\n'*}"
include_ephemerons="${runtime_layout_values#*$'\n'}"
include_structured_command_diagnostics="${include_ephemerons#*$'\n'}"
include_ephemerons="${include_ephemerons%%$'\n'*}"
[[ "$runtime_target" = "$target" && "$runtime_global_base" =~ ^[0-9]+$ &&
   "$include_ephemerons" =~ ^[01]$ &&
   "$include_structured_command_diagnostics" =~ ^[01]$ ]] || {
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
actual_emscripten="$(emcc --version | sed -nE \
    '1s/.*\) ([0-9]+(\.[0-9]+)+)([-+][^ ]+)? .*/\1/p')"
[[ "$actual_emscripten" = "$emscripten_version" ]] || {
    echo "Emscripten $emscripten_version is required; found $actual_emscripten" >&2
    exit 1
}

defines=()
if [[ "$force_component_collection" = 1 ]]; then
    defines+=(-DNETWASM_FORCE_COMPONENT_COLLECTION)
fi
if [[ "$collector" = boehm ]]; then
dependency_root="${NETWASM_DEPENDENCY_ROOT:-${TMPDIR:-/tmp}/netwasm-dependencies}"
[[ "$dependency_root" = /* ]] || {
    echo "NETWASM_DEPENDENCY_ROOT must be an absolute path" >&2
    exit 2
}
gc_source="$dependency_root/bdwgc-v$gc_version"
mkdir -p "$dependency_root"

dependency_lock="$dependency_root/.bdwgc-$gc_version-$emscripten_version.lock"
dependency_lock_owned=0
for attempt in {1..6000}; do
    if mkdir "$dependency_lock" 2>/dev/null; then
        printf '%s\n' "$$" > "$dependency_lock/pid"
        dependency_lock_owned=1
        break
    fi
    if [[ -f "$dependency_lock/pid" ]]; then
        # The owner can remove the pid file between the existence check and
        # this read. Treat that as an unlocked retry instead of letting
        # `set -e` abort a concurrent runtime build.
        lock_pid="$(cat "$dependency_lock/pid" 2>/dev/null || true)"
        if [[ "$lock_pid" =~ ^[0-9]+$ ]] && ! kill -0 "$lock_pid" 2>/dev/null; then
            stale_lock="$dependency_lock.stale.$$"
            if mv "$dependency_lock" "$stale_lock" 2>/dev/null; then
                rm -rf "$stale_lock"
            fi
        fi
    fi
    sleep 0.1
done
[[ "$dependency_lock_owned" = 1 ]] || {
    echo "timed out waiting for shared BDWGC dependency cache" >&2
    exit 1
}
release_dependency_lock() {
    if [[ "$dependency_lock_owned" = 1 ]]; then
        rm -f "$dependency_lock/pid"
        rmdir "$dependency_lock" 2>/dev/null || true
        dependency_lock_owned=0
    fi
}
trap release_dependency_lock EXIT
trap 'release_dependency_lock; exit 130' INT
trap 'release_dependency_lock; exit 143' TERM

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

collection_profile="ordinary"
if [[ "$force_component_collection" = 1 ]]; then
    collection_profile="forced-component"
fi
build_root="$dependency_root/build-bdwgc-$gc_version-$emscripten_version-$target-$collection_profile"
defines=(-DSTACK_NOT_SCANNED -DSMALL_CONFIG -DGC_NO_DLOPEN
    -DGC_DONT_REGISTER_MAIN_STATIC_DATA -DNO_CLOCK -DNO_GETENV
    -DGC_DISABLE_INCREMENTAL)
if [[ "$force_component_collection" = 1 ]]; then
    defines+=(-DNETWASM_FORCE_COMPONENT_COLLECTION)
fi
target_cflags=""
if [[ "$target" = wasm64 ]]; then
    target_cflags="-sMEMORY64=1 -sWASM_BIGINT=1"
fi
dependency_cache_key="$(
    {
        printf '%s\n' "$gc_commit" "$emscripten_version" "$target" \
            "$force_component_collection"
        shasum -a 256 "$repo_root/eng/build-netwasm-runtime.sh"
    } | shasum -a 256 | cut -d' ' -f1
)"
dependency_cache_stamp="$build_root/.netwasm-cache-key"
cached_dependency_key=""
if [[ -f "$dependency_cache_stamp" ]]; then
    cached_dependency_key="$(cat "$dependency_cache_stamp")"
fi
if [[ ! -f "$build_root/libgc.a" ||
      "$cached_dependency_key" != "$dependency_cache_key" ]]; then
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
    node "$repo_root/src/NetWasm.Runtime.Pack/tools/relativize-ninja-source-root.mjs" \
        "$build_root/build.ninja" "$gc_work" "$build_root"
    cmake --build "$build_root" --target gc -j 8 >/dev/null
    printf '%s\n' "$dependency_cache_key" > "$dependency_cache_stamp.tmp.$$"
    mv "$dependency_cache_stamp.tmp.$$" "$dependency_cache_stamp"
fi
release_dependency_lock
trap - EXIT INT TERM
else
    defines+=('-DNETWASM_UNMANAGED_ALLOCATOR_BACKEND="unmanaged_allocator_libc.h"')
    gc_work="$repo_root/src/NetWasm.Runtime/collector/compact"
fi

optimization=(-Oz -flto)
configuration_defines=()
if [[ "$configuration" = debug ]]; then
    optimization=(-O0)
    configuration_defines=(-DNETWASM_GC_DIAGNOSTICS)
fi
if [[ "$include_structured_command_diagnostics" = 1 ]]; then
    configuration_defines+=("-DNETWASM_STRUCTURED_COMMAND_DIAGNOSTICS")
fi
# NetWasm does not currently publish DWARF. Keep this explicit because recent
# Emscripten builds can otherwise retain native-runtime DWARF in -O0 output.
debug_information=(-g0)
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
if [[ "$include_ephemerons" = 1 ]]; then
    runtime_sources+=(
      "$repo_root/src/NetWasm.Runtime/ephemeron_runtime.c"
      "$repo_root/src/NetWasm.Runtime/collector/boehm_ephemeron.c"
      "$repo_root/src/NetWasm.Runtime/collector/ephemeron_handle_table.c"
    )
fi
collector_sources=()
collector_archive=""
if [[ "$collector" = tcms ]]; then
    # Keep the existing collector interfaces and shared handle stores. Select
    # adapters at composition time, without runtime backend dispatch.
    selected_sources=()
    for source in "${runtime_sources[@]}"; do
        case "$(basename "$source")" in
            boehm_identity_hash.c|boehm_reference_store.c|boehm_pinning.c) ;;
            boehm_*.c) source="${source%/*}/tcms_${source##*/boehm_}" ;;
        esac
        selected_sources+=("$source")
    done
    runtime_sources=("${selected_sources[@]}")
    collector_sources=("$gc_work/tcms.c" "$gc_work/tcms_lifetime.c")
else
    collector_archive="$build_root/libgc.a"
fi
runtime_sources+=("${additional_sources[@]}")
libc_internal_include="$EMSDK/upstream/emscripten/system/lib/libc/musl/src/internal"
libc_arch_include="$EMSDK/upstream/emscripten/system/lib/libc/musl/arch/emscripten"
libc_source_include="$EMSDK/upstream/emscripten/system/lib/libc"
if [[ "$output_kind" = relocatable ]]; then
    archive_work="$runtime_work/archive"
    mkdir "$archive_work"
    runtime_objects=("$metadata_object")
    for source in "${runtime_sources[@]}"; do
        object="$archive_work/runtime-$(basename "${source%.c}").o"
        relative_source="${source#"$repo_root"/}"
        [[ "$relative_source" != "$source" ]] || {
            echo "relocatable runtime sources must belong to the repository" >&2
            exit 2
        }
        (
            cd "$repo_root"
            emcc "$relative_source" -c -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work/include" -I"$gc_work" \
                -I"$libc_internal_include" -I"$libc_arch_include" -I"$libc_source_include" \
                "${defines[@]}" "${configuration_defines[@]}" \
                -DNETWASM_RUNTIME_PACK \
                "${target_args[@]}" "${optimization[@]}" \
                "${debug_information[@]}" -o "$object"
        )
        runtime_objects+=("$object")
    done
    emar rcs "$output" "${runtime_objects[@]}"
    mkdir -p "$(dirname "$collector_output")"
    if [[ "$collector" = tcms ]]; then
        collector_objects=()
        for source in "${collector_sources[@]}"; do
            object="$archive_work/collector-$(basename "${source%.c}").o"
            emcc "$source" -c "${target_args[@]}" "${optimization[@]}" \
                "${debug_information[@]}" -o "$object"
            collector_objects+=("$object")
        done
        emar rcs "$collector_output" "${collector_objects[@]}"
    else
        cp "$collector_archive" "$collector_output"
    fi
else
    initial_memory="$(( (runtime_global_base + 4194304 + 65535) / 65536 * 65536 ))"
    emcc_args=(
      "$metadata_object"
      "${runtime_sources[@]}"
      "${collector_sources[@]}"
      -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work/include" -I"$gc_work" -I"$libc_internal_include" -I"$libc_arch_include"
      -I"$libc_source_include" "${defines[@]}" "${configuration_defines[@]}"
    )
    if [[ -n "$collector_archive" ]]; then emcc_args+=("$collector_archive"); fi
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
        "${optimization[@]}" "${debug_information[@]}" -o "$output"
    )
    emcc "${emcc_args[@]}"
    wasm-tools validate "$output" --features all
    node "$repo_root/src/NetWasm.Runtime.Pack/tools/validate-runtime-component-metadata.mjs" \
        "$output" "$runtime_work/component-type.bin"
fi

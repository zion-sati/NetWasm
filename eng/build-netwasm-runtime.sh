#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
layout=""
output=""
target="wasm32"
configuration="release"
native_profile="size"
force_component_collection=0
gc_stress=0
output_kind="module"
collector_output=""
collector="compact"
link_map=""
additional_sources=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime-layout) layout="$2"; shift 2 ;;
        --output) output="$2"; shift 2 ;;
        --target) target="$2"; shift 2 ;;
        --configuration) configuration="$2"; shift 2 ;;
        --native-profile) native_profile="$2"; shift 2 ;;
        --force-component-collection) force_component_collection=1; shift ;;
        --gc-stress) gc_stress=1; shift ;;
        --relocatable) output_kind="relocatable"; shift ;;
        --collector-output) collector_output="$2"; shift 2 ;;
        --collector) collector="$2"; shift 2 ;;
        --link-map) link_map="$2"; shift 2 ;;
        --additional-source) additional_sources+=("$2"); shift 2 ;;
        *) echo "unknown runtime-build option '$1'" >&2; exit 2 ;;
    esac
done

[[ "$collector" = compact || "$collector" = netwasm || "$collector" = bump ]] || {
    echo "collector must be 'compact', 'netwasm' or 'bump'" >&2
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
[[ "$native_profile" = size || "$native_profile" = speed ]] || {
    echo "native profile must be 'size' or 'speed'" >&2
    exit 2
}
[[ "$configuration" != debug || "$native_profile" = size ]] || {
    echo "the speed native profile requires release configuration" >&2
    exit 2
}
native_optimization=-Oz
if [[ "$native_profile" = speed ]]; then
    native_optimization=-O3
fi

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
const includeFinalization = layout.runtimeFeatures === undefined ||
  layout.runtimeFeatures.includes("finalization");
const includeArraySetValue = layout.runtimeFeatures === undefined ||
  layout.runtimeFeatures.includes("array-set-value");
process.stdout.write(
  `${layout.target}\n${runtimeGlobalBase}\n${includeEphemerons ? 1 : 0}\n${includeStructuredCommandDiagnostics ? 1 : 0}\n${includeFinalization ? 1 : 0}\n${includeArraySetValue ? 1 : 0}`);
NODE
)"
runtime_target="${runtime_layout_values%%$'\n'*}"
runtime_layout_values="${runtime_layout_values#*$'\n'}"
runtime_global_base="${runtime_layout_values%%$'\n'*}"
runtime_layout_values="${runtime_layout_values#*$'\n'}"
include_ephemerons="${runtime_layout_values%%$'\n'*}"
runtime_layout_values="${runtime_layout_values#*$'\n'}"
include_structured_command_diagnostics="${runtime_layout_values%%$'\n'*}"
runtime_layout_values="${runtime_layout_values#*$'\n'}"
include_finalization="${runtime_layout_values%%$'\n'*}"
include_array_set_value="${runtime_layout_values#*$'\n'}"
[[ "$runtime_target" = "$target" && "$runtime_global_base" =~ ^[0-9]+$ &&
   "$include_ephemerons" =~ ^[01]$ &&
   "$include_structured_command_diagnostics" =~ ^[01]$ &&
   "$include_finalization" =~ ^[01]$ &&
   "$include_array_set_value" =~ ^[01]$ ]] || {
    echo "runtime layout contract validation failed" >&2
    exit 1
}

for tool in wasm-tools; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done

toolchain="$repo_root/eng/toolchain.json"
emscripten_version="$(node -p 'require(process.argv[1]).emscripten' "$toolchain")"
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
collector_defines=()
if [[ "$collector" = compact || "$collector" = netwasm ]]; then
    # Internal collector invariants stay checked in debug builds. Shipping
    # assertion messages also retains libc's diagnostic formatting machinery.
    # Public argument, overflow and allocation checks are ordinary code.
    if [[ "$configuration" = release ]]; then
        collector_defines+=(-DNDEBUG)
    fi
fi
if [[ "$gc_stress" = 1 ]]; then
    # Collects before every managed allocation, for qualification: a missing
    # root then frees what it should keep at the first chance. Bump never
    # collects.
    [[ "$collector" = compact || "$collector" = netwasm ]] || {
        echo "--gc-stress requires the compact or netwasm collector" >&2
        exit 2
    }
    collector_defines+=(-DNETWASM_GC_STRESS)
fi
if [[ "$collector" = compact ]]; then
    # Compact shares NetWasmGC's adapters and lifetime layer around its own core.
    collector_defines+=(-DNETWASM_GC_COMPACT)
fi
if [[ "$force_component_collection" = 1 ]]; then
    defines+=(-DNETWASM_FORCE_COMPONENT_COLLECTION)
fi
gc_work="$repo_root/src/NetWasm.Runtime/collector/netwasm"
if [[ "$collector" = bump ]]; then
    gc_work="$repo_root/src/NetWasm.Runtime/collector/bump"
fi
if [[ "$include_finalization" = 0 ]]; then
    defines+=(-DNETWASM_NO_FINALIZATION)
fi
if [[ "$include_array_set_value" = 0 ]]; then
    defines+=(-DNETWASM_NO_ARRAY_SET_VALUE)
fi

optimization=("$native_optimization" -flto)
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
  "$repo_root/src/NetWasm.Runtime/collector/weak_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/strong_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/gc_handle_table.c" \
  "$repo_root/src/NetWasm.Runtime/collector/identity_hash.c" \
  "$repo_root/src/NetWasm.Runtime/collector/reference_store.c" \
  "$repo_root/src/NetWasm.Runtime/collector/pinning.c"
)
if [[ "$include_ephemerons" = 1 ]]; then
    runtime_sources+=(
      "$repo_root/src/NetWasm.Runtime/ephemeron_runtime.c"
      "$repo_root/src/NetWasm.Runtime/collector/ephemeron_handle_table.c"
    )
fi
if [[ "$collector" = bump ]]; then
    defines+=(-DNETWASM_BUMP_COLLECTOR)
    runtime_sources+=("$repo_root/src/NetWasm.Runtime/collector/bump_runtime.c")
    collector_sources=("$gc_work/bump_arena.c")
else
    for adapter in weak_reference metadata roots allocation lifecycle collection finalization; do
        runtime_sources+=("$gc_work/$adapter.c")
    done
    if [[ "$include_ephemerons" = 1 ]]; then
        runtime_sources+=("$gc_work/ephemeron.c")
    fi
    # Compact has its own size-first core behind NetWasmGC's interface.
    collector_core="$gc_work/netwasm_gc.c"
    if [[ "$collector" = compact ]]; then collector_core="$gc_work/compact_gc.c"; fi
    collector_sources=("$collector_core" "$gc_work/lifetime.c")
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
            emcc "$relative_source" -c -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work" \
                -I"$libc_internal_include" -I"$libc_arch_include" -I"$libc_source_include" \
                "${defines[@]}" "${configuration_defines[@]}" "${collector_defines[@]}" \
                -DNETWASM_RUNTIME_PACK \
                "${target_args[@]}" "${optimization[@]}" \
                "${debug_information[@]}" -o "$object"
        )
        runtime_objects+=("$object")
    done
    # emar never removes members, so a rebuilt archive starts empty.
    rm -f "$output"
    emar rcs "$output" "${runtime_objects[@]}"
    mkdir -p "$(dirname "$collector_output")"
    collector_objects=()
    for source in "${collector_sources[@]}"; do
        object="$archive_work/collector-$(basename "${source%.c}").o"
        emcc "$source" -c "${target_args[@]}" "${optimization[@]}" "${collector_defines[@]}" \
            "${debug_information[@]}" -o "$object"
        collector_objects+=("$object")
    done
    rm -f "$collector_output"
    emar rcs "$collector_output" "${collector_objects[@]}"
else
    initial_memory="$(( (runtime_global_base + 4194304 + 65535) / 65536 * 65536 ))"
    emcc_args=(
      "$metadata_object"
      "${runtime_sources[@]}"
      "${collector_sources[@]}"
      -I"$repo_root/src/NetWasm.Runtime" -I"$gc_work" -I"$libc_internal_include" -I"$libc_arch_include"
      -I"$libc_source_include" "${defines[@]}" "${configuration_defines[@]}" "${collector_defines[@]}"
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
        "${optimization[@]}" "${debug_information[@]}" -o "$output"
    )
    emcc "${emcc_args[@]}"
    wasm-tools validate "$output" --features all
    node "$repo_root/src/NetWasm.Runtime.Pack/tools/validate-runtime-component-metadata.mjs" \
        "$output" "$runtime_work/component-type.bin"
fi

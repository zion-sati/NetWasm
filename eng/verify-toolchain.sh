#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="$repo_root/eng/toolchain.json"

usage() {
    cat <<'EOF' >&2
Usage: eng/verify-toolchain.sh [--qualification]

Without options, verifies the documented public source-build prerequisites.
--qualification additionally verifies the exact tools used by the full repository matrix.
EOF
}

qualification=0
if [[ "$#" -gt 1 ]]; then
    usage
    exit 2
fi
if [[ "$#" -eq 1 ]]; then
    case "$1" in
        --qualification) qualification=1 ;;
        -h|--help) usage; exit 0 ;;
        *) usage; exit 2 ;;
    esac
fi

for tool in dotnet npm python3; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "missing required tool: $tool" >&2
        exit 1
    fi
done

node_path="${NETWASM_NODE_PATH:-${EMSDK_NODE:-$(command -v node || true)}}"
if [[ -z "$node_path" || ! -x "$node_path" ]]; then
    echo "missing required Node.js executable" >&2
    exit 1
fi

node_modules_root="${NETWASM_NODE_MODULES_ROOT:-$repo_root/node_modules}"
playwright_command="$node_modules_root/.bin/playwright"
jco_command="$node_modules_root/.bin/jco"
for javascript_tool in "$playwright_command" "$jco_command"; do
    if [[ ! -x "$javascript_tool" ]]; then
        echo "missing installed JavaScript tool: $javascript_tool" >&2
        exit 1
    fi
done

emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK:-${EMSDK_ROOT:-}}}"
if [[ -z "$emsdk_root" || ! -x "$emsdk_root/upstream/emscripten/emcc" ]]; then
    echo "NETWASM_EMSDK_ROOT must identify the pinned Emscripten SDK" >&2
    exit 1
fi
for binaryen_tool in wasm-as wasm-dis wasm-merge wasm-opt; do
    if [[ ! -x "$emsdk_root/upstream/bin/$binaryen_tool" ]]; then
        echo "activated Emscripten SDK is missing Binaryen tool: $binaryen_tool" >&2
        exit 1
    fi
done

read_manifest() {
    "$node_path" -e 'const value=require(process.argv[1]); console.log(process.argv[2].split(".").reduce((current, key) => current[key], value))' \
        "$manifest" "$1"
}

sdk_version="$(read_manifest dotnetSdk)"
roslyn_version="$(read_manifest roslynCompiler)"
binaryen_version="$(read_manifest binaryen)"
wabt_version="$(read_manifest wabt)"
node_version="$(read_manifest node)"
node_minimum_major="$(read_manifest nodeMinimumMajor)"
playwright_version="$(read_manifest playwright)"
emscripten_version="$(read_manifest emscripten)"
llvm_lld_version="$(read_manifest llvmLld.version)"
wasm_tools_version="$(read_manifest wasmTools)"
wasmtime_version="$(read_manifest wasmtime)"
jco_version="$(read_manifest jco)"
preview2_shim_version="$(read_manifest preview2Shim)"

require_version() {
    local label="$1"
    local actual="$2"
    local expected="$3"
    if [[ "$actual" != "$expected" ]]; then
        printf '%s version mismatch: expected %s, found %s\n' \
            "$label" "$expected" "$actual" >&2
        exit 1
    fi
}

require_version dotnet "$(dotnet --version)" "$sdk_version"
actual_node_version="$("$node_path" --version)"
actual_node_major="$(sed -nE 's/^v([0-9]+).*/\1/p' <<<"$actual_node_version")"
if [[ -z "$actual_node_major" || "$actual_node_major" -lt "$node_minimum_major" ]]; then
    printf 'node version mismatch: required major %s or newer, found %s\n' \
        "$node_minimum_major" "$actual_node_version" >&2
    exit 1
fi
require_version binaryen \
    "$("$emsdk_root/upstream/bin/wasm-opt" --version | awk '{print $3}')" \
    "$binaryen_version"
require_version playwright "$("$playwright_command" --version)" "Version $playwright_version"
require_version jco "$("$jco_command" --version)" "$jco_version"

actual_lld_version="$("$emsdk_root/upstream/bin/wasm-ld" --version)"
actual_lld_major="$(sed -nE 's/.*LLD ([0-9]+).*/\1/p' <<<"$actual_lld_version")"
required_lld_major="${llvm_lld_version%%.*}"
if [[ -z "$actual_lld_major" || "$actual_lld_major" -lt "$required_lld_major" ]]; then
    printf 'wasm-ld version mismatch: required LLD major %s or newer\n' \
        "$required_lld_major" >&2
    exit 1
fi

if [[ "$qualification" -eq 1 ]]; then
    for tool in wasm-validate wasm-tools wasmtime; do
        if ! command -v "$tool" >/dev/null 2>&1; then
            echo "missing qualification tool: $tool" >&2
            exit 1
        fi
    done
    require_version node "$actual_node_version" "v$node_version"
    require_version wabt "$(wasm-validate --version)" "$wabt_version"
    require_version wasm-tools "$(wasm-tools --version)" "wasm-tools $wasm_tools_version"
    require_version wasmtime "$(wasmtime --version | cut -d' ' -f2)" "$wasmtime_version"
fi

package_version() {
    "$node_path" -e 'const value=require(process.argv[1]); console.log(value.packages[process.argv[2]].version)' \
        "$repo_root/package-lock.json" "node_modules/$1"
}

require_version package-jco "$(package_version @bytecodealliance/jco)" "$jco_version"
require_version package-preview2-shim \
    "$(package_version @bytecodealliance/preview2-shim)" "$preview2_shim_version"

actual_emscripten="$("$emsdk_root/upstream/emscripten/emcc" --version | \
    sed -n '1s/.*) \([0-9][0-9.]*\) .*/\1/p')"
require_version emscripten "$actual_emscripten" "$emscripten_version"

dotnet_root="$(dirname "$(command -v dotnet)")"
csc="$dotnet_root/sdk/$sdk_version/Roslyn/bincore/csc.dll"
if [[ ! -f "$csc" ]]; then
    echo "missing pinned Roslyn compiler: $csc" >&2
    exit 1
fi
actual_roslyn="$(dotnet "$csc" -version)"
require_version roslyn "${actual_roslyn%% *}" "$roslyn_version"

printf 'NetWasm toolchain PASS\n'

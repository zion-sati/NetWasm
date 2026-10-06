#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# -lt 1 || $# -gt 2 || "$1" != /* || -e "$1" ||
      ( $# -eq 2 && "$2" != --compare-collectors ) ]]; then
    echo 'usage: measure-console42-size.sh <new absolute evidence directory> [--compare-collectors]' >&2
    exit 2
fi
evidence_root="$1"
compare_collectors=0
if [[ $# -eq 2 ]]; then compare_collectors=1; fi
case "$evidence_root" in
    "$repo_root"/*)
        echo 'evidence directory must be outside the source worktree' >&2
        exit 2
        ;;
esac
mkdir -p "$evidence_root/logs" "$evidence_root/tmp"
trap 'status=$?; printf "%s\n" "$status" > "$evidence_root/exit-status.txt"' EXIT
cd "$repo_root"

if [[ -n "$(git status --porcelain)" ]]; then
    echo 'size measurement requires a clean worktree' >&2
    exit 3
fi
git rev-parse HEAD > "$evidence_root/source-head.txt"
shasum -a 256 global.json eng/toolchain.json \
    eng/size-canary/Hello42.cs eng/size-canary/Program.cs \
    eng/size-canary/SizeCanary.csproj \
    eng/size-canary/expected-component-bytes.txt \
    eng/size-canary/expected-compact-component-bytes.txt \
    eng/compare-collector-size.mjs \
    eng/measure-console42-size.sh \
    > "$evidence_root/input-sha256.txt"

export NUGET_PACKAGES="$evidence_root/nuget-packages"
export TMPDIR="$evidence_root/tmp"
export NETWASM_NODE_MODULES_ROOT="${NETWASM_NODE_MODULES_ROOT:-$repo_root/node_modules}"
export NETWASM_EMSDK_ROOT="${NETWASM_EMSDK_ROOT:-${EMSDK:-${EMSDK_ROOT:-}}}"

run_logged() {
    local name="$1"
    shift
    local status=0
    if "$@" > "$evidence_root/logs/$name.stdout.log" \
        2> "$evidence_root/logs/$name.stderr.log"; then
        status=0
    else
        status=$?
    fi
    printf "%s\n" "$status" > "$evidence_root/logs/$name.exit-status.txt"
    return "$status"
}

run_logged toolchain bash eng/verify-toolchain.sh --qualification
run_logged corelib dotnet build src/NetWasm.CoreLib/NetWasm.CoreLib.csproj \
    -c Release --nologo
run_logged harness dotnet build eng/size-canary/SizeCanary.csproj \
    -c Release --nologo

sdk_version="$(dotnet --version)"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
assembly="$evidence_root/NetWasmApp.dll"
application="$evidence_root/application.wasm"
runtime="$evidence_root/runtime.wasm"
layout="$evidence_root/runtime-layout.json"
command_wit="$evidence_root/command.wit.wasm"
component="$evidence_root/NetWasmApp.wasm"
harness="$repo_root/eng/size-canary/bin/Release/net10.0/SizeCanary.dll"

run_logged csc dotnet "$csc" -nologo -noconfig -nostdlib \
    -langversion:latest \
    '-define:NETWASM_REF_STRUCT_GENERICS;NETWASM_REGEX_STRING_CREATE;SYSTEM_TEXT_REGULAREXPRESSIONS' \
    -deterministic+ -optimize+ -target:exe \
    -runtimemetadataversion:v4.0.30319 \
    "-reference:$corelib" "-out:$assembly" eng/size-canary/Hello42.cs
run_logged compile dotnet "$harness" compile "$repo_root" "$assembly" \
    "$application" "$layout"
run_logged runtime bash eng/build-netwasm-runtime.sh \
    --runtime-layout "$layout" --target wasm32 \
    --configuration release --output "$runtime"
run_logged command-wit wasm-tools component wit \
    src/NetWasm.Toolchain/wit --wasm --output "$command_wit"
run_logged package dotnet "$harness" package "$application" "$runtime" \
    "$command_wit" "$component"
run_logged validate wasm-tools validate "$component"
run_logged wit wasm-tools component wit "$component"
run_logged run wasmtime run "$component"

if [[ "$(< "$evidence_root/logs/run.stdout.log")" != '42' ]] || \
   ! rg -q 'export wasi:cli/run@0.2.11' "$evidence_root/logs/wit.stdout.log"; then
    echo 'final component does not satisfy the Console42 command contract' >&2
    exit 4
fi

component_bytes="$(wc -c < "$component" | tr -d ' ')"
expected_component_bytes="$(tr -d '[:space:]' < \
    eng/size-canary/expected-component-bytes.txt)"
if [[ ! "$expected_component_bytes" =~ ^[0-9]+$ ]] || \
   [[ "$component_bytes" != "$expected_component_bytes" ]]; then
    printf 'Console42 component size mismatch: expected %s bytes, found %s bytes\n' \
        "$expected_component_bytes" "$component_bytes" >&2
    exit 6
fi

if [[ "$compare_collectors" = 1 ]]; then
    candidate_runtime="$evidence_root/runtime-tcms.wasm"
    candidate_component="$evidence_root/NetWasmApp-tcms.wasm"
    run_logged runtime-tcms bash eng/build-netwasm-runtime.sh \
        --collector tcms --runtime-layout "$layout" --target wasm32 \
        --configuration release --output "$candidate_runtime"
    run_logged package-tcms dotnet "$harness" package "$application" \
        "$candidate_runtime" "$command_wit" "$candidate_component"
    run_logged validate-tcms wasm-tools validate "$candidate_component"
    run_logged wit-tcms wasm-tools component wit "$candidate_component"
    run_logged run-tcms wasmtime run "$candidate_component"
    compact_component_bytes="$(wc -c < "$candidate_component" | tr -d ' ')"
    expected_compact_component_bytes="$(tr -d '[:space:]' < \
        eng/size-canary/expected-compact-component-bytes.txt)"
    if [[ ! "$expected_compact_component_bytes" =~ ^[0-9]+$ ]] || \
       [[ "$compact_component_bytes" != "$expected_compact_component_bytes" ]]; then
        printf 'Compact Console42 component size mismatch: expected %s bytes, found %s bytes\n' \
            "$expected_compact_component_bytes" "$compact_component_bytes" >&2
        exit 6
    fi
    if [[ "$(< "$evidence_root/logs/run-tcms.stdout.log")" != '42' ]] || \
       ! rg -q 'export wasi:cli/run@0.2.11' "$evidence_root/logs/wit-tcms.stdout.log"; then
        echo 'candidate component does not satisfy the Console42 command contract' >&2
        exit 4
    fi
    shasum -a 256 "$candidate_runtime" "$candidate_component" \
        > "$evidence_root/candidate-sha256.txt"
    node "$repo_root/eng/compare-collector-size.mjs" \
        "$component" "$candidate_component" "$evidence_root/collector-comparison.json"
fi

wc -c "$corelib" "$assembly" "$application" "$runtime" "$command_wit" "$component" \
    > "$evidence_root/byte-sizes.txt"
shasum -a 256 "$corelib" "$assembly" "$application" "$runtime" "$layout" \
    "$command_wit" "$component" > "$evidence_root/artifact-sha256.txt"
if [[ -n "$(git status --porcelain)" ]] || \
   [[ "$(git rev-parse HEAD)" != "$(< "$evidence_root/source-head.txt")" ]]; then
    echo 'source worktree changed during size measurement' >&2
    exit 5
fi

printf 'component_bytes=%s\n' "$component_bytes"

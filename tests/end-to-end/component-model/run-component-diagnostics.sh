#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_COMPONENT_DIAGNOSTICS_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/component-diagnostics}"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/nwc.dll"
wit_bindgen="$artifact_root/wit-tool/bin/netwasm-wit-bindgen"
scalar_wit="$repo_root/tests/NetWasm.Compiler.Tests/Fixtures/scalar-component.wit"

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --no-restore --nologo \
    > "$artifact_root/build.log"
rm -rf "$artifact_root/wit-tool"
bash "$repo_root/eng/prepare-wit-bindings-test-tool.sh" "$artifact_root/wit-tool"

expect_failure() {
    local expected="$1"
    local output="$2"
    local executable="$3"
    shift 3
    rm -f "$output" "$output.manifest"
    local launcher=(dotnet "$executable")
    [[ "$executable" == *.dll ]] || launcher=("$executable")
    if "${launcher[@]}" "$@" \
        > "$artifact_root/command.stdout" \
        2> "$artifact_root/command.stderr"; then
        echo "command unexpectedly succeeded" >&2
        exit 1
    fi
    local diagnostic
    # nwc writes diagnostics to standard output, as csc does; the WIT bindings
    # generator writes them to standard error.
    diagnostic="$(cat "$artifact_root/command.stdout" "$artifact_root/command.stderr")"
    case "$diagnostic" in
        *"$expected"*) ;;
        *)
            echo "missing diagnostic '$expected': $diagnostic" >&2
            exit 1
            ;;
    esac
    [[ ! -e "$output" && ! -e "$output.manifest" ]] || {
        echo "failed command left a partial output at '$output'" >&2
        exit 1
    }
}

# A world the WIT does not declare fails before any output is written, in the
# bindings generator and before componentize reads its core module.
missing_world="example:scalar/missing@1.0.0"
bindings="$artifact_root/missing-world-bindings.cs"
expect_failure \
    "NW1009: WIT world '$missing_world' was not found or is ambiguous" \
    "$bindings" \
    "$wit_bindgen" --wit "$scalar_wit" --world "$missing_world" --output "$bindings"

component="$artifact_root/missing-world.component.wasm"
expect_failure \
    "NW1009: WIT world '$missing_world' was not found or is ambiguous" \
    "$component" \
    "$compiler" \
    componentize \
    -optimize:none \
    -coremodule:"$artifact_root/not-read-before-validation.wasm" \
    -wit:"$scalar_wit" -world:"$missing_world" -platform:wasm32 \
    -out:"$component" -manifest:"$component.manifest"

mismatched="$artifact_root/mismatched.component.wasm"
expect_failure \
    "interop manifest target 'wasm64' does not match component target 'wasm32'" \
    "$mismatched" \
    "$compiler" \
    componentize \
    -optimize:none \
    -coremodule:"$artifact_root/not-read-before-manifest-validation.wasm" \
    -wit:"$scalar_wit" -platform:wasm32 \
    -interopmanifest:"$repo_root/tests/end-to-end/component-model/fixtures/mismatched-interop.json" \
    -out:"$mismatched" -manifest:"$mismatched.manifest"

printf 'Component Model component diagnostic atomicity PASS\n'

#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/component-diagnostics"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit_bindgen="$repo_root/tools/NetWasm.Wit.Bindings/bin/Release/net10.0/NetWasm.Wit.Bindings.dll"
async_wit="$repo_root/tests/NetWasm.Compiler.Tests/Fixtures/async-component.wit"
scalar_wit="$repo_root/tests/NetWasm.Compiler.Tests/Fixtures/scalar-component.wit"

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --no-restore --nologo \
    > "$artifact_root/build.log"

expect_failure() {
    local expected="$1"
    local output="$2"
    local executable="$3"
    shift 3
    rm -f "$output" "$output.manifest"
    if dotnet "$executable" "$@" \
        > "$artifact_root/command.stdout" \
        2> "$artifact_root/command.stderr"; then
        echo "command unexpectedly succeeded" >&2
        exit 1
    fi
    local diagnostic
    diagnostic="$(<"$artifact_root/command.stderr")"
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

bindings="$artifact_root/async-bindings.cs"
expect_failure \
    "NW1009: user-defined asynchronous WIT function 'run' is not supported" \
    "$bindings" \
    "$wit_bindgen" --wit "$async_wit" --output "$bindings"

component="$artifact_root/async.component.wasm"
expect_failure \
    "NW1009: user-defined asynchronous WIT function 'run' is not supported" \
    "$component" \
    "$compiler" \
    componentize \
    --optimization none \
    --core-module "$artifact_root/not-read-before-validation.wasm" \
    --wit "$async_wit" --target wasm32 \
    --output "$component" --manifest "$component.manifest"

mismatched="$artifact_root/mismatched.component.wasm"
expect_failure \
    "interop manifest target 'wasm64' does not match component target 'wasm32'" \
    "$mismatched" \
    "$compiler" \
    componentize \
    --optimization none \
    --core-module "$artifact_root/not-read-before-manifest-validation.wasm" \
    --wit "$scalar_wit" --target wasm32 \
    --interop-manifest "$repo_root/tests/end-to-end/component-model/fixtures/mismatched-interop.json" \
    --output "$mismatched" --manifest "$mismatched.manifest"

printf 'Component Model component diagnostic atomicity PASS\n'

#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/scalar-component"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/NetWasm.Compiler.Tests/Fixtures/scalar-component.wit"
optimization="${NETWASM_TEST_OPTIMIZATION:-none}"

for tool in cargo dotnet node wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --no-restore --nologo \
    > "$artifact_root/build.log"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/ScalarComponent.dll" \
    "$repo_root/tests/end-to-end/component-model/fixtures/ScalarComponent.cs"

for repeat in first second; do
    dotnet "$compiler" --input "$artifact_root/ScalarComponent.dll" \
        --reference "$corelib" \
        --entry 'NetWasm.Acceptance.Components.ScalarComponent::Run' \
        --wit "$wit" --target wasm32 \
        --output "$artifact_root/application.$repeat.wasm" \
        --runtime-layout "$artifact_root/runtime-layout.$repeat.json"
done
cmp "$artifact_root/application.first.wasm" "$artifact_root/application.second.wasm"
cmp "$artifact_root/runtime-layout.first.json" "$artifact_root/runtime-layout.second.json"

"$repo_root/eng/build-netwasm-runtime.sh" \
    --runtime-layout "$artifact_root/runtime-layout.first.json" \
    --target wasm32 --configuration release \
    --output "$artifact_root/runtime.wasm"

for repeat in first second; do
    dotnet "$compiler" componentize \
        --optimization "$optimization" \
        --core-module "$artifact_root/application.first.wasm" \
        --runtime-module "$artifact_root/runtime.wasm" \
        --wit "$wit" --target wasm32 \
        --output "$artifact_root/component.$repeat.wasm" \
        --manifest "$artifact_root/manifest.$repeat.json"
done
cmp "$artifact_root/component.first.wasm" "$artifact_root/component.second.wasm"
cmp "$artifact_root/manifest.first.json" "$artifact_root/manifest.second.json"
wasm-tools validate "$artifact_root/component.first.wasm" --features all
wasm-tools component wit "$artifact_root/component.first.wasm" \
    > "$artifact_root/component.wit"

result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-scalar-host -- \
    "$artifact_root/component.first.wasm" 41)"
[[ "$result" = 42 ]] || { echo "expected 42, received '$result'" >&2; exit 1; }
if wasmtime run --invoke 'run(41)' "$artifact_root/component.first.wasm" \
    > "$artifact_root/denied.stdout" 2> "$artifact_root/denied.stderr"; then
    echo "component unexpectedly instantiated without its required host service" >&2
    exit 1
fi

printf 'Component Model scalar Preview 2 component acceptance PASS\n'

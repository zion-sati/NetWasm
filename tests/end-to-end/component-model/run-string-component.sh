#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_STRING_COMPONENT_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/string-component}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit_bindgen="$artifact_root/wit-tool/bin/netwasm-wit-bindgen"
wit="$repo_root/tests/NetWasm.Compiler.Tests/Fixtures/string-component.wit"

for tool in cargo dotnet node wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --nologo \
    > "$artifact_root/build.log"
bash "$repo_root/eng/prepare-wit-bindings-test-tool.sh" "$artifact_root/wit-tool"
"$wit_bindgen" --wit "$wit" \
    --output "$artifact_root/StringBindings.cs"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/StringComponent.dll" \
    "$artifact_root/StringBindings.cs" \
    "$repo_root/tests/end-to-end/component-model/fixtures/StringComponent.cs"

for repeat in first second; do
    dotnet "$compiler" --input "$artifact_root/StringComponent.dll" \
        --reference "$corelib" \
        --entry 'NetWasm.Wit.Example.Strings._1._0._0.StringComponent::Run' \
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
node "$repo_root/tests/end-to-end/component-model/test-component-allocator.mjs" \
    "$artifact_root/runtime.wasm" wasm32

for repeat in first second; do
    dotnet "$compiler" --input "$artifact_root/StringComponent.dll" \
        --reference "$corelib" \
        --entry 'NetWasm.Wit.Example.Strings._1._0._0.StringComponent::Run' \
        --wit "$wit" --target wasm64 \
        --output "$artifact_root/application.wasm64.$repeat.wasm" \
        --runtime-layout "$artifact_root/runtime-layout.wasm64.$repeat.json"
done
cmp "$artifact_root/application.wasm64.first.wasm" \
    "$artifact_root/application.wasm64.second.wasm"
cmp "$artifact_root/runtime-layout.wasm64.first.json" \
    "$artifact_root/runtime-layout.wasm64.second.json"
wasm-tools validate "$artifact_root/application.wasm64.first.wasm" --features all
"$repo_root/eng/build-netwasm-runtime.sh" \
    --runtime-layout "$artifact_root/runtime-layout.wasm64.first.json" \
    --target wasm64 --configuration release \
    --output "$artifact_root/runtime.wasm64.wasm"
node "$repo_root/tests/end-to-end/component-model/test-component-allocator.mjs" \
    "$artifact_root/runtime.wasm64.wasm" wasm64

for repeat in first second; do
    dotnet "$compiler" componentize \
        --optimization none \
        --core-module "$artifact_root/application.first.wasm" \
        --runtime-module "$artifact_root/runtime.wasm" \
        --wit "$wit" --target wasm32 \
        --output "$artifact_root/component.$repeat.wasm" \
        --manifest "$artifact_root/manifest.$repeat.json"
done
cmp "$artifact_root/component.first.wasm" "$artifact_root/component.second.wasm"
cmp "$artifact_root/manifest.first.json" "$artifact_root/manifest.second.json"
wasm-tools validate "$artifact_root/component.first.wasm" --features all

value='Hello, Ω and 😀'
result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-string-host -- \
    "$artifact_root/component.first.wasm" "$value")"
[[ "$result" = "$value" ]] || {
    echo "string component returned '$result'" >&2
    exit 1
}

printf 'Component Model string Canonical ABI acceptance PASS\n'

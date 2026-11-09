#!/usr/bin/env bash
# A library component exports a WIT async func. Its generated bindings lift it with a callback,
# and Wasmtime invokes it: the export awaits a delay and an async import, then returns its
# string through task.return.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_ASYNC_EXPORT_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/async-export}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/nwc.dll"
wit_bindgen="$artifact_root/wit-tool/bin/netwasm-wit-bindgen"
world="netwasm:test-async/async-export@1.0.0"

for tool in dotnet node wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || {
        echo "missing required tool: $tool" >&2
        exit 1
    }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]

rm -rf "$artifact_root/wit"
mkdir -p "$artifact_root/wit/deps"
cp "$repo_root/tests/end-to-end/component-model/fixtures/async-export-wit/world.wit" "$artifact_root/wit/"
cp -r "$repo_root/wit/wasi-0.3.2/clocks" "$artifact_root/wit/deps/wasi-clocks"
dotnet build "$repo_root/NetWasm.slnx" -c Release --nologo > "$artifact_root/build.log"
bash "$repo_root/eng/prepare-wit-bindings-test-tool.sh" "$artifact_root/wit-tool"
"$wit_bindgen" --wit "$artifact_root/wit" --world "$world" \
    --output "$artifact_root/AsyncExportBindings.cs"

csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/AsyncExportComponent.dll" \
    "$artifact_root/AsyncExportBindings.cs" \
    "$repo_root/tests/end-to-end/component-model/fixtures/AsyncExportComponent.cs"
dotnet "$compiler" "$artifact_root/AsyncExportComponent.dll" \
    -r:"$corelib" -target:function \
    -entry:'NetWasm.Wit.Netwasm.Test.Async._1._0._0.AsyncExportExports::Run' \
    -wit:"$artifact_root/wit" -world:"$world" -platform:wasm32 \
    -out:"$artifact_root/application.wasm" \
    -runtimelayout:"$artifact_root/runtime-layout.json"
"$repo_root/eng/build-netwasm-runtime.sh" \
    --runtime-layout "$artifact_root/runtime-layout.json" \
    --target wasm32 --configuration release \
    --output "$artifact_root/runtime.wasm"
dotnet "$compiler" componentize -optimize:none \
    -coremodule:"$artifact_root/application.wasm" \
    -runtimemodule:"$artifact_root/runtime.wasm" \
    -wit:"$artifact_root/wit" -world:"$world" -platform:wasm32 \
    -out:"$artifact_root/component.wasm" \
    -manifest:"$artifact_root/manifest.json"
wasm-tools validate "$artifact_root/component.wasm" --features all
wasm-tools print "$artifact_root/component.wasm" | grep -q 'async (callback' || {
    echo "the async export is not lifted with a callback" >&2
    exit 1
}

result="$(wasmtime run --invoke 'greet("component", 40)' "$artifact_root/component.wasm")"
[[ "$result" = '"hello component, waited True"' ]] || {
    echo "async export returned '$result'" >&2
    exit 1
}
printf 'Component Model async export acceptance PASS\n'

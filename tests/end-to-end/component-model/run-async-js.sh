#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_COMPONENT_ASYNC_JS_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/async-js}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/async-js-wit"
compatibility_host="$repo_root/src/NetWasm.Runtime/browser-host.mjs"

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --nologo \
    > "$artifact_root/build.log"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/AsyncJavaScriptInterop.dll" \
    "$repo_root/tests/end-to-end/component-model/fixtures/AsyncJavaScriptInterop.cs"

for target in wasm32 wasm64; do
    for repeat in first second; do
        dotnet "$compiler" \
            --input "$artifact_root/AsyncJavaScriptInterop.dll" \
            --reference "$corelib" \
            --entry 'NetWasm.Fixtures.JavaScriptInterop.EntryPoint::Run' \
            --wit "$wit" \
            --target "$target" \
            --output "$artifact_root/application.$target.$repeat.wasm" \
            --interop-manifest "$artifact_root/interop.$target.$repeat.json" \
            --runtime-layout "$artifact_root/runtime-layout.$target.$repeat.json"
    done
    cmp "$artifact_root/application.$target.first.wasm" \
        "$artifact_root/application.$target.second.wasm"
    cmp "$artifact_root/interop.$target.first.json" \
        "$artifact_root/interop.$target.second.json"
    cmp "$artifact_root/runtime-layout.$target.first.json" \
        "$artifact_root/runtime-layout.$target.second.json"
    wasm-tools validate "$artifact_root/application.$target.first.wasm" --features all
    "$repo_root/eng/build-netwasm-runtime.sh" \
        --runtime-layout "$artifact_root/runtime-layout.$target.first.json" \
        --target "$target" --configuration release \
        --output "$artifact_root/runtime.$target.wasm"
    merge_features=(
        --enable-exception-handling --enable-bulk-memory --enable-multimemory
        --enable-nontrapping-float-to-int -g)
    if [[ "$target" = wasm64 ]]; then
        merge_features+=(--enable-memory64)
    fi
    wasm-merge \
        "$artifact_root/application.$target.first.wasm" netwasm.application.v1 \
        "$artifact_root/runtime.$target.wasm" netwasm.runtime.v1 \
        "${merge_features[@]}" \
        -o "$artifact_root/merged.$target.wasm"
    wasm-tools validate "$artifact_root/merged.$target.wasm" --features all

    node "$repo_root/tests/end-to-end/component-model/run-async-js.mjs" \
        "$artifact_root/merged.$target.wasm" \
        "$artifact_root/interop.$target.first.json" \
        "$compatibility_host" \
        "$target"
    node "$repo_root/tests/end-to-end/component-model/run-async-js-playwright.mjs" \
        "$artifact_root/merged.$target.wasm" \
        "$artifact_root/interop.$target.first.json" \
        "$compatibility_host" \
        "$target"
done

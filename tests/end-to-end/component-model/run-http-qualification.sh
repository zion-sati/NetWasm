#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_HTTP_ARTIFACT_ROOT:-$repo_root/tests/end-to-end/component-model/artifacts/http-qualification}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasm_tools_version="$(node -p 'require(process.argv[1]).wasmTools' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
jco_version="$(node -p 'require(process.argv[1]).jco' "$toolchain")"
shim_version="$(node -p 'require(process.argv[1]).preview2Shim' "$toolchain")"
wasi_http_version="$(node -p 'require(process.argv[1]).wasiHttpWit' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
system_net_http="$repo_root/src/NetWasm.System.Net.Http/bin/Release/netwasm0.1/System.Net.Http.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/wasi-http-wit"
fixture="$repo_root/tests/end-to-end/component-model/fixtures/WasiHttpQualificationComponent.cs"
host_manifest="$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml"
host_binary="$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/target/release/netwasm-wasmtime-http-host"
browser_runner="$repo_root/tests/end-to-end/component-model/run-jco-http-qualification.mjs"

for tool in cargo dotnet node npx wasm-tools wasmtime jq shasum; do
    command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 91; }
done
[[ "$(dotnet --version)" = "$sdk_version" ]] || { echo "dotnet version mismatch" >&2; exit 92; }
[[ "$(wasm-tools --version)" = "wasm-tools $wasm_tools_version" ]] || { echo "wasm-tools version mismatch" >&2; exit 92; }
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]] || { echo "wasmtime version mismatch" >&2; exit 92; }
[[ "$(npx --no-install jco --version)" = "$jco_version" ]] || { echo "jco version mismatch" >&2; exit 92; }

emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK_ROOT:-}}"
[[ -n "$emsdk_root" && -x "$emsdk_root/upstream/emscripten/emcc" ]] || {
    echo "missing NETWASM_EMSDK_ROOT/emcc capability" >&2
    exit 91
}

mkdir -p "$artifact_root"
printf '%s\n' "{\"schemaVersion\":1,\"dotnetSdk\":\"$sdk_version\",\"wasmTools\":\"$wasm_tools_version\",\"wasmtime\":\"$wasmtime_version\",\"jco\":\"$jco_version\",\"preview2Shim\":\"$shim_version\",\"wasiHttpWit\":\"$wasi_http_version\"}" > "$artifact_root/toolchain.json"

dotnet build "$repo_root/src/NetWasm.CoreLib/NetWasm.CoreLib.csproj" -c Release --no-restore --nologo \
    > "$artifact_root/corelib-build.stdout" 2> "$artifact_root/corelib-build.stderr"
dotnet build "$repo_root/src/NetWasm.System.Net.Http/NetWasm.System.Net.Http.csproj" -c Release --no-restore --nologo \
    > "$artifact_root/http-build.stdout" 2> "$artifact_root/http-build.stderr"
dotnet build "$repo_root/src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj" -c Release --no-restore --nologo \
    > "$artifact_root/compiler-build.stdout" 2> "$artifact_root/compiler-build.stderr"
cargo build --quiet --locked --release --manifest-path "$host_manifest" --bin netwasm-wasmtime-http-host \
    > "$artifact_root/host-build.stdout" 2> "$artifact_root/host-build.stderr"
[[ -x "$host_binary" ]] || { echo "native host build did not produce an executable" >&2; exit 91; }

for configuration in debug release; do
    dotnet test "$repo_root/tests/NetWasm.SystemNetHttp.Tests/NetWasm.SystemNetHttp.Tests.csproj" \
        -c "$configuration" --no-restore --nologo --filter 'FullyQualifiedName~HttpTransportContractTests|FullyQualifiedName~Wasi02ActorContractTests' \
        > "$artifact_root/desktop-$configuration.stdout" 2> "$artifact_root/desktop-$configuration.stderr"
done

csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"

for configuration in debug release; do
    if [[ "$configuration" = debug ]]; then
        optimize_flag=-optimize-
        debug_flag=-debug:portable
    else
        optimize_flag=-optimize+
        debug_flag=-debug-
    fi
    fixture_dll="$artifact_root/WasiHttpQualificationComponent.$configuration.dll"
    dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
        -warnaserror+ -nullable:enable -deterministic+ "$optimize_flag" "$debug_flag" \
        -target:library -runtimemetadataversion:v4.0.30319 \
        -reference:"$corelib" -reference:"$system_net_http" \
        -out:"$fixture_dll" "$fixture" \
        > "$artifact_root/$configuration-csc.stdout" 2> "$artifact_root/$configuration-csc.stderr"

    for target in wasm32 wasm64; do
        prefix="$artifact_root/$configuration.$target"
        dotnet "$compiler" --input "$fixture_dll" --reference "$corelib" \
            --reference "$system_net_http" \
            --entry 'NetWasm.Fixtures.ComponentModel.WasiHttpQualificationComponent::Run' \
            --wit "$wit" --world 'netwasm:http-spike@1.0.0/test-http-spike' \
            --target "$target" --output "$prefix.core.wasm" --runtime-layout "$prefix.layout.json" \
            > "$prefix.compile.stdout" 2> "$prefix.compile.stderr"
        wasm-tools validate "$prefix.core.wasm" --features all \
            > "$prefix.core.validate.stdout" 2> "$prefix.core.validate.stderr"
        NETWASM_EMSDK_ROOT="$emsdk_root" TMPDIR="$artifact_root/runtime-tmp" \
            "$repo_root/eng/build-netwasm-runtime.sh" \
            --runtime-layout "$prefix.layout.json" --target "$target" \
            --configuration release --force-component-collection --output "$prefix.runtime.wasm" \
            > "$prefix.runtime.stdout" 2> "$prefix.runtime.stderr"
        wasm-tools validate "$prefix.runtime.wasm" --features all \
            > "$prefix.runtime.validate.stdout" 2> "$prefix.runtime.validate.stderr"

        if [[ "$target" = wasm64 ]]; then
            if dotnet "$compiler" componentize \
                --optimization none \
                --core-module "$prefix.core.wasm" --runtime-module "$prefix.runtime.wasm" \
                --wit "$wit" --world test-http-spike --target "$target" \
                --jco-version "$jco_version" --preview2-shim-version "$shim_version" \
                --output "$prefix.component.wasm" --manifest "$prefix.manifest.json" \
                > "$prefix.componentize.stdout" 2> "$prefix.componentize.stderr"; then
                echo "wasm64 componentize unexpectedly succeeded" >&2
                exit 93
            fi
            grep -q '^NW1010:' "$prefix.componentize.stderr" || {
                echo "wasm64 componentize did not preserve NW1010 boundary" >&2
                exit 93
            }
            jq -n --arg configuration "$configuration" --arg target "$target" \
                --arg coreSha "$(shasum -a 256 "$prefix.core.wasm" | cut -d' ' -f1)" \
                --arg runtimeSha "$(shasum -a 256 "$prefix.runtime.wasm" | cut -d' ' -f1)" \
                '{schemaVersion:1, configuration:$configuration, target:$target,
                  rawCoreValidated:true, rawRuntimeValidated:true,
                  componentExecution:false, externalBoundary:"cm64p2",
                  packagingDiagnostic:"NW1010", artifacts:{coreWasmSha256:$coreSha,
                  runtimeWasmSha256:$runtimeSha}}' \
                > "$prefix.boundary.evidence.json"
            continue
        fi

        dotnet "$compiler" componentize \
            --optimization none \
            --core-module "$prefix.core.wasm" --runtime-module "$prefix.runtime.wasm" \
            --wit "$wit" --world test-http-spike --target "$target" \
            --jco-version "$jco_version" --preview2-shim-version "$shim_version" \
            --output "$prefix.component.wasm" --manifest "$prefix.manifest.json" \
            > "$prefix.componentize.stdout" 2> "$prefix.componentize.stderr"
        wasm-tools validate "$prefix.component.wasm" --features all \
            > "$prefix.component.validate.stdout" 2> "$prefix.component.validate.stderr"

        manifest="$prefix.manifest.json"
        jq -e --arg v "$wasi_http_version" '
            (.javaScript.imports | length == 0) and
            ([.importedInterfaces[]] | sort == [
              "netwasm:runtime@1.0.0/reactor-host",
              "wasi:clocks@\($v)/monotonic-clock",
              "wasi:http@\($v)/outgoing-handler",
              "wasi:http@\($v)/types",
              "wasi:io@\($v)/error",
              "wasi:io@\($v)/poll",
              "wasi:io@\($v)/streams"]) and
            ([.importedFunctions[]] | all(. | contains("wasi:sockets") | not))
        ' "$manifest" > "$prefix.import-audit.json"
        wasm-tools print "$prefix.component.wasm" > "$prefix.component.print.wat"
        grep -q "wasi:http/outgoing-handler@$wasi_http_version" "$prefix.component.print.wat"
        grep -q "wasi:http/types@$wasi_http_version" "$prefix.component.print.wat"
        ! grep -qE 'wasi:sockets|JSImport|JavaScript' "$prefix.component.print.wat"

        native_output="$prefix.native.stdout"
        "$host_binary" --deny-http "$prefix.component.wasm" \
            > "$prefix.denied.stdout" 2> "$prefix.denied.stderr"
        grep -qx 'WASI HTTP capability denial PASS' "$prefix.denied.stdout"
        "$host_binary" "$prefix.component.wasm" \
            > "$native_output" 2> "$prefix.native.stderr"
        grep -qx 'WASI HTTP qualification component PASS contract=0x3ff counters=0x101010201' "$native_output"

        package="$artifact_root/$configuration.$target.package"
        if [[ -e "$package" ]]; then /usr/bin/trash "$package"; fi
        npx --no-install jco transpile "$prefix.component.wasm" -o "$package" \
            --name http --instantiation async --strict --quiet --bindgen-enable-wasm-exnref \
            > "$prefix.jco.stdout" 2> "$prefix.jco.stderr"
        grep -q "wasi:http/outgoing-handler@$wasi_http_version" "$package/http.js"
        grep -q "wasi:http/types@$wasi_http_version" "$package/http.js"
        ! grep -qE 'wasi:sockets|JSImport|JavaScript' "$package/http.js"
        node "$browser_runner" "$package" > "$prefix.browser.stdout" 2> "$prefix.browser.stderr"
        grep -qx 'http qualification jco Chromium PASS contract=0x3ff counters=0x101010201' "$prefix.browser.stdout"

        core_sha="$(shasum -a 256 "$prefix.core.wasm" | cut -d' ' -f1)"
        runtime_sha="$(shasum -a 256 "$prefix.runtime.wasm" | cut -d' ' -f1)"
        component_sha="$(shasum -a 256 "$prefix.component.wasm" | cut -d' ' -f1)"
        jq -n --arg configuration "$configuration" --arg target "$target" \
            --arg coreSha "$core_sha" --arg runtimeSha "$runtime_sha" --arg componentSha "$component_sha" \
            --arg nativeSha "$(shasum -a 256 "$native_output" | cut -d' ' -f1)" \
            --arg browserSha "$(shasum -a 256 "$prefix.browser.stdout" | cut -d' ' -f1)" \
            '{schemaVersion:1, configuration:$configuration, target:$target,
              nativeWasmtime:"passed", chromiumJco:"passed", capabilityDenial:"passed",
              semantics:{contract:"0x3ff", counters:"0x101010201", delayedHeaders:true,
              streamingChunks:2, cancellation:true, disposal:true, errorMapping:true,
              headerRejection:true, exactlyOnceWakeAndRelease:true},
              artifacts:{coreWasmSha256:$coreSha, runtimeWasmSha256:$runtimeSha,
              componentWasmSha256:$componentSha, nativeObservationSha256:$nativeSha,
              browserObservationSha256:$browserSha}}' \
            > "$prefix.execution.evidence.json"
    done
done

jq -s '.' "$artifact_root"/*.evidence.json > "$artifact_root/cell-evidence.json"
jq -n --arg boundary "cm64p2" \
    '{schemaVersion:1, status:"passed-with-external-boundary", executionCells:4,
      rawValidationCells:2, wasm64PackagingBoundary:{diagnostic:"NW1010",
      identity:$boundary, executionClaim:false}, noBlockedCellsClaimed:true}' \
    > "$artifact_root/execution-status.json"
if [[ -e "$artifact_root/runtime-tmp" ]]; then /usr/bin/trash "$artifact_root/runtime-tmp"; fi
for package in "$artifact_root"/*.package; do
    [[ -e "$package" ]] || continue
    /usr/bin/trash "$package"
done
printf 'System.Net.Http WASI HTTP H4 PASS (4 executed wasm32 cells; wasm64 raw validation; external cm64p2 NW1010 boundary)\n'

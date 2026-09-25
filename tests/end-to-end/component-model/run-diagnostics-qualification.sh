#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/diagnostics-qualification"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasm_tools_version="$(node -p 'require(process.argv[1]).wasmTools' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
jco_version="$(node -p 'require(process.argv[1]).jco' "$toolchain")"
shim_version="$(node -p 'require(process.argv[1]).preview2Shim' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/diagnostics-wasi-wit"
fixture="$repo_root/tests/end-to-end/component-model/fixtures/DiagnosticsComponent.cs"
oracle="$repo_root/tests/end-to-end/component-model/run-jco-diagnostics-browser.mjs"
desktop_project="$repo_root/compiler-qualification/diagnostics/dp06/desktop-oracle/DiagnosticsDesktopOracle.csproj"

mkdir -p "$artifact_root"
dotnet build "$desktop_project" -c Release --nologo \
    > "$artifact_root/desktop-oracle-build.stdout" \
    2> "$artifact_root/desktop-oracle-build.stderr"
desktop_oracle="$artifact_root/desktop-oracle.json"
desktop_dll="$repo_root/compiler-qualification/diagnostics/dp06/desktop-oracle/bin/Release/net10.0/DiagnosticsDesktopOracle.dll"
dotnet "$desktop_dll" > "$desktop_oracle" 2> "$artifact_root/desktop-oracle.stderr"
jq -e '.status == "passed" and .contract == 63 and ([.checks[]] | all)' \
    "$desktop_oracle" > /dev/null
desktop_contract_hex="$(jq -r '.contract' "$desktop_oracle" | awk '{printf "0x%x", $1}')"

for tool in cargo dotnet node npx wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || {
        printf 'capability-boundary=missing-tool:%s\n' "$tool" >&2
        exit 91
    }
done
[[ "$(dotnet --version)" = "$sdk_version" ]] || {
    printf 'capability-boundary=dotnet-version\n' >&2
    exit 92
}
[[ "$(wasm-tools --version)" = "wasm-tools $wasm_tools_version" ]] || {
    printf 'capability-boundary=wasm-tools-version\n' >&2
    exit 92
}
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]] || {
    printf 'capability-boundary=wasmtime-version\n' >&2
    exit 92
}
[[ "$(npx --no-install jco --version)" = "$jco_version" ]] || {
    printf 'capability-boundary=jco-version\n' >&2
    exit 92
}
emsdk_root="${NETWASM_EMSDK_ROOT:-${EMSDK_ROOT:-}}"
[[ -n "$emsdk_root" && -x "$emsdk_root/upstream/emscripten/emcc" ]] || {
    printf '%s\n' '{"schemaVersion":1,"status":"blocked","capabilityBoundary":"emscripten-runtime-packaging","executedCells":0,"requiredCells":8,"desktopOracle":"desktop-oracle.json"}' > "$artifact_root/execution-status.json"
    printf 'capability-boundary=emscripten-runtime-packaging\n' >&2
    exit 91
}

executed_cells=0
blocked_cells=0

dotnet build "$repo_root/src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj" \
    -c Release --no-restore --nologo \
    > "$artifact_root/build.stdout" 2> "$artifact_root/build.stderr"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
host_manifest="$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml"
cargo build --quiet --locked --release --manifest-path "$host_manifest" \
    --bin netwasm-wasmtime-diagnostics-host \
    > "$artifact_root/host-build.stdout" 2> "$artifact_root/host-build.stderr"
host_binary="$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/target/release/netwasm-wasmtime-diagnostics-host"
[[ -x "$host_binary" ]] || {
    printf 'capability-boundary=native-host-build\n' >&2
    exit 91
}

for configuration in debug release; do
    if [[ "$configuration" = debug ]]; then
        optimize_flag=-optimize-
        debug_flag=-debug:portable
    else
        optimize_flag=-optimize+
        debug_flag=-debug-
    fi
    fixture_dll="$artifact_root/DiagnosticsComponent.$configuration.dll"
    dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
        -warnaserror+ -nullable:enable -deterministic+ "$optimize_flag" "$debug_flag" \
        -target:library -runtimemetadataversion:v4.0.30319 \
        -reference:"$corelib" -out:"$fixture_dll" "$fixture" \
        > "$artifact_root/$configuration-csc.stdout" \
        2> "$artifact_root/$configuration-csc.stderr"

    for target in wasm32 wasm64; do
        prefix="$artifact_root/$configuration.$target"
        dotnet "$compiler" --input "$fixture_dll" --reference "$corelib" \
            --entry 'NetWasm.Diagnostics.Components.DiagnosticsComponent::Run' \
            --wit "$wit" --target "$target" \
            --output "$prefix.core.wasm" --runtime-layout "$prefix.layout.json" \
            > "$prefix.compile.stdout" 2> "$prefix.compile.stderr"
        wasm-tools validate "$prefix.core.wasm" --features all \
            > "$prefix.core.validate.stdout" 2> "$prefix.core.validate.stderr"
        "$repo_root/eng/build-netwasm-runtime.sh" \
            --runtime-layout "$prefix.layout.json" --target "$target" \
            --configuration release --force-component-collection \
            --output "$prefix.runtime.wasm" \
            > "$prefix.runtime.stdout" 2> "$prefix.runtime.stderr"
        if ! dotnet "$compiler" componentize \
            --optimization none \
            --core-module "$prefix.core.wasm" --runtime-module "$prefix.runtime.wasm" \
            --wit "$wit" --target "$target" --jco-version "$jco_version" \
            --preview2-shim-version "$shim_version" \
            --output "$prefix.component.wasm" --manifest "$prefix.manifest.json" \
            > "$prefix.componentize.stdout" 2> "$prefix.componentize.stderr"; then
            if [[ "$target" = wasm64 ]] && grep -q '^NW1010:' "$prefix.componentize.stderr"; then
                blocked_cells=$((blocked_cells + 2))
                printf 'capability-boundary=cm64p2-component-encoder configuration=%s\n' "$configuration" >&2
                continue
            fi
            printf 'execution-failure=componentize:%s:%s\n' "$configuration" "$target" >&2
            exit 93
        fi
        wasm-tools validate "$prefix.component.wasm" --features all \
            > "$prefix.component.validate.stdout" 2> "$prefix.component.validate.stderr"

        native_result="$($host_binary "$prefix.component.wasm" 2> "$prefix.native.stderr" || true)"
        printf '%s\n' "$native_result" > "$prefix.native.stdout"
        [[ "$native_result" = "diagnostics native component PASS contract=$desktop_contract_hex" ]] || {
            printf 'execution-failure=native:%s:%s\n' "$configuration" "$target" >&2
            exit 93
        }
        executed_cells=$((executed_cells + 1))

        package="$artifact_root/$configuration.$target.package"
        rm -rf "$package"
        npx --no-install jco transpile "$prefix.component.wasm" \
            -o "$package" --name diagnostics --instantiation async --strict --quiet \
            --bindgen-enable-wasm-exnref \
            > "$prefix.jco.stdout" 2> "$prefix.jco.stderr"
        node "$oracle" "$package" "$desktop_oracle" > "$prefix.browser.stdout" 2> "$prefix.browser.stderr"
        browser_result="$(tr -d '\n' < "$prefix.browser.stdout")"
        [[ "$browser_result" = "diagnostics jco browser PASS contract=$desktop_contract_hex" ]] || {
            printf 'execution-failure=browser:%s:%s\n' "$configuration" "$target" >&2
            exit 93
        }
        executed_cells=$((executed_cells + 1))

        core_sha="$(shasum -a 256 "$prefix.core.wasm" | cut -d' ' -f1)"
        runtime_sha="$(shasum -a 256 "$prefix.runtime.wasm" | cut -d' ' -f1)"
        component_sha="$(shasum -a 256 "$prefix.component.wasm" | cut -d' ' -f1)"
        jq -n \
            --arg configuration "$configuration" --arg target "$target" \
            --arg host "native-component" --arg contract "$desktop_contract_hex" \
            --arg coreSha "$core_sha" --arg runtimeSha "$runtime_sha" \
            --arg componentSha "$component_sha" \
            '{schemaVersion: 1, configuration, target, host, status: "passed", evidence: true,
              semanticContract: $contract, artifacts: {coreWasmSha256: $coreSha,
              runtimeWasmSha256: $runtimeSha, componentWasmSha256: $componentSha,
              nativeObservationSha256: ""}}' \
            > "$prefix.native.evidence.json"
        native_observation_sha="$(shasum -a 256 "$prefix.native.stdout" | cut -d' ' -f1)"
        jq --arg sha "$native_observation_sha" '.artifacts.nativeObservationSha256 = $sha' \
            "$prefix.native.evidence.json" > "$prefix.native.evidence.tmp.json"
        mv "$prefix.native.evidence.tmp.json" "$prefix.native.evidence.json"
        browser_observation_sha="$(shasum -a 256 "$prefix.browser.stdout" | cut -d' ' -f1)"
        jq -n \
            --arg configuration "$configuration" --arg target "$target" \
            --arg host "browser-jco" --arg contract "$desktop_contract_hex" \
            --arg coreSha "$core_sha" --arg runtimeSha "$runtime_sha" \
            --arg componentSha "$component_sha" --arg browserSha "$browser_observation_sha" \
            '{schemaVersion: 1, configuration, target, host, status: "passed", evidence: true,
              semanticContract: $contract, artifacts: {coreWasmSha256: $coreSha,
              runtimeWasmSha256: $runtimeSha, componentWasmSha256: $componentSha,
              browserObservationSha256: $browserSha}}' \
            > "$prefix.browser.evidence.json"
    done
done

jq -s '.' "$artifact_root"/*.evidence.json > "$artifact_root/cell-evidence.json"

matrix_status=blocked
capability_boundary=cm64p2-component-encoder
if [[ "$executed_cells" -eq 8 ]]; then
    matrix_status=passed
    capability_boundary=none
fi
printf '{"schemaVersion":1,"status":"%s","capabilityBoundary":"%s","executedCells":%d,"blockedCells":%d,"requiredCells":8}\n' \
    "$matrix_status" "$capability_boundary" "$executed_cells" "$blocked_cells" \
    > "$artifact_root/execution-status.json"
if [[ "$matrix_status" = passed ]]; then
    printf 'diagnostics host matrix PASS (8 cells)\n'
else
    printf 'diagnostics host matrix PARTIAL (%d/8 cells; blocked=%d)\n' "$executed_cells" "$blocked_cells"
fi

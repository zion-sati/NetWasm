#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_WASI_STREAM_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/wasi-stream}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/wasi-stream-wit"

for tool in cargo dotnet node wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || {
        echo "missing required tool: $tool" >&2
        exit 1
    }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --no-restore --nologo \
    > "$artifact_root/build.log"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -unsafe+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/NetWasm.WasiStreamFixture.dll" \
    "$repo_root/tests/end-to-end/component-model/fixtures/WasiStreamComponent.cs"

entry='NetWasm.Fixtures.ComponentModel.WasiStreamComponent::Run'
for target in wasm32 wasm64; do
    for repeat in first second; do
        dotnet "$compiler" \
            --input "$artifact_root/NetWasm.WasiStreamFixture.dll" \
            --reference "$corelib" --entry "$entry" --wit "$wit" \
            --target "$target" \
            --output "$artifact_root/application.$target.$repeat.wasm" \
            --runtime-layout "$artifact_root/runtime-layout.$target.$repeat.json"
    done
    cmp "$artifact_root/application.$target.first.wasm" \
        "$artifact_root/application.$target.second.wasm"
    cmp "$artifact_root/runtime-layout.$target.first.json" \
        "$artifact_root/runtime-layout.$target.second.json"
    wasm-tools validate "$artifact_root/application.$target.first.wasm" --features all
    wasm-tools print "$artifact_root/application.$target.first.wasm" \
        > "$artifact_root/application.$target.wat"
    "$repo_root/eng/build-netwasm-runtime.sh" \
        --runtime-layout "$artifact_root/runtime-layout.$target.first.json" \
        --target "$target" --configuration release \
        --force-component-collection \
        --output "$artifact_root/runtime.$target.wasm"
done

for target in wasm32 wasm64; do
    node - "$artifact_root/application.$target.wat" "$target" <<'JS'
const fs = require('node:fs');
const [watPath, target] = process.argv.slice(2);
const wat = fs.readFileSync(watPath, 'utf8');
const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
const required = [
  `${prefix}|wasi:cli/stdout@0.2`,
  `${prefix}|wasi:io/error@0.2`,
  `${prefix}|wasi:io/streams@0.2`,
  '[method]output-stream.check-write',
  '[method]output-stream.write',
  'output-stream_drop',
];
const missing = required.filter(value => !wat.includes(value));
if (missing.length !== 0) {
  throw new Error(`missing required WASI stream imports: ${missing.join(', ')}`);
}
const forbidden = [
  'wasi:clocks',
  'wasi:random',
  'wasi:filesystem',
  'wasi:http',
  'wasi:sockets',
];
const retained = forbidden.filter(value => wat.includes(value));
if (retained.length !== 0) {
  throw new Error(`unrequested WASI capabilities retained: ${retained.join(', ')}`);
}
JS
done

for repeat in first second; do
    dotnet "$compiler" componentize \
        --optimization none \
        --core-module "$artifact_root/application.wasm32.first.wasm" \
        --runtime-module "$artifact_root/runtime.wasm32.wasm" \
        --wit "$wit" --target wasm32 \
        --output "$artifact_root/component.$repeat.wasm" \
        --manifest "$artifact_root/manifest.$repeat.json"
done
cmp "$artifact_root/component.first.wasm" "$artifact_root/component.second.wasm"
cmp "$artifact_root/manifest.first.json" "$artifact_root/manifest.second.json"
wasm-tools validate "$artifact_root/component.first.wasm" --features all

allowed_result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-stream-host -- \
    "$artifact_root/component.first.wasm")"
[[ "$allowed_result" = "WASI Preview 2 stream component PASS" ]]

denied_result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-stream-host -- \
    --deny "$artifact_root/component.first.wasm")"
[[ "$denied_result" = "WASI stream capability denial PASS" ]]

printf 'Component Model WASI Preview 2 stream acceptance PASS\n'

#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/reactor-component"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/async-reactor-wit"

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
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/AsyncReactorComponent.dll" \
    "$repo_root/tests/end-to-end/component-model/fixtures/AsyncReactorComponent.cs"

entry='NetWasm.Fixtures.ComponentModel.AsyncReactorComponent::Run'
for target in wasm32 wasm64; do
    for repeat in first second; do
        dotnet "$compiler" \
            --input "$artifact_root/AsyncReactorComponent.dll" \
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
    "$repo_root/eng/build-netwasm-runtime.sh" \
        --runtime-layout "$artifact_root/runtime-layout.$target.first.json" \
        --target "$target" --configuration release \
        --force-component-collection \
        --output "$artifact_root/runtime.$target.wasm"
done

wasm-tools print "$artifact_root/application.wasm32.first.wasm" | node -e '
let text = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", chunk => text += chunk);
process.stdin.on("end", () => {
  const expected = [
    "cm32p2|wasi:clocks/monotonic-clock@0.2",
    "cm32p2|netwasm:runtime/reactor-host@1",
  ];
  for (const value of expected) {
    if (!text.includes(value)) throw new Error(`missing exact import ${value}`);
  }
  const forbidden = [
    "wasi:random",
    "wasi:filesystem",
    "wasi:http",
    "wasi:sockets",
    "wasi:cli",
    "wasi:io/streams",
    "wasi:clocks/wall-clock",
  ];
  for (const value of forbidden) {
    if (text.includes(value)) throw new Error(`retained unrequested import ${value}`);
  }
});'

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

result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-reactor-host -- \
    "$artifact_root/component.first.wasm")"
[[ "$result" = "pollable reactor component PASS" ]] || {
    echo "pollable reactor component returned '$result'" >&2
    exit 1
}

denied_result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-reactor-host -- \
    --deny-clock "$artifact_root/component.first.wasm")"
[[ "$denied_result" = "WASI clock capability denial PASS" ]] || {
    echo "clock denial returned '$denied_result'" >&2
    exit 1
}

printf 'Component Model pollable reactor acceptance PASS\n'

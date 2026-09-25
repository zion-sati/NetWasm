#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="$repo_root/tests/end-to-end/component-model/artifacts/jco-wasi-browser"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
jco_version="$(node -p 'require(process.argv[1]).jco' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/tests/end-to-end/component-model/fixtures/browser-wasi-wit"

for tool in cargo dotnet node npx wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || {
        echo "missing required tool: $tool" >&2
        exit 1
    }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]
[[ "$(npx jco --version)" = "$jco_version" ]]

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --no-restore --nologo \
    > "$artifact_root/build.log"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/BrowserWasiComponent.dll" \
    "$repo_root/tests/end-to-end/component-model/fixtures/BrowserWasiComponent.cs"

entry='NetWasm.Fixtures.ComponentModel.BrowserWasiComponent::Run'
for target in wasm32 wasm64; do
    for repeat in first second; do
        dotnet "$compiler" \
            --input "$artifact_root/BrowserWasiComponent.dll" \
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
  const required = [
    "cm32p2|wasi:random/random@0.2",
    "get-random-u64",
  ];
  for (const value of required) {
    if (!text.includes(value)) throw new Error(`missing exact import ${value}`);
  }
  const forbidden = [
    "get-random-bytes",
    "wasi:clocks",
    "wasi:io",
    "wasi:cli",
    "wasi:filesystem",
    "wasi:http",
    "wasi:sockets",
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
        --jco-version "$jco_version" \
        --preview2-shim-version "$(node -p 'require(process.argv[1]).preview2Shim' "$toolchain")" \
        --output "$artifact_root/component.$repeat.wasm" \
        --manifest "$artifact_root/manifest.$repeat.json"
done
cmp "$artifact_root/component.first.wasm" "$artifact_root/component.second.wasm"
cmp "$artifact_root/manifest.first.json" "$artifact_root/manifest.second.json"
wasm-tools validate "$artifact_root/component.first.wasm" --features all

native_result="$(cargo run --quiet --locked --release \
    --manifest-path "$repo_root/tests/end-to-end/component-model/hosts/wasmtime-scalar/Cargo.toml" \
    --bin netwasm-wasmtime-wasi-host -- \
    "$artifact_root/component.first.wasm")"
[[ "$native_result" = "WASI Preview 2 component PASS" ]] || {
    echo "WASI component returned '$native_result'" >&2
    exit 1
}

mkdir -p "$artifact_root/package.first" "$artifact_root/package.second"
for repeat in first second; do
    npx jco transpile "$artifact_root/component.first.wasm" \
        -o "$artifact_root/package.$repeat" --name wasi \
        --instantiation async --strict --quiet
done
for file in wasi.js wasi.d.ts wasi.core.wasm wasi.core2.wasm; do
    cmp "$artifact_root/package.first/$file" \
        "$artifact_root/package.second/$file"
done

node "$repo_root/tests/end-to-end/component-model/run-jco-wasi-browser.mjs" \
    "$artifact_root/package.first"
printf 'Component Model WASI jco browser packaging PASS\n'

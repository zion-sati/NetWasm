#!/usr/bin/env bash
# A wasi:http 0.3 service built from generated bindings answers Wasmtime's serve: the handler
# is a callback-lifted async export whose response body is a stream and whose trailers are a
# future, and the task lives until both are written.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_HTTP_SERVICE_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/http-service}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
wasmtime_version="$(node -p 'require(process.argv[1]).wasmtime' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/nwc.dll"
wit_bindgen="$artifact_root/wit-tool/bin/netwasm-wit-bindgen"
world="wasi:http/service@0.3.2"
port="${NETWASM_HTTP_SERVICE_PORT:-18190}"

for tool in curl dotnet node wasm-tools wasmtime; do
    command -v "$tool" >/dev/null || {
        echo "missing required tool: $tool" >&2
        exit 1
    }
done
[[ "$(dotnet --version)" = "$sdk_version" ]]
[[ "$(wasmtime --version | cut -d' ' -f2)" = "$wasmtime_version" ]]

rm -rf "$artifact_root/wit"
mkdir -p "$artifact_root/wit/deps"
cp "$repo_root"/wit/wasi-0.3.2/http/*.wit "$artifact_root/wit/"
for package in cli clocks filesystem random sockets; do
    cp -r "$repo_root/wit/wasi-0.3.2/$package" "$artifact_root/wit/deps/wasi-$package"
done
dotnet build "$repo_root/NetWasm.slnx" -c Release --nologo > "$artifact_root/build.log"
bash "$repo_root/eng/prepare-wit-bindings-test-tool.sh" "$artifact_root/wit-tool"
"$wit_bindgen" --wit "$artifact_root/wit" --world "$world" \
    --output "$artifact_root/HttpBindings.cs"

csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
    -warnaserror+ -nullable:enable -deterministic+ -optimize+ -unsafe+ -target:library \
    -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
    -out:"$artifact_root/HttpServiceComponent.dll" \
    "$artifact_root/HttpBindings.cs" \
    "$repo_root/tests/end-to-end/component-model/fixtures/HttpServiceComponent.cs"
dotnet "$compiler" "$artifact_root/HttpServiceComponent.dll" \
    -r:"$corelib" -target:function \
    -entry:'NetWasm.Wit.Wasi.Http._0._3._2.ServiceExports::Run' \
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

wasmtime serve -Sp3 -Scli --addr "127.0.0.1:$port" "$artifact_root/component.wasm" \
    > "$artifact_root/serve.log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
for _ in $(seq 1 120); do
    (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null && break
    sleep 0.25
done
expected='hello from a NetWasm wasi:http handler'
for request in 1 2 3; do
    body="$(curl -sf "http://127.0.0.1:$port/sequential/$request")"
    [[ "$body" = "$expected" ]] || {
        echo "request $request returned '$body'" >&2
        exit 1
    }
done
# Concurrent requests queue behind the one task backpressure admits at a time.
pids=()
for request in 1 2 3 4; do
    curl -sf "http://127.0.0.1:$port/concurrent/$request" > "$artifact_root/concurrent.$request.out" &
    pids+=("$!")
done
for pid in "${pids[@]}"; do wait "$pid"; done
for request in 1 2 3 4; do
    [[ "$(cat "$artifact_root/concurrent.$request.out")" = "$expected" ]]
done
kill "$server" 2>/dev/null || true
wait "$server" 2>/dev/null || true
if grep -q -E 'worker failed|trapped|panicked' "$artifact_root/serve.log"; then
    cat "$artifact_root/serve.log" >&2
    exit 1
fi
printf 'Component Model wasi:http service acceptance PASS\n'

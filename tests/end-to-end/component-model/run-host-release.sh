#!/usr/bin/env bash
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

# A program that reads and writes files, cancels timers and opens files many times over runs to the end
# under NetWasm's JavaScript host in a capped JavaScript heap: the host releases what each finished call
# held (streams, futures, copy buffers, a cancelled wait's timer, resources the program dropped) instead
# of keeping it for the rest of the run. Transpiled with the jco the toolchain ships, whose patched
# bindgen (eng/jco-patches) generates the runtime that keeps those tables.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_HOST_RELEASE_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/host-release}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
harness="$repo_root/eng/size-canary/bin/Release/net10.0/SizeCanary.dll"
modules="$repo_root/node_modules"
[[ -f "$modules/@bytecodealliance/jco/dist/api.js" ]] || {
  echo "pinned jco is missing; run npm ci in $repo_root" >&2
  exit 1
}
patches=("$repo_root"/eng/jco-patches/*/js-component-bindgen-component.core.wasm)
[[ "${#patches[@]}" -eq 1 && -f "${patches[0]}" ]] || {
  echo "expected exactly one patched jco bindgen under eng/jco-patches" >&2
  exit 1
}

mkdir -p "$artifact_root/logs"
dotnet build "$repo_root/src/NetWasm.CoreLib/NetWasm.CoreLib.csproj" -c Release --nologo \
  > "$artifact_root/logs/corelib.stdout" 2> "$artifact_root/logs/corelib.stderr"
dotnet build "$repo_root/eng/size-canary/SizeCanary.csproj" -c Release --nologo \
  > "$artifact_root/logs/harness.stdout" 2> "$artifact_root/logs/harness.stderr"

csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
assembly="$artifact_root/HostRelease.dll"
application="$artifact_root/application.wasm"
runtime="$artifact_root/runtime.wasm"
layout="$artifact_root/runtime-layout.json"
command_wit="$repo_root/src/NetWasm.Toolchain/wit-packages/command.wit.wasm"
component="$artifact_root/HostRelease.wasm"

dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest -warnaserror+ -nullable:enable \
  -deterministic+ -optimize+ -target:exe -runtimemetadataversion:v4.0.30319 "-reference:$corelib" \
  "-out:$assembly" "$repo_root/tests/end-to-end/component-model/fixtures/HostReleaseProgram.cs" \
  > "$artifact_root/logs/csc.stdout" 2> "$artifact_root/logs/csc.stderr"
dotnet "$harness" compile "$repo_root" "$assembly" "$application" "$layout" \
  > "$artifact_root/logs/compile.stdout" 2> "$artifact_root/logs/compile.stderr"
bash "$repo_root/eng/build-netwasm-runtime.sh" --runtime-layout "$layout" \
  --target wasm32 --configuration release --output "$runtime" \
  > "$artifact_root/logs/runtime.stdout" 2> "$artifact_root/logs/runtime.stderr"
dotnet "$harness" package "$application" "$runtime" "$command_wit" "$component" int \
  > "$artifact_root/logs/package.stdout" 2> "$artifact_root/logs/package.stderr"
wasm-tools validate "$component" \
  > "$artifact_root/logs/validate.stdout" 2> "$artifact_root/logs/validate.stderr"

shipped="$artifact_root/shipped-jco/node_modules"
rm -rf "$artifact_root/shipped-jco"
mkdir -p "$shipped/@bytecodealliance"
cp -R "$modules/@bytecodealliance/jco" "$modules/@bytecodealliance/jco-transpile" "$shipped/@bytecodealliance/"
cp "${patches[0]}" "$shipped/@bytecodealliance/jco-transpile/vendor/js-component-bindgen-component.core.wasm"
for entry in "$modules"/* "$modules"/@bytecodealliance/*; do
  name="${entry#"$modules"/}"
  [[ "$name" = @bytecodealliance || -e "$shipped/$name" ]] || ln -s "$entry" "$shipped/$name"
done
jco_output="$artifact_root/jco"
node "$repo_root/src/NetWasm.Hosting.Build/JavaScript/run-jco-transpile.mjs" \
  "$shipped/@bytecodealliance/jco/dist/api.js" "$component" "$jco_output" component "$jco_output.metadata.json" \
  > "$artifact_root/logs/jco.stdout" 2> "$artifact_root/logs/jco.stderr"
node "$repo_root/tests/end-to-end/component-model/run-host-release.mjs" "$jco_output" \
  > "$artifact_root/logs/host.stdout" 2> "$artifact_root/logs/host.stderr" || {
  cat "$artifact_root/logs/host.stderr" >&2
  exit 1
}

printf 'A program reads and writes 400 MB, cancels 50,000 timers and opens 1,000 files in a 128 MB JavaScript heap, and its host waits end without idle polling PASS\n'

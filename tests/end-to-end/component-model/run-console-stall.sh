#!/usr/bin/env bash
# Console output reaches the host as the program writes it and all of it before the program ends,
# as on desktop .NET (issue 224). A command writes a line, then computes without ever waiting again:
# a host that stops the run for running too long still shows the line (Wasmtime with a time limit,
# and NetWasm's JavaScript host in a worker the parent stops once the line arrives). A command writes
# 2,000 lines and returns: every line has reached the sink when the run completes, even through a
# JavaScript host whose reads lag behind the program, and the run still completes when the host
# refuses output past a limit.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_CONSOLE_STALL_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/console-stall}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
harness="$repo_root/eng/size-canary/bin/Release/net10.0/SizeCanary.dll"

mkdir -p "$artifact_root/logs"
dotnet build "$repo_root/src/NetWasm.CoreLib/NetWasm.CoreLib.csproj" -c Release --nologo \
  > "$artifact_root/logs/corelib.stdout" 2> "$artifact_root/logs/corelib.stderr"
dotnet build "$repo_root/eng/size-canary/SizeCanary.csproj" -c Release --nologo \
  > "$artifact_root/logs/harness.stdout" 2> "$artifact_root/logs/harness.stderr"

csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
assembly="$artifact_root/ConsoleStall.dll"
application="$artifact_root/application.wasm"
runtime="$artifact_root/runtime.wasm"
layout="$artifact_root/runtime-layout.json"
command_wit="$repo_root/src/NetWasm.Toolchain/wit-packages/command.wit.wasm"
component="$artifact_root/ConsoleStall.wasm"

dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest -warnaserror+ -nullable:enable \
  -deterministic+ -optimize+ -target:exe -runtimemetadataversion:v4.0.30319 "-reference:$corelib" \
  "-out:$assembly" "$repo_root/tests/end-to-end/component-model/fixtures/ConsoleStallProgram.cs" \
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

# Wasmtime stops the run when its time limit passes; the line is already out.
run_exit=0
wasmtime run -W timeout=5s "$component" \
  > "$artifact_root/logs/wasmtime.stdout" 2> "$artifact_root/logs/wasmtime.stderr" || run_exit=$?
[[ "$run_exit" -ne 0 ]] || { echo 'the stalled command exited under Wasmtime' >&2; exit 1; }
[[ "$(tr -d '\r' < "$artifact_root/logs/wasmtime.stdout")" = stalled ]] || {
  echo "Wasmtime lost the line written before the stall: $(cat "$artifact_root/logs/wasmtime.stdout")" >&2
  exit 1
}

# Transpile with the jco the toolchain ships: the pinned jco with the patched bindgen that
# eng/jco-patches holds, through the SDK's own transpile runner. The patch decides when a
# yielded guest resumes and how the host waits on it, which is what this test is about.
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
shipped="$artifact_root/shipped-jco/node_modules"
rm -rf "$artifact_root/shipped-jco"
mkdir -p "$shipped/@bytecodealliance"
cp -R "$modules/@bytecodealliance/jco" "$modules/@bytecodealliance/jco-transpile" "$shipped/@bytecodealliance/"
cp "${patches[0]}" "$shipped/@bytecodealliance/jco-transpile/vendor/js-component-bindgen-component.core.wasm"
# The copies' other dependencies resolve, by walking up, to links into the pinned install.
for entry in "$modules"/* "$modules"/@bytecodealliance/*; do
  name="${entry#"$modules"/}"
  [[ "$name" = @bytecodealliance || -e "$shipped/$name" ]] || ln -s "$entry" "$shipped/$name"
done
transpile() {
  node "$repo_root/src/NetWasm.Hosting.Build/JavaScript/run-jco-transpile.mjs" \
    "$shipped/@bytecodealliance/jco/dist/api.js" "$1" "$2" component "$2.metadata.json"
}

jco_output="$artifact_root/jco"
transpile "$component" "$jco_output" \
  > "$artifact_root/logs/jco.stdout" 2> "$artifact_root/logs/jco.stderr"
node "$repo_root/tests/end-to-end/component-model/run-console-stall.mjs" "$jco_output" \
  > "$artifact_root/logs/host.stdout" 2> "$artifact_root/logs/host.stderr" || {
  cat "$artifact_root/logs/host.stderr" >&2
  exit 1
}

burst_assembly="$artifact_root/ConsoleBurst.dll"
burst_application="$artifact_root/burst.wasm"
burst_runtime="$artifact_root/burst-runtime.wasm"
burst_layout="$artifact_root/burst-runtime-layout.json"
burst_component="$artifact_root/ConsoleBurst.wasm"
dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest -warnaserror+ -nullable:enable \
  -deterministic+ -optimize+ -target:exe -runtimemetadataversion:v4.0.30319 "-reference:$corelib" \
  "-out:$burst_assembly" "$repo_root/tests/end-to-end/component-model/fixtures/ConsoleBurstProgram.cs" \
  > "$artifact_root/logs/burst-csc.stdout" 2> "$artifact_root/logs/burst-csc.stderr"
dotnet "$harness" compile "$repo_root" "$burst_assembly" "$burst_application" "$burst_layout" \
  > "$artifact_root/logs/burst-compile.stdout" 2> "$artifact_root/logs/burst-compile.stderr"
bash "$repo_root/eng/build-netwasm-runtime.sh" --runtime-layout "$burst_layout" \
  --target wasm32 --configuration release --output "$burst_runtime" \
  > "$artifact_root/logs/burst-runtime.stdout" 2> "$artifact_root/logs/burst-runtime.stderr"
dotnet "$harness" package "$burst_application" "$burst_runtime" "$command_wit" "$burst_component" int \
  > "$artifact_root/logs/burst-package.stdout" 2> "$artifact_root/logs/burst-package.stderr"
wasmtime run "$burst_component" > "$artifact_root/logs/burst-wasmtime.stdout" \
  2> "$artifact_root/logs/burst-wasmtime.stderr"
[[ "$(wc -l < "$artifact_root/logs/burst-wasmtime.stdout" | tr -d ' ')" = 2000 ]] || {
  echo "Wasmtime lost lines of the burst" >&2
  exit 1
}
burst_jco="$artifact_root/burst-jco"
transpile "$burst_component" "$burst_jco" \
  > "$artifact_root/logs/burst-jco.stdout" 2> "$artifact_root/logs/burst-jco.stderr"
node "$repo_root/tests/end-to-end/component-model/run-console-burst.mjs" "$burst_jco" \
  > "$artifact_root/logs/burst-host.stdout" 2> "$artifact_root/logs/burst-host.stderr" || {
  cat "$artifact_root/logs/burst-host.stderr" >&2
  exit 1
}
# A sink that refuses more output closes the program's pipe; the program carries on, as on .NET.
node "$repo_root/tests/end-to-end/component-model/run-console-refused.mjs" "$burst_jco" \
  > "$artifact_root/logs/refused-host.stdout" 2> "$artifact_root/logs/refused-host.stderr" || {
  cat "$artifact_root/logs/refused-host.stderr" >&2
  exit 1
}

printf 'Console output before a stall, before an exit and after the host refuses it PASS\n'

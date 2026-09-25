#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
artifact_root="${NETWASM_COMPONENT_TRIMMING_ARTIFACT_DIR:-$repo_root/tests/end-to-end/component-model/artifacts/trimming}"
toolchain="$repo_root/eng/toolchain.json"
sdk_version="$(node -p 'require(process.argv[1]).dotnetSdk' "$toolchain")"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
wit="$repo_root/wit/netwasm-platform-1.0.0"

mkdir -p "$artifact_root"
dotnet build "$repo_root/NetWasm.slnx" -c Release --nologo \
    > "$artifact_root/build.log"
csc="$(dirname "$(command -v dotnet)")/sdk/$sdk_version/Roslyn/bincore/csc.dll"
for cil_profile in debug release; do
    optimize=-optimize-
    [[ "$cil_profile" = release ]] && optimize=-optimize+
    dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest \
        -warnaserror+ -nullable:enable -deterministic+ "$optimize" -target:library \
        -runtimemetadataversion:v4.0.30319 -reference:"$corelib" \
        -out:"$artifact_root/TrimmingComponent.$cil_profile.dll" \
        "$repo_root/tests/end-to-end/component-model/fixtures/TrimmingComponent.cs"
done

for target in wasm32 wasm64; do
    optimize_features=(
        --enable-exception-handling --enable-bulk-memory --enable-multimemory
        --enable-nontrapping-float-to-int)
    [[ "$target" = wasm64 ]] && optimize_features+=(--enable-memory64)
    merge_features=("${optimize_features[@]}" -g)

    for profile in pure clock; do
        entry="NetWasm.Fixtures.ComponentModel.PureTrimmingComponent::Run"
        wit_arguments=()
        if [[ "$profile" = clock ]]; then
            entry="NetWasm.Fixtures.ComponentModel.ClockTrimmingComponent::Run"
            wit_arguments=(
                --wit "$wit"
                --world "netwasm:platform@1.0.0/platform"
                --export "run=$entry")
        fi
        for repeat in first second; do
            dotnet "$compiler" \
                --input "$artifact_root/TrimmingComponent.release.dll" \
                --reference "$corelib" --entry "$entry" --target "$target" \
                "${wit_arguments[@]}" \
                --output "$artifact_root/$profile.$target.$repeat.wasm" \
                --runtime-layout "$artifact_root/$profile.$target.$repeat.json"
        done
        cmp "$artifact_root/$profile.$target.first.wasm" \
            "$artifact_root/$profile.$target.second.wasm"
        cmp "$artifact_root/$profile.$target.first.json" \
            "$artifact_root/$profile.$target.second.json"
        wasm-tools validate "$artifact_root/$profile.$target.first.wasm" --features all
        wasm-tools print "$artifact_root/$profile.$target.first.wasm" \
            > "$artifact_root/$profile.$target.application.wat"
        "$repo_root/eng/build-netwasm-runtime.sh" \
            --runtime-layout "$artifact_root/$profile.$target.first.json" \
            --target "$target" --configuration release \
            --output "$artifact_root/$profile.$target.runtime.wasm"
        wasm-merge \
            "$artifact_root/$profile.$target.first.wasm" netwasm.application.v1 \
            "$artifact_root/$profile.$target.runtime.wasm" netwasm.runtime.v1 \
            "${merge_features[@]}" \
            -o "$artifact_root/$profile.$target.merged.wasm"
        wasm-opt "$artifact_root/$profile.$target.merged.wasm" -Oz \
            "${optimize_features[@]}" \
            -o "$artifact_root/$profile.$target.final.wasm"
        wasm-tools validate "$artifact_root/$profile.$target.final.wasm" --features all
        wasm-tools print "$artifact_root/$profile.$target.final.wasm" \
            > "$artifact_root/$profile.$target.final.wat"
        node "$repo_root/tests/end-to-end/component-model/run-trimming.mjs" \
            "$artifact_root/$profile.$target.final.wasm" "$target" "$profile" \
            "$artifact_root/$profile.$target.first.wasm" "$artifact_root/$profile.$target.runtime.wasm"
    done
done

for target in wasm32 wasm64; do
    merge_features=(
        --enable-exception-handling --enable-bulk-memory --enable-multimemory
        --enable-nontrapping-float-to-int -g)
    [[ "$target" = wasm64 ]] && merge_features+=(--enable-memory64)
    for profile in pure clock; do
        entry="NetWasm.Fixtures.ComponentModel.PureTrimmingComponent::Run"
        wit_arguments=()
        if [[ "$profile" = clock ]]; then
            entry="NetWasm.Fixtures.ComponentModel.ClockTrimmingComponent::Run"
            wit_arguments=(
                --wit "$wit"
                --world "netwasm:platform@1.0.0/platform"
                --export "run=$entry")
        fi
        dotnet "$compiler" \
            --input "$artifact_root/TrimmingComponent.debug.dll" \
            --reference "$corelib" --entry "$entry" --target "$target" \
            "${wit_arguments[@]}" \
            --output "$artifact_root/$profile.$target.debug.application.wasm" \
            --runtime-layout "$artifact_root/$profile.$target.debug.json"
        "$repo_root/eng/build-netwasm-runtime.sh" \
            --runtime-layout "$artifact_root/$profile.$target.debug.json" \
            --target "$target" --configuration debug \
            --output "$artifact_root/$profile.$target.debug.runtime.wasm"
        wasm-merge \
            "$artifact_root/$profile.$target.debug.application.wasm" netwasm.application.v1 \
            "$artifact_root/$profile.$target.debug.runtime.wasm" netwasm.runtime.v1 \
            "${merge_features[@]}" \
            -o "$artifact_root/$profile.$target.debug.wasm"
        wasm-tools validate "$artifact_root/$profile.$target.debug.wasm" --features all
        node "$repo_root/tests/end-to-end/component-model/run-trimming.mjs" \
            "$artifact_root/$profile.$target.debug.wasm" "$target" "$profile" \
            "$artifact_root/$profile.$target.debug.application.wasm" "$artifact_root/$profile.$target.debug.runtime.wasm"
    done
done

node - "$artifact_root" <<'JS'
const fs = require('node:fs');
const root = process.argv[2];
for (const target of ['wasm32', 'wasm64']) {
  const pure = fs.readFileSync(`${root}/pure.${target}.final.wat`, 'utf8');
  const clock = fs.readFileSync(`${root}/clock.${target}.final.wat`, 'utf8');
  const pureApplication = fs.readFileSync(`${root}/pure.${target}.application.wat`, 'utf8');
  const clockApplication = fs.readFileSync(`${root}/clock.${target}.application.wat`, 'utf8');
  for (const forbidden of [
    'wasi:',
    'netwasm:timezone',
    'netwasm:runtime/reactor',
    'consumer.',
  ]) {
    if (pureApplication.includes(forbidden)) {
      throw new Error(`pure ${target} retained ${forbidden}`);
    }
  }
  for (const forbidden of [
    'wasi:clocks/monotonic-clock',
    'wasi:io',
    'wasi:cli',
    'wasi:random',
    'wasi:filesystem',
    'wasi:http',
    'wasi:sockets',
    'netwasm:timezone',
    'netwasm:runtime/reactor',
    'consumer.',
  ]) {
    if (clockApplication.includes(forbidden)) {
      throw new Error(`clock ${target} retained ${forbidden}`);
    }
  }
  // Final modules may retain native CLI/I/O services, but no other platform
  // family may appear. The execution host separately checks the exact linked
  // import closure against the application plus its freshly built runtime.
  for (const forbidden of [
    'wasi:clocks/monotonic-clock', 'wasi:random', 'wasi:filesystem', 'wasi:http',
    'wasi:sockets', 'netwasm:timezone', 'netwasm:runtime/reactor', 'consumer.',
  ]) {
    if (pure.includes(forbidden) || clock.includes(forbidden))
      throw new Error(`final ${target} retained an unreachable platform family`);
  }
  if (pure.includes('wasi:clocks/'))
    throw new Error(`pure final ${target} retained clocks`);
  for (const forbidden of [
    'component_realloc',
    'component_free',
    'component.realloc',
    'component.post-return',
    `${target === 'wasm64' ? 'cm64p2' : 'cm32p2'}_realloc`,
  ]) {
    if (pureApplication.includes(forbidden)) {
      throw new Error(`pure application ${target} retained ${forbidden}`);
    }
  }
}
JS

printf 'Component Model reachability and final-LTO trimming PASS\n'

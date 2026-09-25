#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
artifacts="${NETWASM_INSTANCE_LIFETIME_ARTIFACT_DIR:-$(mktemp -d "${TMPDIR:-/tmp}/netwasm-instance-lifetime.XXXXXX")}"
sdk_root="${DOTNET_ROOT:-/usr/local/share/dotnet}"
sdk_version="$(node -p "require(process.argv[1]).sdk.version" "$repo_root/global.json")"
csc="$sdk_root/sdk/$sdk_version/Roslyn/bincore/csc.dll"
compiler="$repo_root/src/NetWasm.Compiler.Cli/bin/Release/net10.0/NetWasm.Compiler.Cli.dll"
corelib="$repo_root/src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"
profile="${NETWASM_INSTANCE_LIFETIME_CIL:-all}"
case "$profile" in
  all) configurations=(Debug Release) ;;
  Debug|Release) configurations=("$profile") ;;
  *) echo 'CIL profile must be all, Debug, or Release' >&2; exit 2 ;;
esac
mkdir -p "$artifacts"
dotnet build "$repo_root/src/NetWasm.CoreLib/NetWasm.CoreLib.csproj" -c Release --nologo
dotnet build "$repo_root/src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj" -c Release --nologo

status=0
for configuration in "${configurations[@]}"; do
  optimize=-optimize-
  if [[ "$configuration" == Release ]]; then optimize=-optimize+; fi
  assembly="$artifacts/InstanceLifetime.$configuration.dll"
  dotnet "$csc" -nologo -noconfig -nostdlib -langversion:latest -nullable:enable \
    -warnaserror+ -deterministic+ "$optimize" -target:library \
    -reference:"$corelib" -out:"$assembly" \
    "$repo_root/tests/end-to-end/host-interop/instance-lifetime/InstanceLifetime.cs"
  for target in wasm32 wasm64; do
    set +e
    (
      set -e
      prefix="$artifacts/$configuration-$target"
      dotnet "$compiler" --input "$assembly" --reference "$corelib" \
        --entry 'NetWasm.Fixtures.InstanceLifetime.EntryPoint::Run' \
        --target "$target" --output "$prefix.application.wasm" \
        --interop-manifest "$prefix.manifest.json" --runtime-layout "$prefix.layout.json"
      bash "$repo_root/eng/build-netwasm-runtime.sh" --runtime-layout "$prefix.layout.json" \
        --target "$target" --configuration release --output "$prefix.runtime.wasm"
      features=(--enable-exception-handling --enable-bulk-memory --enable-multimemory \
        --enable-nontrapping-float-to-int)
      if [[ "$target" == wasm64 ]]; then features+=(--enable-memory64); fi
      wasm-merge "$prefix.application.wasm" netwasm.application.v1 \
        "$prefix.runtime.wasm" netwasm.runtime.v1 "${features[@]}" -g -o "$prefix.linked.wasm"
      wasm-tools validate "$prefix.linked.wasm" --features all
      node "$repo_root/tests/end-to-end/host-interop/instance-lifetime/run.mjs" \
        "$prefix.linked.wasm" "$prefix.manifest.json" "$target"
      wasm-opt "$prefix.linked.wasm" -Oz "${features[@]}" --disable-compact-imports \
        -o "$prefix.optimized.wasm"
      wasm-tools validate "$prefix.optimized.wasm" --features all
      node "$repo_root/tests/end-to-end/host-interop/instance-lifetime/run.mjs" \
        "$prefix.optimized.wasm" "$prefix.manifest.json" "$target"
    )
    cell_status=$?
    set -e
    if [[ "$cell_status" != 0 ]]; then status=1; fi
  done
done
exit "$status"

#!/usr/bin/env bash
set -euo pipefail

# Component fixtures must run the packaged tool: its build output intentionally
# lacks the platform assets validated by the normal tool composition root.
if [[ $# != 1 || "$1" != /* || -e "$1" || -L "$1" ]]; then
  echo 'usage: prepare-wit-bindings-test-tool.sh NEW_ABSOLUTE_DIRECTORY' >&2
  exit 2
fi
tool_root="$1"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/tools/NetWasm.Wit.Bindings/NetWasm.Wit.Bindings.csproj"
asset_root="${NETWASM_WIT_TOOL_ASSET_ROOT:-$repo_root/src/NetWasm.Toolchain/platform-assets/wasm-tools}"
[[ "$asset_root" == /* ]] || { echo 'WIT tool asset root must be absolute.' >&2; exit 2; }
for asset in wasm-tools.wasm LICENSE-APACHE LICENSE-Apache-2.0_WITH_LLVM-exception LICENSE-MIT README.md; do
  [[ -f "$asset_root/$asset" ]] || {
    echo 'Stage the pinned wasm-tools assets or set NETWASM_WIT_TOOL_ASSET_ROOT.' >&2
    exit 2
  }
done
mkdir -p "$tool_root/feed" "$tool_root/packages" "$tool_root/platform-assets"
# A restored Toolchain package also contains its runner. The tool project owns
# that runner already; include only the pinned module and distribution notices.
for asset in wasm-tools.wasm LICENSE-APACHE LICENSE-Apache-2.0_WITH_LLVM-exception LICENSE-MIT README.md; do
  cp "$asset_root/$asset" "$tool_root/platform-assets/$asset"
done
base_version="$(dotnet msbuild "$project" -nologo -getProperty:Version)"
[[ "$base_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]] || {
  echo 'The WIT tool project did not return a package version.' >&2
  exit 1
}
# A local-only immutable identity; never overwrite a published package version.
package_version="${base_version%%[-+]*}-testharness.$(date -u +%Y%m%d%H%M%S).$$"
# The caller has just built the solution. Package those verified outputs without
# changing the absolute dependency root captured by its restore.
dotnet pack "$project" -c Release --nologo --no-build --no-restore \
  -p:PackageVersion="$package_version" \
  -p:NetWasmToolchainWasmToolsAssetRoot="$tool_root/platform-assets" --output "$tool_root/feed" \
  > "$tool_root/pack.stdout" 2> "$tool_root/pack.stderr"
NUGET_PACKAGES="$tool_root/packages" dotnet tool install NetWasm.Wit.Bindings.Tool --version "$package_version" \
  --tool-path "$tool_root/bin" --add-source "$tool_root/feed" \
  > "$tool_root/install.stdout" 2> "$tool_root/install.stderr"
[[ -x "$tool_root/bin/netwasm-wit-bindgen" ]]

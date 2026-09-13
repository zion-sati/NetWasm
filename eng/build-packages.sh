#!/usr/bin/env bash

set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
OUTPUT_DIR="${1:-${REPOSITORY_ROOT}/artifacts/packages}"
mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd "${OUTPUT_DIR}" && pwd -P)"

case "${OUTPUT_DIR}" in
  "${REPOSITORY_ROOT}/artifacts"|"${REPOSITORY_ROOT}/artifacts"/*)
    ;;
  "${REPOSITORY_ROOT}"|"${REPOSITORY_ROOT}"/*)
    echo "Package output inside the repository must stay under artifacts/: ${OUTPUT_DIR}" >&2
    exit 2
    ;;
esac

for command in dotnet git node python3; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "Missing required command: ${command}" >&2
    exit 2
  }
done

[[ "$(git -C "${REPOSITORY_ROOT}" rev-parse --is-inside-work-tree 2>/dev/null || true)" == "true" ]] || {
    echo "Package construction requires a Git checkout." >&2
    exit 2
}
[[ -z "$(git -C "${REPOSITORY_ROOT}" status --porcelain=v1)" ]] || {
  echo "Package construction requires a clean checkout." >&2
  exit 2
}

find "${OUTPUT_DIR}" -maxdepth 1 -type f \
  \( -name 'NetWasm.*.nupkg' -o -name 'NetWasm.*.snupkg' \) -delete

build_root="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-core-packages.XXXXXX")"
source_root="${build_root}/source"
cleanup() {
  if [[ -d "${source_root}" ]]; then
    git -C "${REPOSITORY_ROOT}" worktree remove --force "${source_root}" >/dev/null 2>&1 || true
  fi
  rm -rf "${build_root}"
}
trap cleanup EXIT

asset_root="${build_root}/toolchain-assets"
repository_commit="$(git -C "${REPOSITORY_ROOT}" rev-parse HEAD)"
git -C "${REPOSITORY_ROOT}" worktree add --quiet --detach "${source_root}" "${repository_commit}"
export SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-$(git -C "${source_root}" show -s --format='%ct' HEAD)}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${build_root}/packages}"

dotnet restore "${source_root}/NetWasm.slnx" \
  --disable-build-servers \
  --nologo
# The detached worktree isolates source and ignored bin/obj state from the
# caller's checkout. Restore first so Clean resolves analyzers from this run's
# immutable package cache, then clear Release outputs before compiling anything
# that will enter an archive.
dotnet clean "${source_root}/NetWasm.slnx" \
  -c Release \
  --disable-build-servers \
  --nologo

"${source_root}/eng/stage-toolchain-assets.sh" \
  --output "${asset_root}" \
  --cache "${NETWASM_TOOLCHAIN_CACHE:-${build_root}/toolchain-cache}"

jco_root="${asset_root}/jco/payload"
binaryen_root="${asset_root}/binaryen/payload"
bundler_root="${asset_root}/bundler/payload"
wasm_tools_root="${asset_root}/wasm-tools/payload"

dotnet build "${source_root}/NetWasm.slnx" \
  -c Release \
  --no-restore \
  --disable-build-servers \
  --nologo

projects=(
  src/NetWasm.Compiler.Tasks/NetWasm.Compiler.Tasks.csproj
  src/NetWasm.Hosting/NetWasm.Hosting.csproj
  src/NetWasm.Hosting.Build/NetWasm.Hosting.Build.csproj
  src/NetWasm.Ref/NetWasm.Ref.csproj
  src/NetWasm.Runtime.Pack/NetWasm.Runtime.Pack.csproj
  src/NetWasm.Runtime.Wasm32/NetWasm.Runtime.Wasm32.csproj
  src/NetWasm.Runtime.Wasm64/NetWasm.Runtime.Wasm64.csproj
  src/NetWasm.Toolchain/NetWasm.Toolchain.csproj
  src/NetWasm.Sdk/NetWasm.Sdk.csproj
  src/NetWasm.Testing.VSTest/NetWasm.Testing.VSTest.csproj
  src/NetWasm.Templates/NetWasm.Templates.csproj
)

for project in "${projects[@]}"; do
  dotnet pack "${source_root}/${project}" \
    -c Release \
    --no-restore \
    --disable-build-servers \
    --nologo \
    -o "${OUTPUT_DIR}" \
    -p:ContinuousIntegrationBuild=true \
    -p:RepositoryCommit="${repository_commit}" \
    -p:NetWasmSdkProvenanceSourceRevision="${repository_commit}" \
    -p:NetWasmToolchainBinaryenRoot="${binaryen_root}" \
    -p:NetWasmToolchainJcoAssetRoot="${jco_root}" \
    -p:NetWasmToolchainBundlerAssetRoot="${bundler_root}" \
    -p:NetWasmToolchainWasmToolsAssetRoot="${wasm_tools_root}"
done

package_count="$(find "${OUTPUT_DIR}" -maxdepth 1 -type f -name 'NetWasm.*.nupkg' | wc -l | tr -d ' ')"
if [[ "${package_count}" -ne 11 ]]; then
  echo "Expected exactly 11 NetWasm core packages, found ${package_count}." >&2
  exit 1
fi

printf 'Built 11 NetWasm core packages in %s\n' "${OUTPUT_DIR}"

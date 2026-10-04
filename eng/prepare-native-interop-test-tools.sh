#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <new-output-directory>" >&2
  exit 2
fi

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
output_root="$1"
case "${output_root}" in
  /*) ;;
  *) output_root="$(pwd -P)/${output_root}" ;;
esac
[[ ! -e "${output_root}" ]] || {
  echo "Native interop test-tool output already exists: ${output_root}" >&2
  exit 2
}

case "$(uname -s):$(uname -m)" in
  Darwin:arm64) host_rid=osx-arm64 ;;
  Darwin:x86_64) host_rid=osx-x64 ;;
  Linux:aarch64|Linux:arm64) host_rid=linux-arm64 ;;
  Linux:x86_64|Linux:amd64) host_rid=linux-x64 ;;
  *) echo "Unsupported native interop test host: $(uname -s) $(uname -m)" >&2; exit 2 ;;
esac

cache_root="${NETWASM_NATIVE_INTEROP_TOOL_CACHE:-${TMPDIR:-/tmp}/netwasm-native-interop-tool-cache}"
case "${cache_root}" in
  /*) ;;
  *) echo "NETWASM_NATIVE_INTEROP_TOOL_CACHE must be an absolute path." >&2; exit 2 ;;
esac

output_parent="$(dirname "${output_root}")"
output_name="$(basename "${output_root}")"
mkdir -p "${output_parent}" "${cache_root}"
output_parent="$(cd "${output_parent}" && pwd -P)"
work_root="$(mktemp -d "${output_parent}/.${output_name}.XXXXXX")"
cleanup() {
  rm -rf "${work_root}"
}
trap cleanup EXIT

python3 "${repository_root}/eng/stage-host-tools.py" \
  --rid "${host_rid}" \
  --version 0.0.0-test \
  --output "${work_root}/host-tools" \
  --cache "${cache_root}/host-tools" \
  --probe

bash "${repository_root}/eng/stage-toolchain-assets.sh" \
  --output "${work_root}/toolchain-assets" \
  --cache "${cache_root}/toolchain"

mkdir -p "${work_root}/toolchain/tools"
cp -R "${work_root}/toolchain-assets/wasm-tools/payload" \
  "${work_root}/toolchain/tools/wasm-tools"
cp "${repository_root}/src/NetWasm.Toolchain/JavaScript/run-wasm-tools.mjs" \
  "${work_root}/toolchain/tools/wasm-tools/run-wasm-tools.mjs"
cp -R "${work_root}/toolchain-assets/binaryen/payload" \
  "${work_root}/toolchain/tools/binaryen"
rm -rf "${work_root}/toolchain-assets"

for required in \
  "host-tools/tools/bin/node" \
  "host-tools/tools/bin/wasm-ld" \
  "host-tools/tools/bin/wasm-opt" \
  "toolchain/tools/wasm-tools/run-wasm-tools.mjs" \
  "toolchain/tools/wasm-tools/wasm-tools.wasm" \
  "toolchain/tools/binaryen/index.js"; do
  [[ -f "${work_root}/${required}" ]] || {
    echo "Native interop test-tool payload is missing ${required}." >&2
    exit 1
  }
done

mv "${work_root}" "${output_root}"
trap - EXIT
printf 'Prepared native interop test tools in %s\n' "${output_root}"

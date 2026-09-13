#!/usr/bin/env bash

set -euo pipefail

REPOSITORY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
OUTPUT_ROOT=""
CACHE_ROOT="${NETWASM_TOOLCHAIN_CACHE:-${TMPDIR:-/tmp}/netwasm-toolchain-cache}"

usage() {
  cat <<'EOF' >&2
Usage: eng/stage-toolchain-assets.sh --output <new directory> [--cache <directory>]

Materializes the exact platform-neutral assets used to pack NetWasm.Toolchain.
The output directory must not already exist.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      OUTPUT_ROOT="${2:-}"
      shift 2
      ;;
    --cache)
      CACHE_ROOT="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown or incomplete option: $1" >&2
      usage
      exit 2
      ;;
  esac
done

[[ -n "${OUTPUT_ROOT}" ]] || {
  usage
  exit 2
}

case "${OUTPUT_ROOT}" in
  /*) ;;
  *) echo "Toolchain asset output must be an absolute path." >&2; exit 2 ;;
esac
case "${CACHE_ROOT}" in
  /*) ;;
  *) echo "Toolchain asset cache must be an absolute path." >&2; exit 2 ;;
esac
[[ ! -e "${OUTPUT_ROOT}" ]] || {
  echo "Toolchain asset output already exists: ${OUTPUT_ROOT}" >&2
  exit 2
}

for command in python3; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "Missing required command: ${command}" >&2
    exit 2
  }
done

output_parent="$(dirname "${OUTPUT_ROOT}")"
output_name="$(basename "${OUTPUT_ROOT}")"
mkdir -p "${output_parent}" "${CACHE_ROOT}"
output_parent="$(cd "${output_parent}" && pwd -P)"
CACHE_ROOT="$(cd "${CACHE_ROOT}" && pwd -P)"
OUTPUT_ROOT="${output_parent}/${output_name}"

work_root="$(mktemp -d "${output_parent}/.${output_name}.XXXXXX")"
cleanup() {
  if [[ -d "${work_root}" ]]; then
    rm -rf "${work_root}"
  fi
}
trap cleanup EXIT

python3 "${REPOSITORY_ROOT}/eng/stage-jco-closure.py" \
  --lock "${REPOSITORY_ROOT}/package-lock.json" \
  --policy "${REPOSITORY_ROOT}/eng/jco-transpile-closure.json" \
  --generation-root "${work_root}/jco" \
  --cache "${CACHE_ROOT}/npm"

python3 "${REPOSITORY_ROOT}/eng/stage-hosting-bundle-closure.py" \
  --lock "${REPOSITORY_ROOT}/package-lock.json" \
  --policy "${REPOSITORY_ROOT}/eng/hosting-bundle-closure.json" \
  --generation-root "${work_root}/bundler" \
  --cache "${CACHE_ROOT}/npm"

python3 "${REPOSITORY_ROOT}/eng/stage-binaryen.py" \
  --lock "${REPOSITORY_ROOT}/eng/binaryen-package-lock.json" \
  --policy "${REPOSITORY_ROOT}/eng/binaryen-closure.json" \
  --generation-root "${work_root}/binaryen" \
  --cache "${CACHE_ROOT}/npm"

python3 "${REPOSITORY_ROOT}/eng/stage-wasm-tools.py" \
  --generation-root "${work_root}/wasm-tools" \
  --cache "${CACHE_ROOT}/wasm-tools"

binaryen_root="${work_root}/binaryen/payload"
for required in package.json index.js bin/wasm-opt bin/wasm-merge; do
  [[ -f "${binaryen_root}/${required}" ]] || {
    echo "The staged Binaryen closure is missing asset '${required}'." >&2
    exit 1
  }
done

mv "${work_root}" "${OUTPUT_ROOT}"
trap - EXIT
printf 'Staged NetWasm.Toolchain assets in %s\n' "${OUTPUT_ROOT}"

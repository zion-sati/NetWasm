#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: prepare-compiler-qualification-inputs.sh <output-directory>

Creates the writable receipt directory required by linked corpus tests. The
compiler acceptance tests take System.Text.Json and the other framework
libraries from NetWasm.App's in-tree build.
EOF
}

if [[ $# -ne 1 ]]; then
  usage >&2
  exit 64
fi

output_root="$(python3 -c 'import os, sys; print(os.path.abspath(sys.argv[1]))' "$1")"
receipt_root="${output_root}/linked-receipts"

rm -rf "$receipt_root"
mkdir -p "$receipt_root"

printf 'Prepared compiler qualification inputs in %s\n' "$output_root"
